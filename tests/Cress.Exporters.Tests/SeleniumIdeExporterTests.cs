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
