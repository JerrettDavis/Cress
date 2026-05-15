using System.Reflection;
using Cress.Core.Models;
using Cress.Execution;
using Bunit;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Components.Layout;
using Cress.Studio.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class GlobalControlDrawerTests : TestContext
{
    private StudioWorkspaceState CreateState(FakeStudioRecorderService? recorderService = null, FakeStudioCompanionClient? companionClient = null, IStudioRunnerService? runnerService = null)
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<Cress.Studio.Services.IStudioRecorderService>(recorderService ?? new FakeStudioRecorderService());
        Services.AddSingleton<Cress.Studio.Services.IStudioCompanionClient>(companionClient ?? new FakeStudioCompanionClient());
        if (runnerService is not null)
        {
            Services.AddSingleton(runnerService);
        }
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

    [Fact]
    public async Task GlobalControlDrawer_run_actions_dispatch_through_runner_and_close_button_hides_drawer()
    {
        var runner = new FakeRunnerService();
        var state = CreateState(runnerService: runner);
        var projectRoot = CreateProjectRoot();

        try
        {
            state.SetProjectPath(projectRoot);
            state.LoadProject();
            var flow = Assert.Single(state.AvailableFlows);
            Assert.NotNull(flow.SourceFile);
            SetPrivate(state, nameof(StudioWorkspaceState.SelectedFlow), new Cress.Studio.ViewModels.FlowDocumentViewModel
            {
                Id = flow.FlowId,
                Name = flow.Name,
                FilePath = flow.SourceFile!
            });
            state.CreateNewSuite();
            state.ToggleSuiteFlow(flow.FlowId, true);

            var cut = RenderComponent<GlobalControlDrawer>();
            cut.Find("[data-testid='global-controls-toggle']").Click();
            var componentType = typeof(GlobalControlDrawer);
            var runSuiteAsync = componentType.GetMethod("RunSuiteAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RunSuiteAsync was not found.");
            var runAllAsync = componentType.GetMethod("RunAllAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RunAllAsync was not found.");
            var runSelectedAsync = componentType.GetMethod("RunSelectedAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RunSelectedAsync was not found.");

            await (Task)(runSuiteAsync.Invoke(cut.Instance, []) ?? throw new InvalidOperationException("RunSuiteAsync returned null."));
            Assert.Single(runner.Requests);
            Assert.EndsWith("search.flow.yaml", runner.Requests[0].Options.FlowPath, StringComparison.OrdinalIgnoreCase);

            SetPrivate(state, nameof(StudioWorkspaceState.SelectedFlow), new Cress.Studio.ViewModels.FlowDocumentViewModel
            {
                Id = flow.FlowId,
                Name = flow.Name,
                FilePath = flow.SourceFile!
            });
            await (Task)(runAllAsync.Invoke(cut.Instance, []) ?? throw new InvalidOperationException("RunAllAsync returned null."));
            Assert.Equal(2, runner.Requests.Count);
            Assert.Null(runner.Requests[1].Options.FlowPath);

            SetPrivate(state, nameof(StudioWorkspaceState.SelectedFlow), new Cress.Studio.ViewModels.FlowDocumentViewModel
            {
                Id = flow.FlowId,
                Name = flow.Name,
                FilePath = flow.SourceFile!
            });
            await (Task)(runSelectedAsync.Invoke(cut.Instance, []) ?? throw new InvalidOperationException("RunSelectedAsync returned null."));
            Assert.Equal(3, runner.Requests.Count);
            Assert.EndsWith("search.flow.yaml", runner.Requests[2].Options.FlowPath, StringComparison.OrdinalIgnoreCase);

            cut.Find("[aria-label='Close control center']").Click();
            Assert.Empty(cut.FindAll("[data-testid='global-controls-drawer']"));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, true);
            }
        }
    }

    [Fact]
    public void GlobalControlDrawer_private_helpers_cover_idle_ready_and_long_elapsed_states()
    {
        var state = CreateState();
        var cut = RenderComponent<GlobalControlDrawer>();
        var panelType = typeof(GlobalControlDrawer);
        var buildPrimaryStatusLabel = panelType.GetMethod("BuildPrimaryStatusLabel", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildPrimaryStatusLabel was not found.");
        var buildToggleSummary = panelType.GetMethod("BuildToggleSummary", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildToggleSummary was not found.");
        var buildFlowSummary = panelType.GetMethod("BuildFlowSummary", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildFlowSummary was not found.");
        var trimPreview = panelType.GetMethod("TrimPreview", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TrimPreview was not found.");
        var formatElapsed = panelType.GetMethod("FormatElapsed", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FormatElapsed was not found.");
        var closeDrawer = panelType.GetMethod("CloseDrawer", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CloseDrawer was not found.");

        Assert.Equal("Idle", Assert.IsType<string>(buildPrimaryStatusLabel.Invoke(cut.Instance, [])));
        Assert.Equal("Load a project to enable run controls", Assert.IsType<string>(buildToggleSummary.Invoke(cut.Instance, [])));

        SetPrivate(state, nameof(StudioWorkspaceState.Snapshot), new StudioProjectSnapshot { Catalog = new ProjectCatalog() });
        Assert.Equal("Ready", Assert.IsType<string>(buildPrimaryStatusLabel.Invoke(cut.Instance, [])));
        Assert.Equal("Run or record without leaving this page", Assert.IsType<string>(buildToggleSummary.Invoke(cut.Instance, [])));
        Assert.Equal("No live flow selected", Assert.IsType<string>(buildFlowSummary.Invoke(cut.Instance, [])));
        Assert.Equal(new string('x', 1200) + "…", Assert.IsType<string>(trimPreview.Invoke(null, [new string('x', 1201)])));
        Assert.Equal("01:02:03", Assert.IsType<string>(formatElapsed.Invoke(null, [TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3)])));

        cut.Find("[data-testid='global-controls-toggle']").Click();
        closeDrawer.Invoke(cut.Instance, []);
        cut.Render();
        Assert.Empty(cut.FindAll("[data-testid='global-controls-drawer']"));
    }

    private static string CreateProjectRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cress-global-controls-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".cress", "profiles"));
        Directory.CreateDirectory(Path.Combine(root, "flows"));
        Directory.CreateDirectory(Path.Combine(root, "steps"));
        File.WriteAllText(Path.Combine(root, ".cress", "config.yaml"), """
        version: 1
        project:
          name: Drawer sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        """);
        File.WriteAllText(Path.Combine(root, ".cress", "profiles", "local.yaml"), """
        baseUrl: https://example.test
        """);
        File.WriteAllText(Path.Combine(root, "flows", "search.flow.yaml"), """
        version: 1
        id: flow.search
        name: Search flow
        when:
          - step: http.get
            with:
              url: https://example.test/search
        then:
          - expect: http.assert-status
            with:
              status: "200"
        """);
        File.WriteAllText(Path.Combine(root, "steps", "http.yaml"), """
        version: 1
        steps:
          - name: http.get
            implementation:
              plugin: builtin.http
              operation: get
          - name: http.assert-status
            implementation:
              plugin: builtin.http
              operation: assert-status
        """);
        return root;
    }

    private sealed class FakeRunnerService : IStudioRunnerService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public List<StudioRunnerDispatchRequest> Requests { get; } = [];

        public IReadOnlyList<StudioRunnerNodeSnapshot> ListNodes()
            =>
            [
                new(
                    Id: StudioEmbeddedRunnerNode.LocalNodeId,
                    Name: "Local embedded node",
                    DisplayName: "Test runner",
                    Description: "Fake runner",
                    Transport: StudioRunnerTransportKind.Embedded,
                    Location: "This machine",
                    Capabilities: ["http"],
                    Status: StudioRunnerNodeStatus.Healthy,
                    LastHeartbeatUtc: DateTimeOffset.UtcNow,
                    LastCompletedUtc: null,
                    ActiveDispatchId: null,
                    ActiveRunId: null,
                    LastRunId: null,
                    QueueDepth: 0,
                    LastError: null)
            ];

        public Task<StudioRunnerDispatchResult> DispatchAsync(
            StudioRunnerDispatchRequest request,
            IProgress<RuntimeProgressUpdate>? progress,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = Guid.NewGuid().ToString("N"),
                    ArtifactRoot = request.ProjectRoot,
                    Profile = request.Options.Profile ?? "local",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                    DurationMs = 1
                },
                Flows = []
            };

            return Task.FromResult(new StudioRunnerDispatchResult(
                request.NodeId,
                request.DispatchId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                result));
        }
    }
}
