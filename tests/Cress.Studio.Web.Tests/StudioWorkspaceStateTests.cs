using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Cress.Companion;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Recorder;
using Cress.Recorder.Inference;
using System.Reflection;

namespace Cress.Studio.Web.Tests;

public sealed class StudioWorkspaceStateTests : IDisposable
{
    private readonly List<string> _temporaryRoots = [];

    [Fact]
    public void SetRecentWorkspaces_normalizes_existing_paths_and_sets_default_input()
    {
        using var scope = CreateState();
        var first = CreateDirectory("recent-a");
        var second = CreateDirectory("recent-b");

        scope.State.SetProjectPath(null);
        scope.State.SetRecentWorkspaces([first, first, second, Path.Combine(first, "missing")]);

        Assert.Equal(2, scope.State.RecentWorkspaces.Count);
        Assert.Equal(first, scope.State.RecentWorkspaces[0]);
        Assert.Equal(second, scope.State.RecentWorkspaces[1]);
        Assert.Equal(first, scope.State.ProjectPathInput);
    }

    [Fact]
    public void RemoveRecentWorkspace_drops_entry_and_retargets_input_when_needed()
    {
        using var scope = CreateState();
        var first = CreateDirectory("recent-remove-a");
        var second = CreateDirectory("recent-remove-b");

        scope.State.SetProjectPath(first);
        scope.State.SetRecentWorkspaces([first, second]);

        scope.State.RemoveRecentWorkspace(first);

        Assert.Single(scope.State.RecentWorkspaces);
        Assert.Equal(second, scope.State.RecentWorkspaces[0]);
        Assert.Equal(second, scope.State.ProjectPathInput);
    }

    [Fact]
    public void RemoveRecentWorkspace_ignores_blank_and_unknown_paths()
    {
        using var scope = CreateState();
        var first = CreateDirectory("recent-remove-guard-a");
        var second = CreateDirectory("recent-remove-guard-b");

        scope.State.SetProjectPath(second);
        scope.State.SetRecentWorkspaces([first]);

        scope.State.RemoveRecentWorkspace(" ");
        scope.State.RemoveRecentWorkspace(second);

        Assert.Single(scope.State.RecentWorkspaces);
        Assert.Equal(first, scope.State.RecentWorkspaces[0]);
        Assert.Equal(second, scope.State.ProjectPathInput);
        Assert.Contains(second, scope.State.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearRecentWorkspaces_clears_history_and_falls_back_to_suggested_workspace()
    {
        using var scope = CreateState();
        var recent = CreateDirectory("recent-clear");

        scope.State.SetProjectPath(recent);
        scope.State.SetRecentWorkspaces([recent]);

        scope.State.ClearRecentWorkspaces();

        Assert.Empty(scope.State.RecentWorkspaces);
        Assert.Equal(scope.State.SuggestedWorkspacePath, scope.State.ProjectPathInput);
    }

    [Fact]
    public void UseSuggestedWorkspace_updates_project_path_input_when_available()
    {
        using var scope = CreateState();
        var suggested = CreateDirectory("suggested-use");
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.SuggestedWorkspacePath), suggested);
        scope.State.SetProjectPath(null);

        scope.State.UseSuggestedWorkspace();

        Assert.Equal(suggested, scope.State.ProjectPathInput);
        Assert.Contains("Ready to load", scope.State.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspacePicker_can_choose_a_folder_without_loading()
    {
        using var scope = CreateState();
        var root = CreateDirectory("picker-root");
        var child = Path.Combine(root, "child-workspace");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, ".placeholder"), "x");

        scope.State.SetProjectPath(root);
        scope.State.OpenWorkspacePicker();

        Assert.True(scope.State.IsWorkspacePickerOpen);
        Assert.Contains(scope.State.WorkspaceBrowserEntries, entry => entry.Path == child);

        scope.State.ChooseWorkspaceFromPicker(child, loadImmediately: false);

        Assert.False(scope.State.IsWorkspacePickerOpen);
        Assert.Equal(child, scope.State.ProjectPathInput);
        Assert.False(scope.State.HasLoadedProject);
    }

    [Fact]
    public void CloseWorkspacePicker_clears_picker_state_without_changing_project_load_state()
    {
        using var scope = CreateState();
        var root = CreateDirectory("picker-close");

        scope.State.BrowseWorkspacePath(Path.Combine(root, "missing"));
        Assert.NotNull(scope.State.WorkspaceBrowserError);

        scope.State.OpenWorkspacePicker();
        Assert.True(scope.State.IsWorkspacePickerOpen);

        scope.State.CloseWorkspacePicker();

        Assert.False(scope.State.IsWorkspacePickerOpen);
        Assert.Null(scope.State.WorkspaceBrowserError);
    }

    [Fact]
    public void LoadProject_loads_snapshot_and_supports_selecting_capabilities_fixtures_and_steps()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("state-project");

        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        Assert.True(scope.State.HasLoadedProject);
        Assert.Equal(1, scope.State.FlowCount);
        Assert.Equal(1, scope.State.CapabilityCount);
        Assert.Equal(1, scope.State.FixtureCount);
        Assert.Equal(1, scope.State.StepCount);
        Assert.Contains("Loaded", scope.State.StatusMessage, StringComparison.OrdinalIgnoreCase);

        var capability = Assert.Single(scope.State.Snapshot!.Catalog.Capabilities);
        scope.State.SelectCapability(capability);
        Assert.Equal(capability.Name, scope.State.SelectionHeadline);
        Assert.Contains("# Search capability", scope.State.SourceEditorText, StringComparison.Ordinal);

        scope.State.SelectFixture("shared.fixture");
        Assert.Equal("shared.fixture", scope.State.SelectionHeadline);
        Assert.Contains("seed.customer", scope.State.SelectedAssetSummary, StringComparison.Ordinal);

        scope.State.SelectStep("http.get");
        Assert.Equal("http.get", scope.State.SelectionHeadline);
        Assert.Contains("operation: get", scope.State.SourceEditorText, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadProject_with_blank_path_uses_suggested_workspace()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("suggested-workspace");
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.SuggestedWorkspacePath), projectRoot);

        scope.State.SetProjectPath(null);
        scope.State.LoadProject();

        Assert.True(scope.State.HasLoadedProject);
        Assert.Contains("Loaded", scope.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadProject_with_invalid_workspace_clears_loaded_state_and_surfaces_error()
    {
        using var scope = CreateState();
        var validRoot = CreateProject("load-valid-before-invalid");
        var invalidRoot = CreateDirectory("load-invalid-root");

        scope.State.SetProjectPath(validRoot);
        scope.State.LoadProject();
        Assert.True(scope.State.HasLoadedProject);
        Assert.NotNull(scope.State.SelectedFlow);

        scope.State.SetProjectPath(invalidRoot);
        scope.State.LoadProject();

        Assert.False(scope.State.HasLoadedProject);
        Assert.Null(scope.State.Snapshot);
        Assert.Null(scope.State.SelectedFlow);
        Assert.Null(scope.State.SelectedSuite);
        Assert.Equal("No run selected.", scope.State.SelectedRunComparison.Summary);
        Assert.False(string.IsNullOrWhiteSpace(scope.State.StatusMessage));
        Assert.DoesNotContain("Loaded", scope.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChooseWorkspaceFromPicker_can_load_selected_workspace_immediately()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("picker-load");

        scope.State.SetProjectPath(Path.Combine(projectRoot, "missing", "child"));
        scope.State.OpenWorkspacePicker();

        Assert.True(scope.State.IsWorkspacePickerOpen);

        scope.State.ChooseWorkspaceFromPicker(projectRoot, loadImmediately: true);

        Assert.False(scope.State.IsWorkspacePickerOpen);
        Assert.True(scope.State.HasLoadedProject);
        Assert.Equal(projectRoot, scope.State.ProjectPathInput);
        Assert.Contains(projectRoot, scope.State.RecentWorkspaces);
    }

    [Fact]
    public void LoadDemoWorkspace_reports_missing_demo_id()
    {
        using var scope = CreateState();

        scope.State.LoadDemoWorkspace("missing-demo");

        Assert.False(scope.State.HasLoadedProject);
        Assert.Contains("is not available", scope.State.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseWorkspacePath_reports_missing_directory_and_parent_navigation_recovers()
    {
        using var scope = CreateState();
        var root = CreateDirectory("picker-parent");
        var child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);

        scope.State.BrowseWorkspacePath(Path.Combine(child, "missing"));
        Assert.Contains("no longer exists", scope.State.WorkspaceBrowserError, StringComparison.OrdinalIgnoreCase);

        scope.State.BrowseWorkspacePath(child);
        Assert.Equal(child, scope.State.WorkspaceBrowserCurrentPath);

        scope.State.BrowseWorkspaceParent();

        Assert.Equal(root, scope.State.WorkspaceBrowserCurrentPath);
        Assert.Null(scope.State.WorkspaceBrowserError);
    }

    [Fact]
    public void LoadDemoWorkspace_uses_real_demo_and_loads_project()
    {
        using var scope = CreateState();
        var demoProject = CreateProject("demo-http");
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.DemoWorkspaces), new[]
        {
            new StudioDemoWorkspace(
                "httpbin-smoke",
                "HTTP smoke demo",
                "Self-contained demo for tests.",
                demoProject,
                ["service", "smoke"],
                null)
        });

        Assert.NotEmpty(scope.State.DemoWorkspaces);
        var demo = scope.State.DemoWorkspaces[0];

        scope.State.LoadDemoWorkspace(demo.Id);

        Assert.True(scope.State.HasLoadedProject);
        Assert.Contains(Path.GetFileName(demo.ProjectPath), scope.State.ProjectPathInput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(demo.PreferredProfile ?? scope.State.SelectedProfile, scope.State.SelectedProfile);
    }

    [Fact]
    public void LoadDemoWorkspace_loads_runnable_calculator_demo_flow()
    {
        using var scope = CreateState();
        var demoProject = CreateProject("calc-smoke");
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.DemoWorkspaces), new[]
        {
            new StudioDemoWorkspace(
                "calc-smoke",
                "Calculator desktop demo",
                "Self-contained calculator demo for tests.",
                demoProject,
                ["desktop", "calculator"],
                "local")
        });
        var demo = Assert.Single(scope.State.DemoWorkspaces, item => item.Id == "calc-smoke");

        scope.State.LoadDemoWorkspace(demo.Id);

        Assert.True(scope.State.HasLoadedProject);
        Assert.Equal("local", scope.State.SelectedProfile);
        Assert.Contains("calc-smoke", scope.State.ProjectPathInput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(demo.ProjectPath, scope.State.ProjectPathInput);
    }

    [Fact]
    public void Runner_node_changes_fall_back_to_first_available_node_when_selection_disappears()
    {
        var runner = new FakeRunnerService(
        [
            CreateNode("node-a", "Node A", StudioRunnerNodeStatus.Healthy),
            CreateNode("node-b", "Node B", StudioRunnerNodeStatus.Busy)
        ]);
        using var scope = CreateState(runnerService: runner);
        scope.State.SelectedRunnerNodeId = "node-b";

        runner.ReplaceNodes([CreateNode("node-a", "Node A", StudioRunnerNodeStatus.Healthy)]);
        runner.RaiseChanged();

        Assert.Equal("node-a", scope.State.SelectedRunnerNodeId);
        Assert.Equal("Node A", scope.State.SelectedRunnerNode?.DisplayName);
    }

    [Fact]
    public void Runner_node_changes_fall_back_to_local_node_when_none_are_available()
    {
        var runner = new FakeRunnerService([CreateNode("node-a", "Node A", StudioRunnerNodeStatus.Healthy)]);
        using var scope = CreateState(runnerService: runner);
        scope.State.SelectedRunnerNodeId = "node-a";

        runner.ReplaceNodes([]);
        runner.RaiseChanged();

        Assert.Equal(StudioEmbeddedRunnerNode.LocalNodeId, scope.State.SelectedRunnerNodeId);
        Assert.Null(scope.State.SelectedRunnerNode);
    }

    [Fact]
    public void CreateNewFlow_and_save_selected_flow_persist_new_document()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("create-save-flow");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        scope.State.CreateNewFlow();

        var created = scope.State.SelectedFlow;
        Assert.NotNull(created);
        Assert.Equal("New flow created.", scope.State.StatusMessage);
        Assert.Contains("Creating", scope.State.SelectedAssetSummary, StringComparison.Ordinal);

        scope.State.SaveSelectedFlow();

        Assert.True(File.Exists(created!.FilePath));
        Assert.Contains("Loaded flow", scope.State.StatusMessage, StringComparison.Ordinal);
        Assert.NotNull(scope.State.SelectedFlow);
    }

    [Fact]
    public void SaveSelectedFlow_reloads_project_even_when_snapshot_is_missing()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("save-flow-without-snapshot");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        scope.State.CreateNewFlow();

        var flowPath = scope.State.SelectedFlow!.FilePath;
        SetAutoProperty<StudioProjectSnapshot?>(scope.State, nameof(StudioWorkspaceState.Snapshot), null);

        scope.State.SaveSelectedFlow();

        Assert.True(File.Exists(flowPath));
        Assert.True(scope.State.HasLoadedProject);
        Assert.Equal(projectRoot, scope.State.Snapshot!.Catalog.ProjectRoot);
        Assert.Contains("Loaded flow", scope.State.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplySource_with_invalid_yaml_preserves_selection_and_reports_parse_failure()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("apply-source-invalid");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        var originalFlow = scope.State.SelectedFlow;
        Assert.NotNull(originalFlow);

        scope.State.SourceEditorText = """
        version: 1
        id: broken.flow
        when:
          - step: http.get
            with:
              url: [
        """;

        scope.State.ApplySource();

        Assert.Equal(originalFlow!.FilePath, scope.State.SelectedFlow?.FilePath);
        Assert.Equal("Source could not be parsed. Review diagnostics.", scope.State.StatusMessage);
        Assert.NotEmpty(scope.State.Diagnostics);
    }

    [Fact]
    public void RebuildSource_regenerates_source_from_selected_flow_model()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("rebuild-source");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var flow = scope.State.SelectedFlow;
        Assert.NotNull(flow);
        flow!.Name = "Renamed flow";
        scope.State.SourceEditorText = "stale";

        scope.State.RebuildSource();

        Assert.Contains("name: Renamed flow", scope.State.SourceEditorText, StringComparison.Ordinal);
        Assert.Equal(scope.State.SourceEditorText, flow.SourceText);
        Assert.Equal("Source regenerated from the designer.", scope.State.StatusMessage);
    }

    [Fact]
    public void Create_save_toggle_and_delete_suite_updates_summary_and_files()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("create-save-suite");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var flowId = Assert.Single(scope.State.AvailableFlows).FlowId;

        scope.State.CreateNewSuite();
        var suite = scope.State.SelectedSuite;
        Assert.NotNull(suite);
        Assert.Equal("New suite created.", scope.State.StatusMessage);
        Assert.Contains("all matching flows", scope.State.SelectedAssetSummary, StringComparison.Ordinal);

        scope.State.ToggleSuiteFlow(flowId, selected: true);
        Assert.Contains(flowId, suite!.FlowIds);
        Assert.Contains($"Flows: {flowId}", scope.State.SelectedAssetSummary, StringComparison.Ordinal);

        scope.State.SaveSelectedSuite();
        Assert.True(File.Exists(suite.FilePath));
        Assert.Contains("Loaded suite", scope.State.StatusMessage, StringComparison.Ordinal);

        var savedPath = suite.FilePath;
        scope.State.DeleteSelectedSuite();
        Assert.False(File.Exists(savedPath));
        Assert.Null(scope.State.SelectedSuite);
        Assert.Contains("Loaded flow", scope.State.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveSelectedSuite_with_invalid_document_reports_validation_failure()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("save-invalid-suite");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        scope.State.CreateNewSuite();

        scope.State.SelectedSuite!.Name = string.Empty;

        scope.State.SaveSelectedSuite();

        Assert.Equal("Suite save failed. Review diagnostics.", scope.State.StatusMessage);
        Assert.Contains(scope.State.Diagnostics, diagnostic => diagnostic.Code == "STE005");
    }

    [Fact]
    public void SelectFlow_with_invalid_yaml_reports_error_and_keeps_existing_selection()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("select-missing-flow");
        var brokenFlowPath = Path.Combine(projectRoot, "flows", "broken.flow.yaml");
        File.WriteAllText(brokenFlowPath, "version: [");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        var selectedBefore = scope.State.SelectedFlow;

        scope.State.SelectFlow(brokenFlowPath);

        Assert.Same(selectedBefore, scope.State.SelectedFlow);
        Assert.Contains("Could not load", scope.State.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(scope.State.Diagnostics, diagnostic => diagnostic.Code == "FLW001");
    }

    [Fact]
    public void Authoring_commands_without_selected_assets_leave_state_unchanged()
    {
        using var scope = CreateState();

        scope.State.CreateNewFlow();
        scope.State.SaveSelectedFlow();
        scope.State.ApplySource();
        scope.State.RebuildSource();
        scope.State.CreateNewSuite();
        scope.State.SaveSelectedSuite();
        scope.State.DeleteSelectedSuite();
        scope.State.ToggleSuiteFlow("flow.search", selected: true);
        scope.State.RefreshFlowAnalysis();

        Assert.False(scope.State.HasLoadedProject);
        Assert.False(scope.State.HasSelectedFlow);
        Assert.False(scope.State.HasSelectedSuite);
        Assert.Equal("Ready", scope.State.FlowAnalysis.Summary);
        Assert.Empty(scope.State.FlowAnalysis.QuickActions);
    }

    [Fact]
    public void ApplyQuickAction_updates_selected_flow_and_refreshes_analysis()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("quick-action-flow");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var originalFixtureCount = scope.State.SelectedFlow!.Fixtures.Count;

        scope.State.ApplyQuickAction("fixture.session");

        Assert.Equal(originalFixtureCount + 1, scope.State.SelectedFlow.Fixtures.Count);
        Assert.Equal(scope.State.SelectedFlow.SourceText, scope.State.SourceEditorText);
        Assert.Contains("Applied quick action", scope.State.SelectedAssetSummary, StringComparison.Ordinal);
        Assert.NotEmpty(scope.State.FlowAnalysis.QuickActions);

        SetAutoProperty<Cress.Studio.ViewModels.FlowDocumentViewModel?>(scope.State, nameof(StudioWorkspaceState.SelectedFlow), null);
        scope.State.RefreshFlowAnalysis();

        Assert.Equal("Ready", scope.State.FlowAnalysis.Summary);
        Assert.Empty(scope.State.FlowAnalysis.QuickActions);
    }

    [Fact]
    public async Task Idle_workspace_commands_cover_guard_paths_without_mutating_state()
    {
        using var scope = CreateState();
        scope.State.SetProjectPath(null);

        Assert.Equal(0, scope.State.FlowCount);
        Assert.Equal(0, scope.State.CapabilityCount);
        Assert.Equal(0, scope.State.FixtureCount);
        Assert.Equal(0, scope.State.StepCount);

        scope.State.LoadProject();
        scope.State.SelectFlow(null);
        scope.State.SelectFixture("missing.fixture");
        scope.State.SelectStep("missing.step");
        scope.State.StopActiveRun();

        await scope.State.AddFixtureRowAsync();
        await scope.State.RemoveFixtureRowAsync(0);
        await scope.State.AddActionRowAsync();
        await scope.State.RemoveActionRowAsync(0);
        await scope.State.AddExpectationRowAsync();
        await scope.State.RemoveExpectationRowAsync(0);
        await scope.State.RunSelectedAsync();
        await scope.State.RunAllAsync();
        await scope.State.RerunFromStepAsync();

        Assert.False(scope.State.HasLoadedProject);
        Assert.False(scope.State.HasLiveRunActivity);
        Assert.Equal("Choose a workspace path, browse for a folder, or load one of the demos to continue.", scope.State.StatusMessage);
    }

    [Fact]
    public async Task RerunFailedAsync_with_no_failed_flows_sets_informational_status()
    {
        using var scope = CreateState();
        var run = new StoredRunResult
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-clean",
                    ArtifactRoot = string.Empty,
                    Profile = "local",
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    EndedAt = DateTimeOffset.UtcNow
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        SourceFile = "flows\\search.flow.yaml",
                        Outcome = RunOutcome.Passed
                    }
                ]
            }
        };

        scope.State.SelectRun(run);
        await scope.State.RerunFailedAsync();

        Assert.Equal("The selected run has no failed flows to rerun.", scope.State.StatusMessage);
    }

    [Fact]
    public async Task RunSelectedSuiteAsync_with_unresolved_flow_selection_reports_runnable_flow_error()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("suite-run-validation");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        scope.State.CreateNewSuite();
        Assert.NotNull(scope.State.SelectedSuite);
        scope.State.SelectedSuite!.FlowIds.Add("flow.missing");

        await scope.State.RunSelectedSuiteAsync();

        Assert.Equal("Suite selection did not resolve to runnable flows.", scope.State.StatusMessage);
        Assert.NotEmpty(scope.State.Diagnostics);
    }

    [Fact]
    public void ToggleLiveLogVisibility_switches_flag_each_time()
    {
        using var scope = CreateState();

        Assert.False(scope.State.IsLiveLogVisible);

        scope.State.ToggleLiveLogVisibility();
        Assert.True(scope.State.IsLiveLogVisible);

        scope.State.ToggleLiveLogVisibility();
        Assert.False(scope.State.IsLiveLogVisible);
    }

    [Fact]
    public void Workspace_browser_root_invalid_path_and_parent_guard_paths_are_covered()
    {
        using var scope = CreateState();

        scope.State.BrowseWorkspacePath(null);
        Assert.Equal(string.Empty, scope.State.WorkspaceBrowserCurrentPath);
        Assert.Equal("This machine", scope.State.WorkspaceBrowserLocationLabel);
        Assert.NotNull(scope.State.WorkspaceBrowserEntries);

        scope.State.BrowseWorkspaceParent();
        Assert.Equal(string.Empty, scope.State.WorkspaceBrowserCurrentPath);

        scope.State.BrowseWorkspacePath("\0");
        Assert.Contains("Could not open", scope.State.WorkspaceBrowserError, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_state_accessors_refresh_and_recent_workspace_loading_cover_remaining_simple_paths()
    {
        using var scope = CreateState(recorderService: new StaticRecorderService(
            new RecordingTargetInfo { ProcessName = "calc", MainWindowTitle = "Calculator" },
            []));
        var projectRoot = CreateProject("state-accessors");
        var recentRoot = CreateProject("state-recent-load");

        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        scope.State.LiveTimelineEntries.Add(new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Run", "headline", "detail", "Running", "Search flow", null, "INFO"));
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.LiveStepStartedAt), DateTimeOffset.UtcNow.AddSeconds(-3));

        Assert.NotNull(scope.State.RecorderService);
        Assert.True(scope.State.IsRecording);
        Assert.Equal(0, scope.State.RecordedEventCount);
        Assert.Equal(TimeSpan.Zero, scope.State.RecordedElapsed);
        Assert.True(scope.State.HasLiveTimeline);
        Assert.True(scope.State.LiveStepElapsed > TimeSpan.Zero);
        Assert.Contains("local", scope.State.AvailableProfiles);
        Assert.Contains("capability.search", scope.State.CapabilityOptions);
        Assert.NotNull(scope.State.RunInsights);

        var run = new StoredRunResult
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-01",
                    ArtifactRoot = projectRoot,
                    Profile = "local",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow
                },
                Flows =
                [
                    new FlowRunResult { Outcome = RunOutcome.Passed, PassedWithRetry = true },
                    new FlowRunResult { Outcome = RunOutcome.Failed }
                ]
            }
        };
        Assert.Contains("1/2 passed", scope.State.DescribeRun(run), StringComparison.Ordinal);
        Assert.Contains("1 retried", scope.State.DescribeRun(run), StringComparison.Ordinal);

        scope.State.SetProjectPath(null);
        scope.State.SetRecentWorkspaces([recentRoot]);
        Assert.True(scope.State.HasRecentWorkspaces);

        scope.State.LoadRecentWorkspace(recentRoot);
        Assert.True(scope.State.HasLoadedProject);
        Assert.Equal(recentRoot, scope.State.ProjectPathInput);

        scope.State.RefreshProject();
        Assert.True(scope.State.HasLoadedProject);
    }

    [Fact]
    public void Recorder_event_passthroughs_cover_current_events_and_state_changed_notifications()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            Elapsed = TimeSpan.FromSeconds(4),
            CurrentTarget = new RecordingTargetInfo { ProcessId = 42, ProcessName = "sample-app", MainWindowTitle = "Sample App" }
        };
        using var scope = CreateState(recorderService: recorder);
        var notifications = 0;
        scope.State.Changed += () => notifications++;

        recorder.SimulateEvent(new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Invoke,
            Element = new ElementInfo { AutomationId = "save", ControlType = "Button" }
        });

        Assert.Single(scope.State.CurrentEvents);
        Assert.Equal(1, scope.State.RecordedEventCount);
        Assert.Equal(TimeSpan.FromSeconds(4), scope.State.RecordedElapsed);
        Assert.True(notifications > 0);
    }

    [Fact]
    public void Private_initial_flow_resolution_falls_back_to_flows_directory_when_catalog_paths_are_missing()
    {
        var recorder = new FakeStudioRecorderService();
        using var scope = CreateState(recorderService: recorder);
        var projectRoot = CreateProject("state-initial-flow");
        var flowPath = Path.Combine(projectRoot, "flows", "search.flow.yaml");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var snapshot = scope.State.Snapshot ?? throw new InvalidOperationException("Snapshot was not loaded.");
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.Snapshot), snapshot with
        {
            Catalog = snapshot.Catalog with
            {
                NormalizedFlows =
                [
                    new NormalizedFlow
                    {
                        FlowId = "flow.search",
                        Name = "Search flow"
                    }
                ]
            }
        });

        var resolveInitialFlowPath = typeof(StudioWorkspaceState).GetMethod("ResolveInitialFlowPath", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveInitialFlowPath was not found.");

        Assert.Equal(flowPath, Assert.IsType<string>(resolveInitialFlowPath.Invoke(scope.State, [])));
    }

    [Fact]
    public void Private_live_progress_helpers_cover_resolution_status_and_timeline_trimming()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("state-live-helpers");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var stateType = typeof(StudioWorkspaceState);
        var flowOrder = (Dictionary<string, int>)(stateType.GetField("_liveFlowOrder", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(scope.State)
            ?? throw new InvalidOperationException("_liveFlowOrder field was not found."));
        var flowOffsets = (Dictionary<string, int>)(stateType.GetField("_liveFlowStepOffsets", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(scope.State)
            ?? throw new InvalidOperationException("_liveFlowStepOffsets field was not found."));
        var resolveFlowIndex = stateType.GetMethod("ResolveFlowIndex", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveFlowIndex was not found.");
        var resolveFlowStepCount = stateType.GetMethod("ResolveFlowStepCount", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveFlowStepCount was not found.");
        var calculateOverallStepPosition = stateType.GetMethod("CalculateOverallStepPosition", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CalculateOverallStepPosition was not found.");
        var addLiveTimelineEntry = stateType.GetMethod("AddLiveTimelineEntry", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AddLiveTimelineEntry was not found.");
        var resolveLiveEntryStatus = stateType.GetMethod("ResolveLiveEntryStatus", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveLiveEntryStatus was not found.");

        flowOrder["flow.search"] = 2;
        flowOffsets["flow.search"] = 10;
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.LiveCompletedSteps), 7);

        Assert.Equal(2, Assert.IsType<int>(resolveFlowIndex.Invoke(scope.State, ["flow.search"])));
        Assert.Equal(0, Assert.IsType<int>(resolveFlowIndex.Invoke(scope.State, [null])));
        Assert.Equal(2, Assert.IsType<int>(resolveFlowStepCount.Invoke(scope.State, ["flow.search"])));
        Assert.Equal(0, Assert.IsType<int>(resolveFlowStepCount.Invoke(scope.State, [null])));
        Assert.Equal(12, Assert.IsType<int>(calculateOverallStepPosition.Invoke(scope.State, ["flow.search", 2])));
        Assert.Equal(7, Assert.IsType<int>(calculateOverallStepPosition.Invoke(scope.State, [null, 0])));

        var seedEntry = new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Step", "seed", "detail", "Running", "Search flow", "http.get", "INFO");
        addLiveTimelineEntry.Invoke(scope.State, [seedEntry, false]);
        Assert.Single(scope.State.LiveTimelineEntries);
        Assert.Empty(scope.State.LiveEvents);

        for (var index = 0; index < 205; index++)
        {
            addLiveTimelineEntry.Invoke(scope.State, [new StudioLiveRunEntry(DateTimeOffset.UtcNow, "Log", $"headline-{index}", "detail", "Info", "Search flow", null, "INFO"), true]);
        }

        Assert.Equal(200, scope.State.LiveTimelineEntries.Count);
        Assert.Equal(200, scope.State.LiveEvents.Count);
        Assert.Equal("headline-204", scope.State.LiveEvents[0]);
        Assert.Equal("headline-5", scope.State.LiveEvents[^1]);

        Assert.Equal("Warning", Assert.IsType<string>(resolveLiveEntryStatus.Invoke(null, [new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.Log,
            LogLevel = "WARN"
        }])));
        Assert.Equal("Error", Assert.IsType<string>(resolveLiveEntryStatus.Invoke(null, [new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.Log,
            LogLevel = "ERROR"
        }])));
        Assert.Equal("Info", Assert.IsType<string>(resolveLiveEntryStatus.Invoke(null, [new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.Log,
            LogLevel = "INFO"
        }])));
        Assert.Equal("Passed", Assert.IsType<string>(resolveLiveEntryStatus.Invoke(null, [new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.RunCompleted,
            Run = new RunResult
            {
                Flows = [new FlowRunResult { Outcome = RunOutcome.Passed }]
            }
        }])));
        Assert.Equal("Failed", Assert.IsType<string>(resolveLiveEntryStatus.Invoke(null, [new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.FlowCompleted,
            Flow = new FlowRunResult { Outcome = RunOutcome.Failed }
        }])));
        Assert.Equal("Running", Assert.IsType<string>(resolveLiveEntryStatus.Invoke(null, [new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.StepStarted
        }])));
        Assert.Equal("Info", Assert.IsType<string>(resolveLiveEntryStatus.Invoke(null, [new RuntimeProgressUpdate
        {
            Kind = (RuntimeProgressKind)999
        }])));
        Assert.Equal("Failed", Assert.IsType<string>(resolveLiveEntryStatus.Invoke(null, [new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.StepCompleted,
            Step = new StepRunResult
            {
                Name = "http.get",
                Outcome = RunOutcome.Errored
            }
        }])));
    }

    [Fact]
    public void Private_run_resolution_helpers_cover_path_name_tag_and_browser_selection()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("state-run-resolution");
        var apiFlowPath = Path.Combine(projectRoot, "flows", "search.flow.yaml");
        var browserFlowPath = Path.Combine(projectRoot, "flows", "browser.flow.yaml");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var snapshot = scope.State.Snapshot ?? throw new InvalidOperationException("Snapshot was not loaded.");
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.Snapshot), snapshot with
        {
            Catalog = snapshot.Catalog with
            {
                NormalizedFlows =
                [
                    new NormalizedFlow
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        SourceFile = apiFlowPath,
                        Tags = ["api"],
                        Actions = [new NormalizedExecutable { Name = "http.get" }],
                        Expectations = [new NormalizedExecutable { Name = "http.assert-status" }]
                    },
                    new NormalizedFlow
                    {
                        FlowId = "flow.browser",
                        Name = "Browser flow",
                        SourceFile = browserFlowPath,
                        Tags = ["ui"],
                        Actions = [new NormalizedExecutable { Name = "browser.open" }],
                        Expectations = [new NormalizedExecutable { Name = "browser.assert-title" }]
                    }
                ]
            }
        });

        var stateType = typeof(StudioWorkspaceState);
        var resolveRunFlows = stateType.GetMethod("ResolveRunFlows", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveRunFlows was not found.");
        var runTargetsBrowserWorkflow = stateType.GetMethod("RunTargetsBrowserWorkflow", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RunTargetsBrowserWorkflow was not found.");

        Assert.Equal("flow.browser", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<NormalizedFlow>>(resolveRunFlows.Invoke(scope.State, [new RunOptions
        {
            FlowPaths = [Path.Combine("flows", "browser.flow.yaml")]
        }]!))).FlowId);
        Assert.Equal("flow.search", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<NormalizedFlow>>(resolveRunFlows.Invoke(scope.State, [new RunOptions
        {
            FlowPath = "search"
        }]!))).FlowId);
        Assert.Equal("flow.browser", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<NormalizedFlow>>(resolveRunFlows.Invoke(scope.State, [new RunOptions
        {
            FlowPath = "Browser"
        }]!))).FlowId);
        Assert.Equal("flow.search", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<NormalizedFlow>>(resolveRunFlows.Invoke(scope.State, [new RunOptions
        {
            Tag = "api"
        }]!))).FlowId);
        Assert.True(Assert.IsType<bool>(runTargetsBrowserWorkflow.Invoke(scope.State, [new RunOptions
        {
            FlowPath = browserFlowPath
        }])));
        Assert.False(Assert.IsType<bool>(runTargetsBrowserWorkflow.Invoke(scope.State, [new RunOptions
        {
            FlowPath = apiFlowPath
        }])));
    }

    [Fact]
    public async Task Private_live_ticker_notifies_while_step_is_active_and_stops_cleanly()
    {
        using var scope = CreateState();
        var stateType = typeof(StudioWorkspaceState);
        var startLiveTicker = stateType.GetMethod("StartLiveTicker", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("StartLiveTicker was not found.");
        var stopLiveTicker = stateType.GetMethod("StopLiveTicker", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("StopLiveTicker was not found.");

        var notifications = 0;
        scope.State.Changed += () => Interlocked.Increment(ref notifications);
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.LiveStepStartedAt), DateTimeOffset.UtcNow);

        startLiveTicker.Invoke(scope.State, []);
        await WaitForAsync(() => Volatile.Read(ref notifications) > 0, timeoutMilliseconds: 4000);
        stopLiveTicker.Invoke(scope.State, []);

        var notified = Volatile.Read(ref notifications);
        await Task.Delay(1200);

        Assert.True(notified > 0);
        Assert.Equal(notified, Volatile.Read(ref notifications));
    }

    [Fact]
    public void Private_workspace_helpers_cover_start_priority_history_file_resolution_and_suite_summary()
    {
        using var scope = CreateState();
        var first = CreateProject("workspace-helper-a");
        var second = CreateProject("workspace-helper-b");
        var third = CreateProject("workspace-helper-c");
        var extraRoots = Enumerable.Range(0, 7).Select(index => CreateDirectory($"recent-{index}")).ToList();

        var stateType = typeof(StudioWorkspaceState);
        var resolveProjectStartDirectory = stateType.GetMethod("ResolveProjectStartDirectory", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveProjectStartDirectory was not found.");
        var rememberWorkspace = stateType.GetMethod("RememberWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RememberWorkspace was not found.");
        var resolveProjectFilePath = stateType.GetMethod("ResolveProjectFilePath", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveProjectFilePath was not found.");
        var buildSuiteSummary = stateType.GetMethod("BuildSuiteSummary", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildSuiteSummary was not found.");

        scope.State.SetProjectPath(first);
        scope.State.SetRecentWorkspaces([second]);
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.SuggestedWorkspacePath), third);
        Assert.Equal(first, Assert.IsType<string>(resolveProjectStartDirectory.Invoke(scope.State, [])));

        scope.State.SetProjectPath(null);
        scope.State.SetRecentWorkspaces([second]);
        Assert.Equal(second, Assert.IsType<string>(resolveProjectStartDirectory.Invoke(scope.State, [])));

        scope.State.ClearRecentWorkspaces();
        Assert.Equal(third, Assert.IsType<string>(resolveProjectStartDirectory.Invoke(scope.State, [])));

        scope.State.SetProjectPath(null);
        SetAutoProperty<string?>(scope.State, nameof(StudioWorkspaceState.SuggestedWorkspacePath), null);
        Assert.Null(resolveProjectStartDirectory.Invoke(scope.State, []));

        foreach (var root in new[] { first, second, third }.Concat(extraRoots))
        {
            rememberWorkspace.Invoke(scope.State, [root]);
        }

        rememberWorkspace.Invoke(scope.State, [second]);
        Assert.Equal(8, scope.State.RecentWorkspaces.Count);
        Assert.Equal(Path.GetFullPath(second), scope.State.RecentWorkspaces[0]);
        Assert.Equal(1, scope.State.RecentWorkspaces.Count(path => string.Equals(path, Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase)));

        Assert.Null(resolveProjectFilePath.Invoke(null, [first, null]));
        Assert.Equal(Path.Combine(first, "flows", "search.flow.yaml"), Assert.IsType<string>(resolveProjectFilePath.Invoke(null, [first, Path.Combine("flows", "search.flow.yaml")])));
        Assert.Equal(second, Assert.IsType<string>(resolveProjectFilePath.Invoke(null, [first, second])));

        var suite = new StudioSuiteEditorModel
        {
            Id = "suite.smoke",
            Name = "Smoke suite",
            Description = "Covers smoke scenarios.",
            Profile = null,
            Tag = null,
            ReportFormatsText = "html, json"
        };
        suite.FlowIds.Add("flow.b");
        suite.FlowIds.Add("flow.a");

        var summary = Assert.IsType<string>(buildSuiteSummary.Invoke(null, [suite]));
        Assert.Contains("Profile: inherit", summary, StringComparison.Ordinal);
        Assert.Contains("Tag filter: none", summary, StringComparison.Ordinal);
        Assert.Contains("Flows: flow.a, flow.b", summary, StringComparison.Ordinal);
        Assert.Contains("Reports: html, json", summary, StringComparison.Ordinal);
        Assert.Contains("Covers smoke scenarios.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Recorder_panels_can_open_and_close_while_clearing_last_result()
    {
        using var scope = CreateState();
        SetAutoProperty(scope.State, nameof(StudioWorkspaceState.LastRecordingResult), new RecordingResult
        {
            Steps = [new InferredStep { Kind = StepKind.Click, SourceTimestamp = DateTime.UtcNow }]
        });

        scope.State.OpenSavePanel();
        Assert.True(scope.State.IsRecorderSavePanelOpen);

        scope.State.CloseSavePanel();
        Assert.False(scope.State.IsRecorderSavePanelOpen);
        Assert.Null(scope.State.LastRecordingResult);

        scope.State.OpenRecorderPicker();
        Assert.True(scope.State.IsRecorderPickerOpen);

        scope.State.CloseRecorderPicker();
        Assert.False(scope.State.IsRecorderPickerOpen);
    }

    [Fact]
    public async Task RefreshCompanionAsync_formats_unavailable_and_multi_session_status_messages()
    {
        var companion = new FakeStudioCompanionClient();
        using var scope = CreateState(companionClient: companion);

        await scope.State.RefreshCompanionAsync();
        Assert.Contains("unavailable", scope.State.CompanionStatusMessage, StringComparison.OrdinalIgnoreCase);

        companion.Snapshot = new CompanionServiceSnapshot
        {
            IsAvailable = true,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Sessions =
            [
                new CompanionSessionSnapshot { ProcessId = 1, ProcessName = "one", WindowTitle = "One", Status = CompanionSessionStatus.Recording, StartedAtUtc = DateTimeOffset.UtcNow },
                new CompanionSessionSnapshot { ProcessId = 2, ProcessName = "two", WindowTitle = "Two", Status = CompanionSessionStatus.Paused, StartedAtUtc = DateTimeOffset.UtcNow }
            ]
        };

        await scope.State.RefreshCompanionAsync();
        Assert.Contains("tracking 2 apps", scope.State.CompanionStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginCompanionRecordingAsync_surfaces_companion_errors()
    {
        using var scope = CreateState(companionClient: new ThrowingCompanionClient());

        await scope.State.BeginCompanionRecordingAsync(42);

        Assert.Contains("Desktop companion error", scope.State.RecordingError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenRecorderPicker_refreshes_companion_targets_and_status()
    {
        var companion = new FakeStudioCompanionClient
        {
            Snapshot = new CompanionServiceSnapshot
            {
                IsAvailable = true,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Sessions = []
            },
            Targets =
            [
                new CompanionTargetInfo
                {
                    ProcessId = 42,
                    ProcessName = "sample-app",
                    WindowTitle = "Sample App",
                    IsAttachable = true
                }
            ]
        };
        using var scope = CreateState(companionClient: companion);

        scope.State.OpenRecorderPicker();
        await WaitForAsync(() => !scope.State.IsCompanionLoading && scope.State.CompanionTargets.Count == 1);

        Assert.True(scope.State.IsRecorderPickerOpen);
        Assert.Equal("sample-app", scope.State.CompanionTargets[0].ProcessName);
        Assert.Contains("Desktop companion is online", scope.State.CompanionStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Companion_properties_reflect_availability_and_sessions()
    {
        var companion = new FakeStudioCompanionClient
        {
            Snapshot = new CompanionServiceSnapshot
            {
                IsAvailable = true,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Sessions =
                [
                    new CompanionSessionSnapshot
                    {
                        ProcessId = 42,
                        ProcessName = "sample-app",
                        WindowTitle = "Sample App",
                        Status = CompanionSessionStatus.Recording,
                        StartedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            }
        };
        using var scope = CreateState(companionClient: companion);

        await scope.State.RefreshCompanionAsync();

        Assert.True(scope.State.IsCompanionAvailable);
        Assert.True(scope.State.HasCompanionSessions);
    }

    [Fact]
    public async Task Companion_recording_commands_update_snapshot_state()
    {
        var companion = new FakeStudioCompanionClient
        {
            Snapshot = new CompanionServiceSnapshot
            {
                IsAvailable = true,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Sessions = []
            },
            Targets =
            [
                new CompanionTargetInfo
                {
                    ProcessId = 42,
                    ProcessName = "sample-app",
                    WindowTitle = "Sample App",
                    IsAttachable = true
                }
            ]
        };
        using var scope = CreateState(companionClient: companion);

        await scope.State.BeginCompanionRecordingAsync(42);
        Assert.Equal(42, companion.LastStartedProcessId);
        Assert.Equal(CompanionSessionStatus.Recording, Assert.Single(scope.State.CompanionSnapshot.Sessions).Status);

        await scope.State.PauseCompanionRecordingAsync(42);
        Assert.Equal(42, companion.LastPausedProcessId);
        Assert.Equal(CompanionSessionStatus.Paused, Assert.Single(scope.State.CompanionSnapshot.Sessions).Status);

        await scope.State.ResumeCompanionRecordingAsync(42);
        Assert.Equal(42, companion.LastResumedProcessId);
        Assert.Equal(CompanionSessionStatus.Recording, Assert.Single(scope.State.CompanionSnapshot.Sessions).Status);

        await scope.State.StopCompanionRecordingAsync(42);
        Assert.Equal(42, companion.LastStoppedProcessId);
        Assert.Equal(CompanionSessionStatus.Stopped, Assert.Single(scope.State.CompanionSnapshot.Sessions).Status);
    }

    [Fact]
    public async Task BeginRecordingAsync_formats_access_denied_errors()
    {
        using var scope = CreateState(recorderService: new ThrowingRecorderService
        {
            StartRecordingException = new UnauthorizedAccessException("denied")
        });

        await scope.State.BeginRecordingAsync(42);

        Assert.Contains("Access denied", scope.State.RecordingError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginRecordingAsync_formats_exited_and_com_errors()
    {
        using var exitedScope = CreateState(recorderService: new ThrowingRecorderService
        {
            StartRecordingException = new InvalidOperationException("target exited before recording could start")
        });

        await exitedScope.State.BeginRecordingAsync(42);

        Assert.Contains("exited before recording could start", exitedScope.State.RecordingError, StringComparison.Ordinal);

        using var comScope = CreateState(recorderService: new ThrowingRecorderService
        {
            StartRecordingException = new System.Runtime.InteropServices.COMException("uia failed", unchecked((int)0x80004005))
        });

        await comScope.State.BeginRecordingAsync(42);

        Assert.Contains("UIA COM error", comScope.State.RecordingError, StringComparison.Ordinal);
        Assert.Contains("80004005", comScope.State.RecordingError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginWebRecordingAsync_formats_node_not_found_errors()
    {
        using var scope = CreateState(recorderService: new ThrowingRecorderService
        {
            StartWebRecordingException = new InvalidOperationException("Node.js not found on PATH; install Node.js 18+ to enable web recording.")
        });

        await scope.State.BeginWebRecordingAsync("https://example.test", "chromium");

        Assert.Contains("Node.js not found", scope.State.RecordingError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginWebRecordingAsync_formats_generic_node_errors()
    {
        using var scope = CreateState(recorderService: new ThrowingRecorderService
        {
            StartWebRecordingException = new InvalidOperationException("node failed to start browser")
        });

        await scope.State.BeginWebRecordingAsync("https://example.test", "chromium");

        Assert.Equal("Web recording error: node failed to start browser", scope.State.RecordingError);
    }

    [Fact]
    public void Recorder_properties_expose_target_name_fallbacks_and_inferred_steps()
    {
        var inferredStep = new InferredStep
        {
            Kind = StepKind.Click,
            SourceTimestamp = DateTime.UtcNow
        };
        using var titledScope = CreateState(recorderService: new StaticRecorderService(
            new RecordingTargetInfo { MainWindowTitle = "Calculator", ProcessName = "calc" },
            [inferredStep]));

        Assert.Equal("Calculator", titledScope.State.RecordingTargetName);
        Assert.Single(titledScope.State.CurrentInferredSteps);

        using var processScope = CreateState(recorderService: new StaticRecorderService(
            new RecordingTargetInfo { ProcessName = "calc", MainWindowTitle = null! },
            []));
        Assert.Equal("calc", processScope.State.RecordingTargetName);

        using var emptyScope = CreateState(recorderService: new StaticRecorderService(null, []));
        Assert.Null(emptyScope.State.RecordingTargetName);
    }

    [Fact]
    public async Task EndRecordingAsync_returns_empty_result_and_sets_error_when_stop_fails()
    {
        using var scope = CreateState(recorderService: new ThrowingRecorderService
        {
            StopRecordingException = new ArgumentException("bad target")
        });

        var result = await scope.State.EndRecordingAsync();

        Assert.Empty(result.Events);
        Assert.Empty(result.Steps);
        Assert.Contains("Invalid target:", scope.State.RecordingError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndRecordingAsync_stores_result_and_opens_save_panel_when_stop_succeeds()
    {
        var expected = new RecordingResult
        {
            Events =
            [
                new RecordedEvent
                {
                    Sequence = 1,
                    Timestamp = DateTimeOffset.UtcNow,
                    Kind = EventKind.Invoke
                }
            ],
            Steps =
            [
                new InferredStep
                {
                    Kind = StepKind.Click,
                    SourceTimestamp = DateTime.UtcNow
                }
            ]
        };
        using var scope = CreateState(recorderService: new ResultRecorderService(expected));

        var result = await scope.State.EndRecordingAsync();

        Assert.Same(expected, result);
        Assert.Same(expected, scope.State.LastRecordingResult);
        Assert.True(scope.State.IsRecorderSavePanelOpen);
        Assert.Null(scope.State.RecordingError);
    }

    private StateScope CreateState(IStudioRecorderService? recorderService = null, IStudioCompanionClient? companionClient = null, IStudioRunnerService? runnerService = null)
    {
        var services = new ServiceCollection();
        services.AddCressStudioBackend();
        services.AddSingleton<IStudioRecorderService>(recorderService ?? new FakeStudioRecorderService());
        services.AddSingleton<IStudioCompanionClient>(companionClient ?? new FakeStudioCompanionClient());
        if (runnerService is not null)
        {
            services.AddSingleton(runnerService);
        }
        services.AddSingleton<StudioWorkspaceState>();
        var provider = services.BuildServiceProvider();
        return new StateScope(provider, provider.GetRequiredService<StudioWorkspaceState>());
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "cress-studio-state-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        _temporaryRoots.Add(Path.GetDirectoryName(path)!);
        return path;
    }

    private string CreateProject(string name)
    {
        var root = CreateDirectory(name);
        WriteFile(root, Path.Combine(".cress", "config.yaml"), """
        version: 1
        project:
          name: Studio state sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        """);
        WriteFile(root, Path.Combine(".cress", "profiles", "local.yaml"), """
        baseUrl: https://example.test
        """);
        WriteFile(root, Path.Combine("capabilities", "search.md"), """
        ---
        version: 1
        id: capability.search
        owner: qa
        risk: medium
        ---

        # Search capability

        ## Rules
        - Return relevant results.
        """);
        WriteFile(root, Path.Combine("flows", "search.flow.yaml"), """
        version: 1
        id: flow.search
        name: Search flow
        fixtures:
          customer:
            use: shared.fixture
        when:
          - step: http.get
            with:
              url: https://example.test/search
        then:
          - expect: http.assert-status
            with:
              status: "200"
        """);
        WriteFile(root, Path.Combine("steps", "http.yaml"), """
        version: 1
        steps:
          - name: http.get
            implementation:
              plugin: builtin.http
              operation: get
        """);
        WriteFile(root, Path.Combine("fixtures", "fixtures.yaml"), """
        version: 1
        fixtures:
          shared.fixture:
            type: seed.customer
            strategy: static
        """);

        return root;
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static void SetAutoProperty<T>(object target, string propertyName, T value)
    {
        var field = target.GetType().GetField($"<{propertyName}>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Backing field for '{propertyName}' was not found on {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    public void Dispose()
    {
        foreach (var root in _temporaryRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private sealed record StateScope(ServiceProvider Provider, StudioWorkspaceState State) : IDisposable
    {
        public void Dispose()
            => Provider.Dispose();
    }

    private sealed class ThrowingRecorderService : IStudioRecorderService
    {
        public bool IsRecording => false;
        public RecordingTargetInfo? CurrentTarget => null;
        public int CapturedEventCount => 0;
        public TimeSpan Elapsed => TimeSpan.Zero;
        public IReadOnlyList<RecordedEvent> CurrentEvents => [];
        public IReadOnlyList<InferredStep> CurrentInferredSteps => [];
        public Exception? StartRecordingException { get; init; }
        public Exception? StartWebRecordingException { get; init; }
        public Exception? StopRecordingException { get; init; }
        public event Action? StateChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<RecordingTargetInfo>> ListAvailableTargetsAsync()
            => Task.FromResult<IReadOnlyList<RecordingTargetInfo>>([]);

        public Task StartRecordingAsync(int processId)
            => Task.FromException(StartRecordingException ?? new InvalidOperationException("start failed"));

        public Task StartWebRecordingAsync(string url, string browserType, CancellationToken ct = default)
            => Task.FromException(StartWebRecordingException ?? new InvalidOperationException("web start failed"));

        public Task<RecordingResult> StopRecordingAsync()
            => Task.FromException<RecordingResult>(StopRecordingException ?? new InvalidOperationException("stop failed"));

        public Task<RecordingReplayResult> ReplayRecordedFlowAsync(string flowFilePath, string projectPath)
            => Task.FromResult(new RecordingReplayResult());
    }

    private sealed class StaticRecorderService(
        RecordingTargetInfo? currentTarget,
        IReadOnlyList<InferredStep> currentInferredSteps) : IStudioRecorderService
    {
        public bool IsRecording => currentTarget is not null;
        public RecordingTargetInfo? CurrentTarget => currentTarget;
        public int CapturedEventCount => 0;
        public TimeSpan Elapsed => TimeSpan.Zero;
        public IReadOnlyList<RecordedEvent> CurrentEvents => [];
        public IReadOnlyList<InferredStep> CurrentInferredSteps => currentInferredSteps;
        public event Action? StateChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<RecordingTargetInfo>> ListAvailableTargetsAsync()
            => Task.FromResult<IReadOnlyList<RecordingTargetInfo>>([]);

        public Task StartRecordingAsync(int processId)
            => Task.CompletedTask;

        public Task StartWebRecordingAsync(string url, string browserType, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<RecordingResult> StopRecordingAsync()
            => Task.FromResult(new RecordingResult());

        public Task<RecordingReplayResult> ReplayRecordedFlowAsync(string flowFilePath, string projectPath)
            => Task.FromResult(new RecordingReplayResult());
    }

    private sealed class ResultRecorderService(RecordingResult result) : IStudioRecorderService
    {
        public bool IsRecording => false;
        public RecordingTargetInfo? CurrentTarget => null;
        public int CapturedEventCount => result.Events.Count;
        public TimeSpan Elapsed => TimeSpan.Zero;
        public IReadOnlyList<RecordedEvent> CurrentEvents => result.Events;
        public IReadOnlyList<InferredStep> CurrentInferredSteps => result.Steps;
        public event Action? StateChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<RecordingTargetInfo>> ListAvailableTargetsAsync()
            => Task.FromResult<IReadOnlyList<RecordingTargetInfo>>([]);

        public Task StartRecordingAsync(int processId)
            => Task.CompletedTask;

        public Task StartWebRecordingAsync(string url, string browserType, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<RecordingResult> StopRecordingAsync()
            => Task.FromResult(result);

        public Task<RecordingReplayResult> ReplayRecordedFlowAsync(string flowFilePath, string projectPath)
            => Task.FromResult(new RecordingReplayResult());
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Condition was not met before timeout.");
    }

    private static StudioRunnerNodeSnapshot CreateNode(string id, string displayName, StudioRunnerNodeStatus status)
        => new(
            id,
            displayName,
            displayName,
            $"{displayName} description",
            StudioRunnerTransportKind.Embedded,
            "This machine",
            ["web"],
            status,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            0,
            null);

    private sealed class FakeRunnerService(IReadOnlyList<StudioRunnerNodeSnapshot> nodes) : IStudioRunnerService
    {
        private IReadOnlyList<StudioRunnerNodeSnapshot> _nodes = nodes;

        public event Action? Changed;

        public IReadOnlyList<StudioRunnerNodeSnapshot> ListNodes() => _nodes;

        public Task<StudioRunnerDispatchResult> DispatchAsync(
            StudioRunnerDispatchRequest request,
            IProgress<RuntimeProgressUpdate>? progress,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void ReplaceNodes(IReadOnlyList<StudioRunnerNodeSnapshot> nodes)
            => _nodes = nodes;

        public void RaiseChanged()
            => Changed?.Invoke();
    }

    private sealed class ThrowingCompanionClient : IStudioCompanionClient
    {
        public Task<CompanionServiceSnapshot> GetSnapshotAsync(bool includePreview = false, CancellationToken cancellationToken = default)
            => Task.FromResult(new CompanionServiceSnapshot { IsAvailable = false, GeneratedAtUtc = DateTimeOffset.UtcNow, Sessions = [] });

        public Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CompanionTargetInfo>>([]);

        public Task<CompanionSessionSnapshot> StartRecordingAsync(int processId, bool overlayEnabled = true, CancellationToken cancellationToken = default)
            => Task.FromException<CompanionSessionSnapshot>(new InvalidOperationException("boom"));

        public Task<CompanionSessionSnapshot> PauseRecordingAsync(int processId, CancellationToken cancellationToken = default)
            => Task.FromException<CompanionSessionSnapshot>(new InvalidOperationException("boom"));

        public Task<CompanionSessionSnapshot> ResumeRecordingAsync(int processId, CancellationToken cancellationToken = default)
            => Task.FromException<CompanionSessionSnapshot>(new InvalidOperationException("boom"));

        public Task<CompanionSessionSnapshot> StopRecordingAsync(int processId, CancellationToken cancellationToken = default)
            => Task.FromException<CompanionSessionSnapshot>(new InvalidOperationException("boom"));
    }
}
