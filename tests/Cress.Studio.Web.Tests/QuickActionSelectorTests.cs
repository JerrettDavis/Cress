using System.Reflection;
using Bunit;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class QuickActionSelectorTests : TestContext
{
    private StudioWorkspaceState CreateState()
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
    }

    [Fact]
    public void QuickActionSelector_renders_nothing_when_no_flow_selected()
    {
        CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.QuickActionSelector>();

        Assert.DoesNotContain("<select", cut.Markup);
        Assert.DoesNotContain("Apply", cut.Markup);
    }

    [Fact]
    public void QuickActionSelector_renders_select_with_both_quick_actions()
    {
        var state = CreateState();

        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            FilePath = @"C:\workspace\flows\test.flow.yaml",
            Id = "test-flow",
            Name = "Test flow"
        });

        SetPrivate(state, "SelectedFlow", document);
        SetPrivate(state, "FlowAnalysis", new FlowEditorAnalysis
        {
            QuickActions =
            [
                new FlowQuickAction("metadata.smoke", "Apply smoke metadata", "Adds smoke tags.", "Metadata"),
                new FlowQuickAction("fixture.session", "Add fixture row", "Adds a fixture.", "Fixtures")
            ]
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.QuickActionSelector>();

        Assert.Contains("Apply smoke metadata", cut.Markup);
        Assert.Contains("Add fixture row", cut.Markup);
        Assert.Single(cut.FindAll("select"));
    }

    [Fact]
    public void QuickActionSelector_apply_button_disabled_when_nothing_selected()
    {
        var state = CreateState();

        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            FilePath = @"C:\workspace\flows\test.flow.yaml",
            Id = "test-flow",
            Name = "Test flow"
        });

        SetPrivate(state, "SelectedFlow", document);
        SetPrivate(state, "FlowAnalysis", new FlowEditorAnalysis
        {
            QuickActions =
            [
                new FlowQuickAction("metadata.smoke", "Apply smoke metadata", "Adds smoke tags.", "Metadata")
            ]
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.QuickActionSelector>();

        var applyButton = cut.Find("button");
        Assert.NotNull(applyButton.GetAttribute("disabled"));
    }
}
