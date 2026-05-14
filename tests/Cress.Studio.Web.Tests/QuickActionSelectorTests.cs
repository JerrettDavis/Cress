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

    private static void InvokeChanged(StudioWorkspaceState state)
    {
        var backingField = state.GetType()
            .GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? state.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(field => field.Name.StartsWith("Changed", StringComparison.Ordinal));

        if (backingField?.GetValue(state) is Action handler)
        {
            handler.Invoke();
        }
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
        Assert.Contains("2 available", cut.Markup);
        Assert.Contains("optgroup", cut.Markup);
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

        var applyButton = cut.Find("[data-testid='quick-action-apply']");
        Assert.NotNull(applyButton.GetAttribute("disabled"));
    }

    [Fact]
    public void QuickActionSelector_shows_selected_action_description()
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

        cut.Find("[data-testid='quick-action-select']").Change("fixture.session");

        Assert.Contains("Fixtures - Adds a fixture.", cut.Find("[data-testid='quick-action-description']").TextContent);
    }

    [Fact]
    public void QuickActionSelector_updates_when_workspace_state_changes()
    {
        var state = CreateState();
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.QuickActionSelector>();

        Assert.Empty(cut.FindAll("[data-testid='quick-action-selector']"));

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

        cut.InvokeAsync(() => InvokeChanged(state));

        Assert.NotNull(cut.Find("[data-testid='quick-action-selector']"));
        Assert.Contains("1 available", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickActionSelector_apply_button_applies_action_and_resets_selection()
    {
        var state = CreateState();

        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            FilePath = @"C:\workspace\flows\test.flow.yaml",
            Id = "test-flow",
            Name = "Test flow",
            TagsText = "existing"
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
        cut.Find("[data-testid='quick-action-select']").Change("metadata.smoke");

        Assert.Null(cut.Find("[data-testid='quick-action-apply']").GetAttribute("disabled"));

        cut.Find("[data-testid='quick-action-apply']").Click();

        Assert.Contains(
            "Choose an action to preview what it adds before applying it.",
            cut.Find("[data-testid='quick-action-description']").TextContent,
            StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid='quick-action-apply']").GetAttribute("disabled"));
    }
}
