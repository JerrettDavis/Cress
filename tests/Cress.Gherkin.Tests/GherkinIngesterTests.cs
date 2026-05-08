using Cress.Core.Models;
using Cress.Gherkin;
using Cress.Gherkin.Phrases;

namespace Cress.Gherkin.Tests;

public sealed class GherkinIngesterTests
{
    private static GherkinIngester BuildIngester()
        => new(PhraseLibrary.CreateDefault());

    private static FlowToGherkinConverter BuildConverter()
        => new(PhraseLibrary.CreateDefault());

    // -------------------------------------------------------------------------
    // Basic structural parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void Ingest_FeatureLine_SetsName()
    {
        var ingester = BuildIngester();
        var feature = """
            Feature: My Test Flow
              Scenario: My Test Flow
                When I GET https://example.com
                Then the response status should be 200
            """;

        var flow = ingester.Ingest(feature);

        Assert.Equal("My Test Flow", flow.Name);
    }

    [Fact]
    public void Ingest_DescriptionLines_SetsSummary()
    {
        var ingester = BuildIngester();
        var feature = """
            Feature: My Test Flow
              Verifies that the GET endpoint works correctly.

              Scenario: My Test Flow
                When I GET https://example.com
                Then the response status should be 200
            """;

        var flow = ingester.Ingest(feature);

        Assert.NotNull(flow.Summary);
        Assert.Contains("Verifies", flow.Summary);
    }

    [Fact]
    public void Ingest_TagsBeforeFeature_CapturedAsTags()
    {
        var ingester = BuildIngester();
        var feature = """
            @smoke @regression
            Feature: Tagged Flow
              Scenario: Tagged Flow
                When I GET https://example.com
                Then the response status should be 200
            """;

        var flow = ingester.Ingest(feature);

        Assert.Contains("smoke", flow.Tags);
        Assert.Contains("regression", flow.Tags);
    }

    [Fact]
    public void Ingest_TagsBeforeScenario_CapturedAsTags()
    {
        // Tags on the line immediately before Scenario are still harvested
        // because they appear after Feature: which sets state = InFeatureDescription
        var ingester = BuildIngester();
        var feature = """
            @recorded @draft @calculator
            Feature: Calculator: 2 + 2 = 4
              Verifies that Calculator correctly computes 2 + 2 = 4 and displays the result.

              Scenario: Calculator: 2 + 2 = 4
                Given the ApplicationFrameHost application is open
                And I invoke clearButton
                Then the window title should be "REVIEW: replace with expected window title"
            """;

        var flow = ingester.Ingest(feature);

        Assert.Contains("recorded", flow.Tags);
        Assert.Contains("draft", flow.Tags);
        Assert.Contains("calculator", flow.Tags);
    }

    // -------------------------------------------------------------------------
    // Section tracking — And / But inherit previous keyword
    // -------------------------------------------------------------------------

    [Fact]
    public void Ingest_AndAfterWhen_MapsToWhenSection()
    {
        var ingester = BuildIngester();
        var feature = """
            Feature: And Tracking
              Scenario: And Tracking
                When I GET https://example.com
                And I GET https://httpbin.org/get
                Then the response status should be 200
            """;

        var flow = ingester.Ingest(feature);

        // Both GETs should be in When (actions)
        Assert.Equal(2, flow.When.Count);
        Assert.Equal("http.get", flow.When[0].Step);
        Assert.Equal("http.get", flow.When[1].Step);
        Assert.Single(flow.Then);
    }

    [Fact]
    public void Ingest_AndAfterThen_MapsToThenSection()
    {
        var ingester = BuildIngester();
        var feature = """
            Feature: And After Then
              Scenario: And After Then
                When I GET https://example.com
                Then the response status should be 200
                And the response body should contain "ok"
            """;

        var flow = ingester.Ingest(feature);

        Assert.Single(flow.When);
        Assert.Equal(2, flow.Then.Count);
        Assert.Equal("http.assert-status", flow.Then[0].Expect);
        Assert.Equal("http.assert-body-contains", flow.Then[1].Expect);
    }

    [Fact]
    public void Ingest_GivenStep_MapsToWhenSection()
    {
        // Given steps land in the when: section of CressFlow per spec
        var ingester = BuildIngester();
        var feature = """
            Feature: Given Test
              Scenario: Given Test
                Given the ApplicationFrameHost application is open
                When I invoke clearButton
                Then the window title should be "Calculator"
            """;

        var flow = ingester.Ingest(feature);

        Assert.Equal(2, flow.When.Count); // Given + When both in When section
        Assert.Equal("ui.attach", flow.When[0].Step);
        Assert.Equal("ui.invoke", flow.When[1].Step);
    }

    // -------------------------------------------------------------------------
    // Phrase round-trip for each phrase in the default library
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("ui.attach",      "the ApplicationFrameHost application is open", "processName", "ApplicationFrameHost")]
    [InlineData("ui.launch",      "I launch the \"calc.exe\" application",         "application", "calc.exe")]
    [InlineData("ui.close",       "I close the application",                       null,          null)]
    [InlineData("ui.invoke",      "I invoke Clear Button",                         "selector",    "Clear Button")]
    [InlineData("ui.invoke",      "I invoke clearButton",                           "automationId","clearButton")]
    [InlineData("ui.press-key",   "I press Enter",                                  "key",         "Enter")]
    [InlineData("ui.screenshot",  "I take a screenshot",                            null,          null)]
    [InlineData("http.get",       "I GET https://example.com",                      "url",         "https://example.com")]
    [InlineData("http.assert-status", "the response status should be 200",          "status",      "200")]
    [InlineData("http.assert-body-contains", "the response body should contain \"ok\"", "text",   "ok")]
    public void Ingest_DefaultPhrase_RecognisedCorrectly(
        string expectedOp,
        string phraseText,
        string? expectedKey,
        string? expectedValue)
    {
        var ingester = BuildIngester();

        // Construct a minimal feature using that step
        var sectionKeyword = expectedOp.StartsWith("http.assert") || expectedOp == "ui.assert-text"
            || expectedOp == "ui.assert-window-title"
            ? "Then"
            : expectedOp == "ui.attach" ? "Given" : "When";

        // Ensure we always have at least one step in each required section so
        // the flow parses cleanly.
        string feature;
        if (sectionKeyword == "Then")
        {
            feature = $"""
                Feature: Phrase Test
                  Scenario: Phrase Test
                    When I GET https://example.com
                    {sectionKeyword} {phraseText}
                """;
        }
        else
        {
            feature = $"""
                Feature: Phrase Test
                  Scenario: Phrase Test
                    {sectionKeyword} {phraseText}
                    Then the response status should be 200
                """;
        }

        var flow = ingester.Ingest(feature);

        // For Then phrases, check Then; otherwise check When
        if (sectionKeyword == "Then")
        {
            var step = flow.Then.FirstOrDefault(e => e.Expect == expectedOp);
            Assert.NotNull(step);
            if (expectedKey is not null)
            {
                Assert.NotNull(step.With);
                Assert.True(step.With.ContainsKey(expectedKey), $"Expected key '{expectedKey}' in with-block");
                Assert.Equal(expectedValue, step.With[expectedKey]);
            }
        }
        else
        {
            var step = flow.When.FirstOrDefault(a => a.Step == expectedOp);
            Assert.NotNull(step);
            if (expectedKey is not null)
            {
                Assert.NotNull(step.With);
                Assert.True(step.With.ContainsKey(expectedKey), $"Expected key '{expectedKey}' in with-block");
                Assert.Equal(expectedValue, step.With[expectedKey]);
            }
        }
    }

    // -------------------------------------------------------------------------
    // TODO comment stub steps
    // -------------------------------------------------------------------------

    [Fact]
    public void Ingest_TodoCommentLine_ProducesStubStep()
    {
        var ingester = BuildIngester();
        var feature = """
            Feature: Todo Stub
              Scenario: Todo Stub
                When # TODO: custom.my-op
                Then the response status should be 200
            """;

        var flow = ingester.Ingest(feature);

        var stub = flow.When.FirstOrDefault(a => a.Step == "custom.my-op");
        Assert.NotNull(stub);
        Assert.NotNull(stub.With);
        Assert.Equal("TODO: review", stub.With["summary"]);
    }

    // -------------------------------------------------------------------------
    // Round-trip: generate via FlowToGherkinConverter then ingest back
    // -------------------------------------------------------------------------

    [Fact]
    public void RoundTrip_HttpFlow_StepsMatch()
    {
        var converter = BuildConverter();
        var ingester = BuildIngester();

        var originalFlow = new CressFlow
        {
            Version = 1,
            Id = "http.smoke",
            Name = "HTTP Smoke",
            Tags = ["smoke"],
            Summary = "Verifies the HTTP endpoint is alive.",
            When =
            [
                new FlowAction { Step = "http.get", With = new() { ["url"] = "https://httpbin.org/get" } }
            ],
            Then =
            [
                new FlowExpectation { Expect = "http.assert-status", With = new() { ["status"] = "200" } },
                new FlowExpectation
                {
                    Expect = "http.assert-body-contains",
                    With = new() { ["text"] = "httpbin" }
                }
            ]
        };

        var featureText = converter.Convert(originalFlow);
        var reingested = ingester.Ingest(featureText);

        Assert.Equal(originalFlow.Name, reingested.Name);
        Assert.Equal(originalFlow.When.Count, reingested.When.Count);
        Assert.Equal(originalFlow.Then.Count, reingested.Then.Count);
        Assert.Equal("http.get", reingested.When[0].Step);
        Assert.Equal("https://httpbin.org/get", reingested.When[0].With?["url"]);
        Assert.Equal("http.assert-status", reingested.Then[0].Expect);
        Assert.Equal("200", reingested.Then[0].With?["status"]);
    }

    [Fact]
    public void RoundTrip_CalcAddFeatureFile_StepsMatch()
    {
        // Load the v9-generated .feature file and ingest it, then verify steps
        var solutionRoot = FindSolutionRoot();
        var featurePath = Path.Combine(solutionRoot, "artifacts", "v2-features", "v9-calc-add.feature");
        if (!File.Exists(featurePath))
        {
            return; // Skip in CI if file is missing
        }

        var featureText = File.ReadAllText(featurePath);
        var ingester = BuildIngester();
        var flow = ingester.Ingest(featureText);

        Assert.NotEmpty(flow.Name);
        Assert.NotEmpty(flow.When);
        Assert.NotEmpty(flow.Then);
        // Tags should include at least one of the known tags
        Assert.True(flow.Tags.Count > 0, "Expected at least one tag from the feature file");
    }

    [Fact]
    public void RoundTrip_CalcAddFlow_ThenExportThenIngest_StructurallyEquivalent()
    {
        var solutionRoot = FindSolutionRoot();
        var flowFile = Path.Combine(solutionRoot, "specs", "calc-smoke", "flows", "calc-add.flow.yaml");
        if (!File.Exists(flowFile))
        {
            return;
        }

        var parser = new Cress.Specs.FlowParser();
        var parseResult = parser.ParseFile(flowFile);
        Assert.NotNull(parseResult.Value);

        var originalFlow = parseResult.Value!;
        var converter = BuildConverter();
        var ingester = BuildIngester();

        // flow.yaml → .feature
        var featureText = converter.Convert(originalFlow);

        // .feature → CressFlow
        var reingested = ingester.Ingest(featureText);

        // Name must survive the round-trip
        Assert.Equal(originalFlow.Name, reingested.Name);

        // Same number of when steps
        Assert.Equal(originalFlow.When.Count, reingested.When.Count);

        // Same step ops in order
        for (var i = 0; i < originalFlow.When.Count; i++)
        {
            Assert.Equal(originalFlow.When[i].Step, reingested.When[i].Step);
        }

        // Same then expectations in order
        Assert.Equal(originalFlow.Then.Count, reingested.Then.Count);
        for (var i = 0; i < originalFlow.Then.Count; i++)
        {
            Assert.Equal(originalFlow.Then[i].Expect, reingested.Then[i].Expect);
        }
    }

    // -------------------------------------------------------------------------
    // UI phrase variants — testId / role+label
    // -------------------------------------------------------------------------

    [Fact]
    public void Ingest_UiClickTestId_ExtractsTestId()
    {
        var ingester = BuildIngester();
        var feature = """
            Feature: Click TestId
              Scenario: Click TestId
                When I click the "submit-btn" element
                Then the response status should be 200
            """;

        var flow = ingester.Ingest(feature);

        var step = flow.When.FirstOrDefault(a => a.Step == "ui.click");
        Assert.NotNull(step);
        Assert.Equal("submit-btn", step.With?["testId"]);
    }

    [Fact]
    public void Ingest_UiClickRoleLabel_ExtractsRoleAndLabel()
    {
        var ingester = BuildIngester();
        var feature = """
            Feature: Click Role
              Scenario: Click Role
                When I click the button "Submit"
                Then the response status should be 200
            """;

        var flow = ingester.Ingest(feature);

        var step = flow.When.FirstOrDefault(a => a.Step == "ui.click");
        Assert.NotNull(step);
        Assert.Equal("button", step.With?["role"]);
        Assert.Equal("Submit", step.With?["label"]);
    }

    [Fact]
    public void Ingest_UiAssertTextTestId_ExtractsTestIdAndExpected()
    {
        var ingester = BuildIngester();
        var feature = """
            Feature: Assert TestId
              Scenario: Assert TestId
                When I GET https://example.com
                Then the "result-label" element should show "4"
            """;

        var flow = ingester.Ingest(feature);

        var step = flow.Then.FirstOrDefault(e => e.Expect == "ui.assert-text");
        Assert.NotNull(step);
        Assert.Equal("result-label", step.With?["testId"]);
        Assert.Equal("4", step.With?["expected"]);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Cress.sln")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir)!;
        }

        throw new InvalidOperationException("Could not locate solution root from " + AppContext.BaseDirectory);
    }
}
