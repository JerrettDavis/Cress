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

    [Fact]
    public void LiveRunPanel_compact_mode_hides_log_toggle_and_log_content()
    {
        var state = CreateState();
        state.ToggleLiveLogVisibility();
        state.LiveTimelineEntries.Add(new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Run", "Queued", null, "queued", null, null, null));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.LiveRunPanel>(parameters => parameters
            .Add(panel => panel.Compact, true));

        Assert.Empty(cut.FindAll("[data-testid='toggle-live-log']"));
        Assert.Empty(cut.FindAll("[data-testid='live-run-log']"));
    }

    [Fact]
    public void LiveRunPanel_renders_empty_timeline_and_waiting_summaries()
    {
        var state = CreateState();
        state.ToggleLiveLogVisibility();
        SetPrivate(state, nameof(StudioWorkspaceState.LiveRunStatus), "Queued");

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.LiveRunPanel>();

        Assert.Contains("No live events yet.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Waiting for the run plan.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Waiting for the first runnable step.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No step timer active", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("live-run-chip--queued", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveRunPanel_renders_progress_labels_and_elapsed_formats()
    {
        var state = CreateState();
        SetPrivate(state, nameof(StudioWorkspaceState.LiveRunStatus), "In Progress");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentFlow), "Checkout");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentStep), "submit");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentStepMessage), "Posting request");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCompletedFlows), 1);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveTotalFlows), 3);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentFlowIndex), 2);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCompletedSteps), 4);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveTotalSteps), 10);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentFlowStepIndex), 2);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentFlowStepCount), 5);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveStepStartedAt), DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(2)).Add(TimeSpan.FromSeconds(3))));
        SetPrivate(state, nameof(StudioWorkspaceState.IsBusy), true);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.LiveRunPanel>();

        Assert.Contains("Flow 2 / 3", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Step 2 / 5", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Elapsed 01:02:03", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("4 of 10 steps complete", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("live-run-chip--in-progress", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveRunPanel_renders_timeline_status_classes_and_node_label()
    {
        var state = CreateState();
        state.ToggleLiveLogVisibility();
        state.LiveTimelineEntries.AddRange(
        [
            new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Run", "Queued", null, "warning", "Flow A", null, null),
            new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Step", "Failed", "Broken", "error", "Flow A", "ui.click", null),
            new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Step", "Passed", null, "passed", "Flow A", "ui.type", null)
        ]);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.LiveRunPanel>();

        Assert.Contains("Node:", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("live-run-timeline-item--warning", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("live-run-timeline-item--failed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("live-run-timeline-item--passed", cut.Markup, StringComparison.Ordinal);
    }
}
