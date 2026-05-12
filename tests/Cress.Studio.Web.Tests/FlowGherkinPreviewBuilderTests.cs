using Cress.Studio.Services;
using Cress.Studio.ViewModels;

namespace Cress.Studio.Web.Tests;

public sealed class FlowGherkinPreviewBuilderTests
{
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
}
