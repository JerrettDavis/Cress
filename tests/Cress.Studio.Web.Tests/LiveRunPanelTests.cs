using System.Reflection;
using Bunit;
using Cress.Studio;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class LiveRunPanelTests : TestContext
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
    public void LiveRunPanel_announces_current_checkpoint_as_status()
    {
        var state = CreateState();
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentFlow), "Checkout flow");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentStep), "http.get");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentStepMessage), "Waiting for API response.");

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.LiveRunPanel>();

        var statusRegion = cut.Find("[data-testid='live-run-current']");
        Assert.Equal("status", statusRegion.GetAttribute("role"));
        Assert.Equal("polite", statusRegion.GetAttribute("aria-live"));
        Assert.Contains("Checkout flow -> http.get", statusRegion.TextContent);
    }

    [Fact]
    public void LiveRunPanel_shows_log_summary_for_timeline_entries()
    {
        var state = CreateState();
        state.ToggleLiveLogVisibility();
        state.LiveTimelineEntries.AddRange(
        [
            new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Run", "Queued", null, "queued", "Flow A", null, null),
            new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Step", "Clicked", "Button clicked", "passed", "Flow A", "ui.click", null)
        ]);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.LiveRunPanel>();

        Assert.Contains("latest 2 of 2 timeline events", cut.Find("[data-testid='live-run-log-summary']").TextContent, StringComparison.OrdinalIgnoreCase);
    }
}
