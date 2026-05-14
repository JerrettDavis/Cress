using System.Text.Json;
using Cress.Core.Models;
using Cress.Exporters.SeleniumIde;

namespace Cress.Exporters.Tests;

public sealed class SeleniumIdeExporterTests
{
    private readonly SeleniumIdeExporter _sut = new();

    // -------------------------------------------------------------------------
    // Top-level .side structure
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_AlwaysProducesValidJson()
    {
        var flow = MakeFlow("my-test", []);
        var output = _sut.Export(flow);

        // Must parse without throwing
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Export_RootHasRequiredFields()
    {
        var flow = MakeFlow("my-test", []);
        var output = _sut.Export(flow);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("id", out _), "missing 'id'");
        Assert.True(root.TryGetProperty("version", out var ver), "missing 'version'");
        Assert.Equal("2.0", ver.GetString());
        Assert.True(root.TryGetProperty("name", out var name), "missing 'name'");
        Assert.Equal("my-test", name.GetString());
        Assert.True(root.TryGetProperty("tests", out _), "missing 'tests'");
        Assert.True(root.TryGetProperty("suites", out _), "missing 'suites'");
        Assert.True(root.TryGetProperty("urls", out _), "missing 'urls'");
        Assert.True(root.TryGetProperty("plugins", out _), "missing 'plugins'");
    }

    // -------------------------------------------------------------------------
    // browser.navigate → open
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_BrowserNavigate_EmitsOpenCommand()
    {
        var flow = MakeFlow("nav", [
            new FlowAction { Step = "browser.navigate", With = With("url", "https://example.com/app") }
        ]);
        var output = _sut.Export(flow);
        var commands = GetCommands(output);

        Assert.Contains(commands, c => c.Command == "open");
    }

    [Fact]
    public void Export_BrowserNavigate_SetsBaseUrl()
    {
        var flow = MakeFlow("nav", [
            new FlowAction { Step = "browser.navigate", With = With("url", "https://example.com/login") }
        ]);
        var output = _sut.Export(flow);
        using var doc = JsonDocument.Parse(output);

        Assert.Equal("https://example.com", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void Export_NormalizedFlow_ProducesEquivalentSideDocument()
    {
        var flow = new NormalizedFlow
        {
            Version = 1,
            FlowId = "normalized",
            Name = "Normalized",
            Summary = "summary",
            Tags = ["smoke"],
            Actions =
            [
                new NormalizedExecutable
                {
                    Name = "browser.navigate",
                    Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["url"] = "https://example.com/app"
                    }
                }
            ],
            Expectations =
            [
                new NormalizedExecutable
                {
                    Name = "ui.assert-window-title",
                    Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["title"] = "Example"
                    }
                }
            ]
        };

        var output = _sut.Export(flow);
        var commands = GetCommands(output);

        Assert.Contains(commands, c => c.Command == "open" && c.Target == "/app");
        Assert.Contains(commands, c => c.Command == "assertTitle" && c.Target == "Example");
    }

    [Fact]
    public void Export_NormalizedFlow_AllowsMissingInputDictionaries()
    {
        var flow = new NormalizedFlow
        {
            Version = 1,
            FlowId = "normalized-empty",
            Name = "Normalized Empty",
            Actions =
            [
                new NormalizedExecutable
                {
                    Name = "ui.click"
                }
            ],
            Expectations =
            [
                new NormalizedExecutable
                {
                    Name = "ui.assert-visible"
                }
            ]
        };

        var commands = GetCommands(_sut.Export(flow));

        Assert.Contains(commands, c => c.Command == "click" && c.Target == "css=/* TODO: add selector */");
        Assert.Contains(commands, c => c.Command == "assertElementPresent" && c.Target == "css=/* TODO: add selector */");
    }

    // -------------------------------------------------------------------------
    // ui.click
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_ClickByAutomationId_EmitsIdTarget()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("automationId", "clearButton") }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var click = commands.Single(c => c.Command == "click");
        Assert.StartsWith("id=", click.Target);
        Assert.Contains("clearButton", click.Target);
    }

    [Fact]
    public void Export_ClickByTestId_EmitsCssDataTestid()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("testId", "submit-btn") }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var click = commands.Single(c => c.Command == "click");
        Assert.Contains("data-testid", click.Target);
        Assert.Contains("submit-btn", click.Target);
    }

    [Fact]
    public void Export_ClickByCssSelector_EmitsCssTarget()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("cssSelector", ".btn-primary") }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var click = commands.Single(c => c.Command == "click");
        Assert.StartsWith("css=", click.Target);
        Assert.Contains(".btn-primary", click.Target);
    }

    [Fact]
    public void Export_ClickByXpath_EmitsXpathTarget()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("xpath", "//button[@id='go']") }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var click = commands.Single(c => c.Command == "click");
        Assert.StartsWith("xpath=", click.Target);
    }

    [Theory]
    [InlineData("name", "email", "name=email")]
    [InlineData("label", "Email", "css=[aria-label=\"Email\"]")]
    [InlineData("text", "Continue", "linkText=Continue")]
    [InlineData("placeholder", "Search", "css=[placeholder=\"Search\"]")]
    public void Export_ClickByAdditionalLocator_UsesExpectedTarget(string key, string value, string expectedTarget)
    {
        var flow = MakeFlow("f", [
            new FlowAction
            {
                Step = "ui.click",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = value
                }
            }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var click = commands.Single(c => c.Command == "click");
        Assert.Equal(expectedTarget, click.Target);
    }

    [Fact]
    public void Export_ClickByRoleAndLabel_PrefersLabelResolutionOrder()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("role", "button", "label", "Sign in") }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var click = commands.Single(c => c.Command == "click");
        Assert.Equal("css=[aria-label=\"Sign in\"]", click.Target);
    }

    [Fact]
    public void Export_ClickByRoleOnly_EmitsRoleTarget()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("role", "button") }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var click = commands.Single(c => c.Command == "click");
        Assert.Equal("css=[role=\"button\"]", click.Target);
    }

    [Fact]
    public void Export_ClickWithoutLocator_EmitsTodoSelector()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click" }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var click = commands.Single(c => c.Command == "click");
        Assert.Equal("css=/* TODO: add selector */", click.Target);
    }

    [Fact]
    public void Export_BrowserWaitForUrl_EmitsWaitForBodyDataUrlSelector()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "browser.wait-for-url", With = With("url", "/dashboard") }
        ]);

        var wait = GetCommands(_sut.Export(flow)).Single(c => c.Command == "waitForElementPresent");

        Assert.Equal("css=body[data-url*=\"/dashboard\"]", wait.Target);
    }

    [Fact]
    public void Export_BrowserWaitForUrl_UsesEmptySubstring_WhenUrlMissing()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "browser.wait-for-url" }
        ]);

        var wait = GetCommands(_sut.Export(flow)).Single(c => c.Command == "waitForElementPresent");

        Assert.Equal("css=body[data-url*=\"\"]", wait.Target);
    }

    // -------------------------------------------------------------------------
    // ui.fill → type
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_Fill_EmitsTypeCommand()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.fill", With = With("cssSelector", "#email", "value", "user@test.com") }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var type = commands.Single(c => c.Command == "type");
        Assert.Equal("user@test.com", type.Value);
    }

    [Fact]
    public void Export_FillWithoutValue_UsesEmptyString()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.fill", With = With("cssSelector", "#email") }
        ]);

        var type = GetCommands(_sut.Export(flow)).Single(c => c.Command == "type");

        Assert.Equal(string.Empty, type.Value);
    }

    [Theory]
    [InlineData("ENTER", "${KEY_ENTER}")]
    [InlineData("tab", "${KEY_TAB}")]
    [InlineData("Left", "${KEY_LEFT}")]
    [InlineData("F12", "${KEY_F12}")]
    [InlineData("CustomKey", "CustomKey")]
    public void Export_PressKey_MapsKeysToSeleniumNotation(string key, string expectedValue)
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.press-key", With = With("key", key) }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        var sendKeys = commands.Single(c => c.Command == "sendKeys");
        Assert.Equal("css=body", sendKeys.Target);
        Assert.Equal(expectedValue, sendKeys.Value);
    }

    [Fact]
    public void Export_SelectCheckUncheckAndScreenshot_EmitExpectedCommands()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.select", With = With("testId", "country", "value", "Canada") },
            new FlowAction { Step = "ui.check", With = With("testId", "terms") },
            new FlowAction { Step = "ui.uncheck", With = With("testId", "terms") },
            new FlowAction { Step = "ui.screenshot" }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        Assert.Contains(commands, c => c.Command == "select" && c.Value == "label=Canada");
        Assert.Contains(commands, c => c.Command == "check");
        Assert.Contains(commands, c => c.Command == "uncheck");
        Assert.Contains(commands, c => c.Command == "storeScreenshot" && c.Value == "screenshot");
    }

    [Fact]
    public void Export_UnknownAndUnmappedSteps_EmitComments()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "unknown", With = With("comment", "custom comment") },
            new FlowAction { Step = "desktop.magic" }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        Assert.Contains(commands, c => c.Command == "//" && c.Target == "custom comment");
        Assert.Contains(commands, c => c.Command == "//" && c.Target.Contains("desktop.magic", StringComparison.Ordinal));
    }

    // -------------------------------------------------------------------------
    // HTTP steps → comment block
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_HttpGet_EmitsCommentNotCommand()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "http.get", With = With("url", "https://api.example.com/v1/users") }
        ]);
        var commands = GetCommands(_sut.Export(flow));

        // Should be a comment command ("//"), NOT a real Selenium command
        Assert.Contains(commands, c => c.Command == "//" && c.Target.Contains("HTTP GET"));
        Assert.DoesNotContain(commands, c => c.Command == "open" && c.Target.Contains("api.example.com"));
    }

    [Fact]
    public void Export_AllHttpVerbs_EmitCommentCommands()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "http.put", With = With("url", "https://api.example.com/v1/users/1") },
            new FlowAction { Step = "http.delete", With = With("url", "https://api.example.com/v1/users/1") },
            new FlowAction { Step = "http.patch", With = With("url", "https://api.example.com/v1/users/1") }
        ]);

        var comments = GetCommands(_sut.Export(flow)).Where(c => c.Command == "//").Select(c => c.Target).ToList();

        Assert.Contains(comments, target => target.Contains("HTTP PUT", StringComparison.Ordinal));
        Assert.Contains(comments, target => target.Contains("HTTP DELETE", StringComparison.Ordinal));
        Assert.Contains(comments, target => target.Contains("HTTP PATCH", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_UsesHttpUrlAsBaseUrlWhenNoNavigateStepExists()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "http.post", With = With("url", "https://api.example.com/v1/users") }
        ]);
        var output = _sut.Export(flow);
        using var doc = JsonDocument.Parse(output);

        Assert.Equal("https://api.example.com", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void Export_FallsBackToLocalhostWhenNoUrlExists()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("testId", "save") }
        ]);
        var output = _sut.Export(flow);
        using var doc = JsonDocument.Parse(output);

        Assert.Equal("http://localhost", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void Export_UsesRawNavigateUrl_WhenItIsNotAbsolute()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "browser.navigate", With = With("url", "relative/page") }
        ]);

        using var doc = JsonDocument.Parse(_sut.Export(flow));

        Assert.Equal("relative/page", doc.RootElement.GetProperty("url").GetString());
        Assert.Contains(GetCommands(doc.RootElement.GetRawText()), c => c.Command == "open" && c.Target == "relative/page");
    }

    [Fact]
    public void Export_UsesFullNavigateUrl_WhenItTargetsDifferentOrigin()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "browser.navigate", With = With("url", "https://example.com/app") },
            new FlowAction { Step = "browser.navigate", With = With("url", "https://other.example.net/dashboard") }
        ]);

        var opens = GetCommands(_sut.Export(flow)).Where(c => c.Command == "open").ToList();

        Assert.Equal("/app", opens[0].Target);
        Assert.Equal("https://other.example.net/dashboard", opens[1].Target);
    }

    [Fact]
    public void Export_UnknownStepWithoutComment_UsesStepNameAsCommentText()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "unknown" }
        ]);

        var comment = GetCommands(_sut.Export(flow)).Single(c => c.Command == "//");

        Assert.Equal("unknown", comment.Target);
    }

    // -------------------------------------------------------------------------
    // Expectations
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_AssertText_EmitsAssertTextCommand()
    {
        var flow = MakeFlow("f", [],
            expectations: [
                new FlowExpectation { Expect = "ui.assert-text", With = With("cssSelector", "#result", "expected", "42") }
            ]);
        var commands = GetCommands(_sut.Export(flow));

        var assertText = commands.Single(c => c.Command == "assertText");
        Assert.Equal("42", assertText.Value);
    }

    [Fact]
    public void Export_AssertVisible_EmitsAssertElementPresent()
    {
        var flow = MakeFlow("f", [],
            expectations: [
                new FlowExpectation { Expect = "ui.assert-visible", With = With("testId", "banner") }
            ]);
        var commands = GetCommands(_sut.Export(flow));

        Assert.Contains(commands, c => c.Command == "assertElementPresent");
    }

    [Fact]
    public void Export_AdditionalExpectations_EmitExpectedCommandsAndComments()
    {
        var flow = MakeFlow("f", [],
            expectations: [
                new FlowExpectation { Expect = "browser.assert-url", With = With("expected", "/dashboard") },
                new FlowExpectation { Expect = "http.assert-url", With = With("url", "/api/orders") },
                new FlowExpectation { Expect = "ui.assert-window-title", With = With("title", "Dashboard") },
                new FlowExpectation { Expect = "http.assert-status", With = With("status", "200") },
                new FlowExpectation { Expect = "http.assert-json", With = With("path", "$.data.id") },
                new FlowExpectation { Expect = "desktop.assert-magic" }
            ]);
        var commands = GetCommands(_sut.Export(flow));

        Assert.Contains(commands, c => c.Command == "assertLocation" && c.Target == "/dashboard");
        Assert.Contains(commands, c => c.Command == "assertLocation" && c.Target == "/api/orders");
        Assert.Contains(commands, c => c.Command == "assertTitle" && c.Target == "Dashboard");
        Assert.Contains(commands, c => c.Command == "//" && c.Target.Contains("HTTP status assertion", StringComparison.Ordinal));
        Assert.Contains(commands, c => c.Command == "//" && c.Target.Contains("$.data.id", StringComparison.Ordinal));
        Assert.Contains(commands, c => c.Command == "//" && c.Target.Contains("desktop.assert-magic", StringComparison.Ordinal));
    }

    // -------------------------------------------------------------------------
    // End-to-end: calc-add flow (desktop steps gracefully degraded)
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_CalcAddFlow_ProducesValidSideFile()
    {
        var flow = new CressFlow
        {
            Version = 1,
            Id = "calc.add-two-plus-two",
            Name = "Calculator: 2 + 2 = 4",
            When =
            [
                new FlowAction { Step = "ui.click", With = With("automationId", "num2Button") },
                new FlowAction { Step = "ui.click", With = With("automationId", "plusButton") },
                new FlowAction { Step = "ui.click", With = With("automationId", "num2Button") },
                new FlowAction { Step = "ui.click", With = With("automationId", "equalButton") }
            ],
            Then =
            [
                new FlowExpectation { Expect = "ui.assert-text", With = With("automationId", "CalculatorResults", "expected", "Display is 4") }
            ]
        };

        var output = _sut.Export(flow);
        using var doc = JsonDocument.Parse(output);

        Assert.Equal("Calculator: 2 + 2 = 4", doc.RootElement.GetProperty("name").GetString());

        var commands = GetCommands(output);
        Assert.Equal(4, commands.Count(c => c.Command == "click"));
        Assert.Single(commands, c => c.Command == "assertText");
    }

    // -------------------------------------------------------------------------
    // Suite references the test ID
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_SuiteReferencesTestId()
    {
        var flow = MakeFlow("f", []);
        var output = _sut.Export(flow);
        using var doc = JsonDocument.Parse(output);

        var testId = doc.RootElement
            .GetProperty("tests")[0]
            .GetProperty("id")
            .GetString();

        var suiteTests = doc.RootElement
            .GetProperty("suites")[0]
            .GetProperty("tests");

        Assert.Contains(suiteTests.EnumerateArray(), el => el.GetString() == testId);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CressFlow MakeFlow(
        string name,
        List<FlowAction> actions,
        List<FlowExpectation>? expectations = null) =>
        new()
        {
            Version = 1,
            Id = name,
            Name = name,
            When = actions,
            Then = expectations ?? []
        };

    private static Dictionary<string, string> With(string k1, string v1) =>
        new(StringComparer.OrdinalIgnoreCase) { [k1] = v1 };

    private static Dictionary<string, string> With(string k1, string v1, string k2, string v2) =>
        new(StringComparer.OrdinalIgnoreCase) { [k1] = v1, [k2] = v2 };

    private record SideCommand(string Command, string Target, string Value);

    private static List<SideCommand> GetCommands(string sideJson)
    {
        using var doc = JsonDocument.Parse(sideJson);
        var commands = new List<SideCommand>();

        var testCommands = doc.RootElement
            .GetProperty("tests")[0]
            .GetProperty("commands");

        foreach (var cmd in testCommands.EnumerateArray())
        {
            commands.Add(new SideCommand(
                cmd.GetProperty("command").GetString() ?? "",
                cmd.GetProperty("target").GetString() ?? "",
                cmd.GetProperty("value").GetString() ?? ""
            ));
        }

        return commands;
    }
}
