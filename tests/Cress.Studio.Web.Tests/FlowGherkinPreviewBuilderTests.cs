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
}
