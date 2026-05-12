using System.Reflection;
using Bunit;
using Cress.Studio;
using Cress.Studio.Web.Components.Layout;
using Cress.Studio.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class GlobalControlDrawerTests : TestContext
{
    private StudioWorkspaceState CreateState(FakeStudioRecorderService? recorderService = null, FakeStudioCompanionClient? companionClient = null)
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<Cress.Studio.Services.IStudioRecorderService>(recorderService ?? new FakeStudioRecorderService());
        Services.AddSingleton<Cress.Studio.Services.IStudioCompanionClient>(companionClient ?? new FakeStudioCompanionClient());
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
    public void GlobalControlDrawer_opens_and_shows_empty_monitor_states()
    {
        CreateState();

        var cut = RenderComponent<GlobalControlDrawer>();
        cut.Find("[data-testid='global-controls-toggle']").Click();

        Assert.Contains("Studio control center", cut.Markup);
        Assert.Contains("No run or recording session is active yet.", cut.Markup);
        Assert.Contains("Select a result artifact to keep its latest screenshot or report preview in this drawer.", cut.Markup);
    }

    [Fact]
    public void GlobalControlDrawer_shows_live_status_logs_and_preview()
    {
        var state = CreateState();
        SetPrivate(state, nameof(StudioWorkspaceState.LiveRunStatus), "Running");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveRunHeadline), "[run-123] Running checkout flow");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentFlow), "Checkout flow");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentStep), "ui.invoke");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentStepMessage), "Clicking the confirm button.");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentFlowIndex), 1);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveTotalFlows), 3);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveRunId), "run-123");
        SetPrivate(state, nameof(StudioWorkspaceState.SelectedArtifact), new StudioArtifactItem("checkout-step.png", "Latest screenshot", @"C:\artifacts\checkout-step.png"));
        SetPrivate(state, nameof(StudioWorkspaceState.PreviewText), "Latest screenshot preview is loading.");
        state.LiveTimelineEntries.Add(new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Step", "Clicked confirm", "Flow 1 • Step 2", "Running", "Checkout flow", "ui.invoke", null));

        var cut = RenderComponent<GlobalControlDrawer>();
        cut.Find("[data-testid='global-controls-toggle']").Click();

        Assert.Contains("Running", cut.Find("[data-testid='global-controls-status']").TextContent);
        Assert.Contains("Checkout flow", cut.Markup);
        Assert.Contains("Clicked confirm", cut.Markup);
        Assert.Contains("checkout-step.png", cut.Markup);
        Assert.Contains("Latest screenshot preview is loading.", cut.Markup);
    }

    [Fact]
    public async Task GlobalControlDrawer_shows_companion_sessions_when_available()
    {
        var companion = new FakeStudioCompanionClient
        {
            Snapshot = new Cress.Companion.CompanionServiceSnapshot
            {
                IsAvailable = true,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Sessions =
                [
                    new Cress.Companion.CompanionSessionSnapshot
                    {
                        ProcessId = 4321,
                        ProcessName = "notepad",
                        WindowTitle = "Release notes",
                        Status = Cress.Companion.CompanionSessionStatus.Recording,
                        StartedAtUtc = DateTimeOffset.UtcNow,
                        LastStepSummary = "Click(automationId=saveButton)"
                    }
                ]
            }
        };

        var state = CreateState(companionClient: companion);
        await state.RefreshCompanionAsync();

        var cut = RenderComponent<GlobalControlDrawer>();
        cut.Find("[data-testid='global-controls-toggle']").Click();

        Assert.Contains("Desktop companion", cut.Markup);
        Assert.Contains("Release notes", cut.Markup);
        Assert.Contains("Click(automationId=saveButton)", cut.Markup);
    }

    [Fact]
    public void GlobalControlDrawer_auto_opens_when_attention_starts()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = false,
            CapturedEventCount = 0
        };
        var state = CreateState(recorder);
        var cut = RenderComponent<GlobalControlDrawer>();

        Assert.Empty(cut.FindAll("[data-testid='global-controls-drawer']"));

        recorder.IsRecording = true;
        recorder.CapturedEventCount = 4;
        cut.InvokeAsync(recorder.SimulateStateChange);

        Assert.Contains("Studio control center", cut.Markup);
        Assert.Contains("Recording", cut.Find("[data-testid='global-controls-status']").TextContent);
        Assert.Contains("4 events captured", cut.Find("[data-testid='global-controls-toggle']").TextContent);
    }

    [Fact]
    public void GlobalControlDrawer_open_results_navigates_and_closes_drawer()
    {
        var state = CreateState();
        state.LoadDemoWorkspace("calc-smoke");

        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<GlobalControlDrawer>();

        cut.Find("[data-testid='global-controls-toggle']").Click();
        cut.Find("[data-testid='global-controls-open-results']").Click();

        Assert.EndsWith("/results", navigation.Uri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='global-controls-drawer']"));
    }

    [Fact]
    public void GlobalControlDrawer_renders_image_preview_when_selected_artifact_has_image()
    {
        var state = CreateState();
        SetPrivate(state, nameof(StudioWorkspaceState.SelectedArtifact), new StudioArtifactItem("latest.png", "Latest screenshot", @"C:\artifacts\latest.png"));
        SetPrivate(state, nameof(StudioWorkspaceState.PreviewImageDataUrl), "data:image/png;base64,abc123");

        var cut = RenderComponent<GlobalControlDrawer>();
        cut.Find("[data-testid='global-controls-toggle']").Click();

        var image = cut.Find("img.global-controls-preview-image");
        Assert.Equal("data:image/png;base64,abc123", image.GetAttribute("src"));
        Assert.Equal("Latest selected artifact preview", image.GetAttribute("alt"));
    }
}
