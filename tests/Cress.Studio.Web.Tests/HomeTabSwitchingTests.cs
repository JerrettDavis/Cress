using System.Reflection;
using Bunit;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;
using Cress.Studio.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class HomeTabSwitchingTests : TestContext
{
    private StudioWorkspaceState CreateState()
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        JSInterop.Setup<string[]>("cressStudio.getRecentWorkspaces").SetResult([]);
        JSInterop.SetupVoid("cressStudio.setRecentWorkspaces", _ => true);
        JSInterop.SetupVoid("cressStudio.scrollSectionIntoView", _ => true);
        JSInterop.Setup<string>("getTheme").SetResult("system");
        JSInterop.SetupVoid("setTheme", _ => true);
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
        // 'public event Action? Changed' has a compiler-generated backing field named "Changed".
        var backingField = state.GetType()
            .GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? state.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name.StartsWith("Changed", StringComparison.Ordinal));

        if (backingField?.GetValue(state) is Action handler)
        {
            handler.Invoke();
        }
    }

    [Fact]
    public void Home_switches_editor_tab_to_flow_when_flow_selection_transitions()
    {
        var state = CreateState();
        state.LoadDemoWorkspace("calc-smoke");
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/designer");

        var cut = RenderComponent<Cress.Studio.Web.Components.Pages.Home>();

        cut.Find("[data-testid='designer-tab-overview']").Click();
        Assert.Contains(cut.FindAll(".tab-button.active"), b => b.TextContent.Contains("Overview", StringComparison.Ordinal));

        // Simulate state after ExplorerPanel selects a flow.
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            FilePath = @"C:\workspace\flows\login.flow.yaml",
            Id = "flow-login",
            Name = "Login flow"
        });
        SetPrivate(state, "SelectedFlow", document);

        cut.InvokeAsync(() => InvokeChanged(state));

        // Tab should have auto-switched to "flow".
        Assert.Contains(cut.FindAll(".tab-button.active"), b => b.TextContent.Contains("Flow editor"));
    }

    [Fact]
    public void Home_switches_editor_tab_to_suite_when_suite_selection_transitions()
    {
        var state = CreateState();
        state.LoadDemoWorkspace("calc-smoke");
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/designer");

        var cut = RenderComponent<Cress.Studio.Web.Components.Pages.Home>();

        cut.Find("[data-testid='designer-tab-overview']").Click();
        Assert.Contains(cut.FindAll(".tab-button.active"), b => b.TextContent.Contains("Overview", StringComparison.Ordinal));

        // Simulate state after ExplorerPanel selects a suite.
        var suite = new StudioSuiteEditorModel
        {
            FilePath = @"C:\workspace\suites\regression.suite.yaml",
            Id = "suite-regression",
            Name = "Regression suite"
        };
        SetPrivate(state, "SelectedSuite", suite);

        cut.InvokeAsync(() => InvokeChanged(state));

        // Tab should have auto-switched to "suite".
        Assert.Contains(cut.FindAll(".tab-button.active"), b => b.TextContent.Contains("Suite editor"));
    }

    [Fact]
    public void Home_does_not_override_manual_tab_choice_on_repeated_state_change_with_same_flow()
    {
        var state = CreateState();
        state.LoadDemoWorkspace("calc-smoke");
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/designer");

        var cut = RenderComponent<Cress.Studio.Web.Components.Pages.Home>();

        // Select a flow to auto-switch to the flow tab.
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            FilePath = @"C:\workspace\flows\login.flow.yaml",
            Id = "flow-login",
            Name = "Login flow"
        });
        SetPrivate(state, "SelectedFlow", document);
        cut.InvokeAsync(() => InvokeChanged(state));

        // User manually switches to "source".
        cut.Find("[data-testid='designer-tab-source']").Click();
        Assert.Contains(cut.FindAll(".tab-button.active"), b => b.TextContent.Contains("Source", StringComparison.Ordinal));

        // Another state change fires but SelectedFlow is still the same path — no tab switch.
        cut.InvokeAsync(() => InvokeChanged(state));

        Assert.Contains(cut.FindAll(".tab-button.active"), b => b.TextContent.Contains("Source", StringComparison.Ordinal));
    }
}
