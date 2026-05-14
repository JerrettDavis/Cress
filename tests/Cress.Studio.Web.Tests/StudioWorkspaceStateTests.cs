using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Cress.Companion;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Recorder;
using Cress.Recorder.Inference;

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
