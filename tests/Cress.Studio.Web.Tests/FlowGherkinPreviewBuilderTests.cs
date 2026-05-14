using Cress.Studio.Services;
using Cress.Studio.ViewModels;

namespace Cress.Studio.Web.Tests;

public sealed class FlowGherkinPreviewBuilderTests
{
    [Fact]
    public void Build_returns_empty_for_null_document()
    {
        var preview = FlowGherkinPreviewBuilder.Build(null);

        Assert.Equal(string.Empty, preview);
    }

    [Fact]
    public void Build_emits_feature_and_scenario_from_flow_document()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "web-search",
            Name = "Web search",
            Summary = "Exercises a simple browser search path.",
            Fixtures =
            [
                new EditableFixture
                {
                    Alias = "browser",
                    Use = "browser.chromium"
                }
            ],
            Actions =
            [
                new EditableExecutable
                {
                    Name = "browser.open",
                    InputsText = "url=https://www.google.com"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "browser.title_contains",
                    InputsText = "value=Google"
                }
            ]
        });

        var preview = FlowGherkinPreviewBuilder.Build(document);

        Assert.Contains("Feature: Web search", preview);
        Assert.Contains("Scenario: Web search", preview);
        Assert.Contains("Given browser uses browser.chromium", preview);
        Assert.Contains("When browser.open with url=https://www.google.com", preview);
        Assert.Contains("Then browser.title_contains with value=Google", preview);
    }

    [Fact]
    public void Build_uses_fallback_names_and_skips_blank_fixture_rows()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = string.Empty,
            Name = string.Empty,
            Fixtures =
            [
                new EditableFixture(),
                new EditableFixture
                {
                    Alias = "account"
                }
            ],
            Actions =
            [
                new EditableExecutable
                {
                    Name = "custom.step",
                    InputsText = "value=1"
                }
            ]
        });

        var preview = FlowGherkinPreviewBuilder.Build(document);

        Assert.Contains("Feature: Untitled flow", preview);
        Assert.Contains("Scenario: Unnamed scenario", preview);
        Assert.DoesNotContain("Given fixture uses", preview, StringComparison.Ordinal);
        Assert.Contains("Given account uses configured data", preview);
    }

    [Fact]
    public void Build_humanizes_desktop_ui_steps()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "calculator-flow",
            Name = "Calculator: 2 + 2 = 4",
            Actions =
            [
                new EditableExecutable
                {
                    Name = "ui.launch",
                    InputsText = "application=calc.exe"
                },
                new EditableExecutable
                {
                    Name = "ui.attach",
                    InputsText = "processName=ApplicationFrameHost\nwindowTitle=Calculator"
                },
                new EditableExecutable
                {
                    Name = "ui.invoke",
                    InputsText = "selector=#clearButton"
                },
                new EditableExecutable
                {
                    Name = "ui.invoke",
                    InputsText = "selector=#num2Button"
                },
                new EditableExecutable
                {
                    Name = "ui.screenshot",
                    InputsText = "name=calc-result"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "ui.assert-text",
                    InputsText = "selector=#CalculatorResults\ntext=Display is 4."
                }
            ]
        });

        var preview = FlowGherkinPreviewBuilder.Build(document);

        Assert.Contains("Given the user launches the calc.exe application", preview);
        Assert.Contains("And the \"Calculator\" window is open in the ApplicationFrameHost process", preview);
        Assert.Contains("And the user clicks the clear button", preview);
        Assert.Contains("And the user clicks the number 2 button", preview);
        Assert.Contains("And the user captures a screenshot named \"calc-result\"", preview);
        Assert.Contains("Then the Calculator Results accessibility text should display \"Display is 4.\"", preview);
    }

    [Fact]
    public void BuildExecutablePreview_returns_empty_for_blank_name()
    {
        var preview = FlowGherkinPreviewBuilder.BuildExecutablePreview(null, "value=1");

        Assert.Equal(string.Empty, preview);
    }

    [Fact]
    public void BuildExecutablePreview_uses_fallback_phrase_when_no_library_match_exists()
    {
        var preview = FlowGherkinPreviewBuilder.BuildExecutablePreview("custom.step", "value=1", Cress.Gherkin.Phrases.GherkinKeyword.Then);

        Assert.Equal("Then custom.step with value=1", preview);
    }

    [Fact]
    public void BuildExecutablePreview_uses_phrase_library_keyword_when_available()
    {
        var preview = FlowGherkinPreviewBuilder.BuildExecutablePreview("ui.launch", "application=calc.exe");

        Assert.Equal("Given the user launches the calc.exe application", preview);
    }

    [Fact]
    public void BuildExecutablePreview_handles_empty_inputs()
    {
        var preview = FlowGherkinPreviewBuilder.BuildExecutablePreview("custom.step", null);

        Assert.Equal("When custom.step", preview);
    }

    [Fact]
    public void Build_ignores_malformed_input_lines_and_blank_keys()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "malformed-inputs",
            Name = "Malformed inputs",
            Actions =
            [
                new EditableExecutable
                {
                    Name = "custom.step",
                    InputsText = """
                    invalid
                    =missing-key
                    valid = kept
                    """
                }
            ]
        });

        var preview = FlowGherkinPreviewBuilder.Build(document);

        Assert.Contains("When custom.step with valid=kept", preview);
        Assert.DoesNotContain("invalid", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("missing-key", preview, StringComparison.Ordinal);
    }
}
