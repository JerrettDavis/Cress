using Cress.Specs;

namespace Cress.UnitTests;

/// <summary>
/// Tests that FlowParser correctly parses the V1 locator strategy fields
/// (role, testId, label, text, cssSelector, xpath) from the with: block.
/// </summary>
public sealed class FlowParserLocatorTests
{
    private static readonly FlowParser Parser = new();

    // -------------------------------------------------------------------------
    // Helper: build minimal valid flow YAML with a single when-step that carries
    // the supplied with-block content.
    // -------------------------------------------------------------------------
    private static string FlowWith(string withBlock) => $"""
version: 1
id: locator-test
name: Locator test flow
when:
  - step: ui.invoke
    with:
{withBlock}
then:
  - expect: ui.assert-window-title
    with:
      title: Calc
""";

    // -------------------------------------------------------------------------
    // V1-1: role field parses without error
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithRole_ParsesWithoutError()
    {
        var yaml = FlowWith("      role: button");
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, Diags(result));
        Assert.Equal("button", result.Value!.When[0].With!["role"]);
    }

    // -------------------------------------------------------------------------
    // V1-2: testId field parses without error
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithTestId_ParsesWithoutError()
    {
        var yaml = FlowWith("      testId: submit-button");
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, Diags(result));
        Assert.Equal("submit-button", result.Value!.When[0].With!["testId"]);
    }

    // -------------------------------------------------------------------------
    // V1-3: label field parses without error
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithLabel_ParsesWithoutError()
    {
        var yaml = FlowWith("      label: Submit");
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, Diags(result));
        Assert.Equal("Submit", result.Value!.When[0].With!["label"]);
    }

    // -------------------------------------------------------------------------
    // V1-4: text field parses without error
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithText_ParsesWithoutError()
    {
        var yaml = FlowWith("      text: Click me");
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, Diags(result));
        Assert.Equal("Click me", result.Value!.When[0].With!["text"]);
    }

    // -------------------------------------------------------------------------
    // V1-5: cssSelector field parses without error
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithCssSelector_ParsesWithoutError()
    {
        var yaml = FlowWith("      cssSelector: .btn-primary");
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, Diags(result));
        Assert.Equal(".btn-primary", result.Value!.When[0].With!["cssSelector"]);
    }

    // -------------------------------------------------------------------------
    // V1-6: xpath field parses without error
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithXPath_ParsesWithoutError()
    {
        var yaml = FlowWith("      xpath: \"//button[@id='ok']\"");
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, Diags(result));
        Assert.True(result.Value!.When[0].With!.ContainsKey("xpath"), "xpath key must be present");
    }

    // -------------------------------------------------------------------------
    // V1-7: mix of new + legacy locator fields all parse together
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_AllLocatorFields_ParsesWithoutError()
    {
        const string withBlock = """
      automationId: btn1
      name: OK
      controlType: Button
      role: button
      testId: ok-btn
      label: OK button
      text: OK
      cssSelector: button.ok
      xpath: "//button[@id='ok']"
""";
        var yaml = FlowWith(withBlock);
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, Diags(result));

        var with = result.Value!.When[0].With!;
        Assert.Equal("btn1",      with["automationId"]);
        Assert.Equal("OK",        with["name"]);
        Assert.Equal("Button",    with["controlType"]);
        Assert.Equal("button",    with["role"]);
        Assert.Equal("ok-btn",    with["testId"]);
        Assert.Equal("OK button", with["label"]);
        Assert.Equal("OK",        with["text"]);
        Assert.Equal("button.ok", with["cssSelector"]);
        Assert.True(with.ContainsKey("xpath"));
    }

    // -------------------------------------------------------------------------
    // V1-8: existing calc-smoke style flows (automationId-only) still parse cleanly
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_LegacyAutomationIdOnly_BackwardCompatible()
    {
        const string yaml = """
version: 1
id: calc.add-two-plus-two
name: Calculator - 2 + 2 = 4
when:
  - step: ui.attach
    with:
      processName: calc
  - step: ui.invoke
    with:
      automationId: num2Button
      controlType: Button
  - step: ui.invoke
    with:
      automationId: plusButton
      controlType: Button
  - step: ui.invoke
    with:
      automationId: equalButton
      controlType: Button
then:
  - expect: ui.assert-text
    with:
      automationId: CalculatorResults
      text: Display is 4
""";

        var result = Parser.Parse(yaml);

        Assert.True(result.Success, Diags(result));
        Assert.Equal(4, result.Value!.When.Count);
        Assert.Single(result.Value!.Then);
        Assert.Equal("calc", result.Value!.When[0].With!["processName"]);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static string Diags<T>(Cress.Core.Models.OperationResult<T> result)
        => string.Join("; ", result.Diagnostics.Select(d => d.Message));
}
