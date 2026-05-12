using Cress.Core.Models;
using Cress.Gherkin.Phrases;

namespace Cress.Gherkin.Tests;

public sealed class FlowToGherkinConverterTests
{
    private static FlowToGherkinConverter BuildConverter()
        => new(PhraseLibrary.CreateDefault());

    // -------------------------------------------------------------------------
    // CressFlow conversion
    // -------------------------------------------------------------------------

    [Fact]
    public void Convert_CressFlow_ContainsFeatureKeyword()
    {
        var converter = BuildConverter();
        var flow = new CressFlow
        {
            Version = 1,
            Id = "test.flow",
            Name = "My Test Flow",
            When = [new FlowAction { Step = "http.get", With = new() { ["url"] = "https://example.com" } }],
            Then = [new FlowExpectation { Expect = "http.assert-status", With = new() { ["status"] = "200" } }]
        };

        var result = converter.Convert(flow);

        Assert.Contains("Feature: My Test Flow", result);
    }

    [Fact]
    public void Convert_CressFlow_ContainsScenarioKeyword()
    {
        var converter = BuildConverter();
        var flow = new CressFlow
        {
            Version = 1,
            Id = "test.flow",
            Name = "My Test Flow",
            When = [new FlowAction { Step = "http.get", With = new() { ["url"] = "https://example.com" } }],
            Then = [new FlowExpectation { Expect = "http.assert-status", With = new() { ["status"] = "200" } }]
        };

        var result = converter.Convert(flow);

        Assert.Contains("Scenario: My Test Flow", result);
    }

    [Fact]
    public void Convert_CressFlow_ContainsWhenPhrase()
    {
        var converter = BuildConverter();
        var flow = new CressFlow
        {
            Version = 1,
            Id = "test.flow",
            Name = "My Test Flow",
            When = [new FlowAction { Step = "http.get", With = new() { ["url"] = "https://httpbin.org/get" } }],
            Then = [new FlowExpectation { Expect = "http.assert-status", With = new() { ["status"] = "200" } }]
        };

        var result = converter.Convert(flow);

        Assert.Contains("When I GET https://httpbin.org/get", result);
    }

    [Fact]
    public void Convert_CressFlow_ContainsThenPhrase()
    {
        var converter = BuildConverter();
        var flow = new CressFlow
        {
            Version = 1,
            Id = "test.flow",
            Name = "My Test Flow",
            When = [new FlowAction { Step = "http.get", With = new() { ["url"] = "https://httpbin.org/get" } }],
            Then = [new FlowExpectation { Expect = "http.assert-status", With = new() { ["status"] = "200" } }]
        };

        var result = converter.Convert(flow);

        Assert.Contains("Then the response status should be 200", result);
    }

    [Fact]
    public void Convert_DesktopSelectorFlow_UsesSelectorFriendlyPhrases()
    {
        var converter = BuildConverter();
        var flow = new CressFlow
        {
            Version = 1,
            Id = "desktop.selector-flow",
            Name = "Desktop selector flow",
            When =
            [
                new FlowAction { Step = "ui.launch", With = new() { ["application"] = "calc.exe" } },
                new FlowAction { Step = "ui.invoke", With = new() { ["selector"] = "#clearButton" } }
            ],
            Then =
            [
                new FlowExpectation { Expect = "ui.assert-text", With = new() { ["selector"] = "#CalculatorResults", ["text"] = "Display is 4" } }
            ]
        };

        var result = converter.Convert(flow);

        Assert.Contains("Given the user launches the calc.exe application", result);
        Assert.Contains("And the user clicks the clear button", result);
        Assert.Contains("Then the Calculator Results accessibility text should display \"Display is 4\"", result);
    }

    [Fact]
    public void Convert_MultipleThenSteps_SecondStepUsesAnd()
    {
        var converter = BuildConverter();
        var flow = new CressFlow
        {
            Version = 1,
            Id = "test.flow",
            Name = "Multi assertion",
            When = [new FlowAction { Step = "http.get", With = new() { ["url"] = "https://example.com" } }],
            Then =
            [
                new FlowExpectation { Expect = "http.assert-status", With = new() { ["status"] = "200" } },
                new FlowExpectation { Expect = "http.assert-json", With = new() { ["path"] = "url", ["equals"] = "https://example.com" } }
            ]
        };

        var result = converter.Convert(flow);

        Assert.Contains("Then the response status should be 200", result);
        Assert.Contains("And the response JSON url should equal", result);
    }

    [Fact]
    public void Convert_Tags_EmittedAsGherkinTags()
    {
        var converter = BuildConverter();
        var flow = new CressFlow
        {
            Version = 1,
            Id = "test.flow",
            Name = "Tagged Flow",
            Tags = ["smoke", "http"],
            When = [new FlowAction { Step = "http.get", With = new() { ["url"] = "https://example.com" } }],
            Then = [new FlowExpectation { Expect = "http.assert-status", With = new() { ["status"] = "200" } }]
        };

        var result = converter.Convert(flow);

        Assert.Contains("@smoke", result);
        Assert.Contains("@http", result);
    }

    [Fact]
    public void Convert_UnknownStep_EmitsTodoComment()
    {
        var converter = BuildConverter();
        var flow = new CressFlow
        {
            Version = 1,
            Id = "test.flow",
            Name = "Unknown Step Flow",
            When = [new FlowAction { Step = "custom.unknown-op", With = new() }],
            Then = [new FlowExpectation { Expect = "http.assert-status", With = new() { ["status"] = "200" } }]
        };

        var result = converter.Convert(flow);

        Assert.Contains("# TODO: add phrase for custom.unknown-op", result);
    }

    // -------------------------------------------------------------------------
    // NormalizedFlow conversion
    // -------------------------------------------------------------------------

    [Fact]
    public void Convert_NormalizedFlow_WellFormedGherkin()
    {
        var converter = BuildConverter();
        var flow = new NormalizedFlow
        {
            FlowId = "http.smoke",
            Name = "HTTP Smoke",
            Actions =
            [
                new NormalizedExecutable
                {
                    Kind = "step",
                    Name = "http.get",
                    Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["url"] = "https://httpbin.org/get" },
                    Source = new SourceReference { Section = "when", Index = 0 }
                }
            ],
            Expectations =
            [
                new NormalizedExecutable
                {
                    Kind = "expectation",
                    Name = "http.assert-status",
                    Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["status"] = "200" },
                    Source = new SourceReference { Section = "then", Index = 0 }
                }
            ]
        };

        var result = converter.Convert(flow);

        Assert.Contains("Feature:", result);
        Assert.Contains("Scenario:", result);
        Assert.Contains("When", result);
        Assert.Contains("Then", result);
    }

    // -------------------------------------------------------------------------
    // End-to-end: calc-add.flow.yaml parsed and converted
    // -------------------------------------------------------------------------

    [Fact]
    public void EndToEnd_CalcAddFlow_ProducesWellFormedGherkin()
    {
        // Locate the fixture file relative to this test assembly's location.
        var solutionRoot = FindSolutionRoot();
        var flowFile = Path.Combine(solutionRoot, "specs", "calc-smoke", "flows", "calc-add.flow.yaml");
        if (!File.Exists(flowFile))
        {
            // Skip gracefully if the file doesn't exist in the CI environment.
            return;
        }

        var parser = new Cress.Specs.FlowParser();
        var parseResult = parser.ParseFile(flowFile);
        Assert.NotNull(parseResult.Value);

        var converter = BuildConverter();
        var feature = converter.Convert(parseResult.Value!);

        // Must contain the standard Gherkin structure markers.
        Assert.Contains("Feature:", feature);
        Assert.Contains("Scenario:", feature);

        // Must have at least one step keyword (Given/When/Then/And).
        var hasStepLine = feature.Split('\n').Any(l =>
        {
            var trimmed = l.TrimStart();
            return trimmed.StartsWith("Given ", StringComparison.Ordinal)
                || trimmed.StartsWith("When ", StringComparison.Ordinal)
                || trimmed.StartsWith("Then ", StringComparison.Ordinal)
                || trimmed.StartsWith("And ", StringComparison.Ordinal)
                || trimmed.StartsWith("When #", StringComparison.Ordinal)
                || trimmed.StartsWith("Then #", StringComparison.Ordinal);
        });

        Assert.True(hasStepLine, $"No step lines found in:\n{feature}");
    }

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
