using Cress.Core.Models;
using Cress.Execution;
using Cress.Recorder.Inference;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class StudioWorkspaceRunTests : IDisposable
{
    private readonly List<string> _temporaryRoots = [];

    [Fact]
    public async Task WorkspaceState_can_create_edit_and_save_a_flow()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("flow-authoring");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        scope.State.CreateNewFlow();
        Assert.True(scope.State.HasSelectedFlow);
        var initialFixtureCount = scope.State.SelectedFlow!.Fixtures.Count;
        var initialActionCount = scope.State.SelectedFlow.Actions.Count;
        var initialExpectationCount = scope.State.SelectedFlow.Expectations.Count;

        await scope.State.AddFixtureRowAsync();
        await scope.State.AddActionRowAsync();
        await scope.State.AddExpectationRowAsync();

        Assert.Equal(initialFixtureCount + 1, scope.State.SelectedFlow.Fixtures.Count);
        Assert.Equal(initialActionCount + 1, scope.State.SelectedFlow.Actions.Count);
        Assert.Equal(initialExpectationCount + 1, scope.State.SelectedFlow.Expectations.Count);

        await scope.State.RemoveFixtureRowAsync(scope.State.SelectedFlow.Fixtures.Count - 1);
        await scope.State.RemoveActionRowAsync(scope.State.SelectedFlow.Actions.Count - 1);
        await scope.State.RemoveExpectationRowAsync(scope.State.SelectedFlow.Expectations.Count - 1);

        Assert.Equal(initialFixtureCount, scope.State.SelectedFlow.Fixtures.Count);
        Assert.Equal(initialActionCount, scope.State.SelectedFlow.Actions.Count);
        Assert.Equal(initialExpectationCount, scope.State.SelectedFlow.Expectations.Count);

        scope.State.SourceEditorText = """
        version: 1
        id: authored.flow
        name: Authored flow
        when:
          - step: http.get
            with:
              url: https://example.test/author
        then:
          - expect: http.assert-status
            with:
              status: "200"
        """;

        scope.State.ApplySource();
        Assert.Equal("Authored flow", scope.State.SelectedFlow.Name);

        scope.State.RebuildSource();
        Assert.Contains("Authored flow", scope.State.SourceEditorText, StringComparison.Ordinal);

        scope.State.SaveSelectedFlow();
        Assert.True(File.Exists(scope.State.SelectedFlow.FilePath));
        Assert.Contains("Authored flow", File.ReadAllText(scope.State.SelectedFlow.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceState_can_create_save_toggle_and_delete_a_suite()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("suite-authoring");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        scope.State.CreateNewSuite();
        Assert.True(scope.State.HasSelectedSuite);

        scope.State.ToggleSuiteFlow("flow.search", true);
        Assert.Contains("flow.search", scope.State.SelectedSuite!.FlowIds);

        scope.State.SaveSelectedSuite();
        var suitePath = scope.State.SelectedSuite!.FilePath;
        Assert.True(File.Exists(suitePath));
        Assert.Contains("flow.search", File.ReadAllText(suitePath), StringComparison.Ordinal);

        scope.State.ToggleSuiteFlow("flow.search", false);
        Assert.DoesNotContain("flow.search", scope.State.SelectedSuite.FlowIds);

        scope.State.DeleteSelectedSuite();
        Assert.False(File.Exists(suitePath));
        Assert.True(scope.State.HasLoadedProject);
    }

    [Fact]
    public void WorkspaceState_select_run_populates_artifacts_and_preview_modes()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("artifact-preview");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var artifactRoot = CreateDirectory("artifact-root");
        var textPath = Path.Combine(artifactRoot, "logs", "run.txt");
        var imagePath = Path.Combine(artifactRoot, "shots", "result.png");
        var reportPath = Path.Combine(artifactRoot, "reports", "summary.html");
        Directory.CreateDirectory(Path.GetDirectoryName(textPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(textPath, "run-log");
        File.WriteAllBytes(imagePath, [1, 2, 3, 4]);
        File.WriteAllText(reportPath, "<html>report</html>");

        var run = new StoredRunResult
        {
            ArtifactDirectory = artifactRoot,
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-preview",
                    ArtifactRoot = artifactRoot,
                    Profile = "local",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                    DurationMs = 200
                },
                Reports = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["html"] = reportPath
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        SourceFile = Path.Combine(projectRoot, "flows", "search.flow.yaml"),
                        Outcome = RunOutcome.Failed,
                        Steps =
                        [
                            new StepRunResult
                            {
                                Name = "http.get",
                                Outcome = RunOutcome.Failed,
                                Artifacts =
                                [
                                    new EvidenceArtifact
                                    {
                                        Category = "log",
                                        RelativePath = Path.Combine("logs", "run.txt"),
                                        Description = "Execution log"
                                    },
                                    new EvidenceArtifact
                                    {
                                        Category = "screenshot",
                                        RelativePath = Path.Combine("shots", "result.png"),
                                        Description = "Failure shot"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        scope.State.Runs.Insert(0, run);
        scope.State.SelectRun(run);

        Assert.Equal("run-preview", scope.State.SelectionHeadline);
        Assert.NotEmpty(scope.State.SelectedRunArtifacts);
        Assert.Equal("run-log", scope.State.PreviewText);

        var imageArtifact = scope.State.SelectedRunArtifacts.Single(item => item.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        scope.State.SelectArtifact(imageArtifact);
        Assert.StartsWith("data:image/png;base64,", scope.State.PreviewImageDataUrl, StringComparison.Ordinal);

        scope.State.SelectRunFlow(run.Result.Flows[0]);
        Assert.Single(scope.State.SelectedRunSteps);
        scope.State.SelectRunStep(run.Result.Flows[0].Steps[0]);
        Assert.Equal(3, scope.State.SelectedRunArtifacts.Count);
    }

    [Fact]
    public async Task WorkspaceState_dispatches_run_and_rerun_requests_through_runner_service()
    {
        var runner = new FakeRunnerService();
        using var scope = CreateState(runner);
        var projectRoot = CreateProject("runner-state");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        await scope.State.RunSelectedAsync();
        Assert.Single(runner.Requests);
        Assert.EndsWith("search.flow.yaml", runner.Requests[0].Options.FlowPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("passed", scope.State.LiveRunHeadline, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(scope.State.LiveTimelineEntries);
        Assert.Contains(scope.State.LiveTimelineEntries, entry => entry.Category == "Log" && entry.Headline.Contains("Waiting 5s before click.", StringComparison.Ordinal));
        Assert.Contains(scope.State.LiveTimelineEntries, entry => string.Equals(entry.Category, "Step", StringComparison.Ordinal) && (entry.Detail?.Contains("Clicked!", StringComparison.Ordinal) ?? false));

        var failedRun = new StoredRunResult
        {
            ArtifactDirectory = CreateDirectory("failed-run"),
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-failed",
                    ArtifactRoot = CreateDirectory("failed-run-artifacts"),
                    Profile = "local",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                    DurationMs = 50
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        SourceFile = Path.Combine(projectRoot, "flows", "search.flow.yaml"),
                        Outcome = RunOutcome.Failed,
                        Steps =
                        [
                            new StepRunResult
                            {
                                Name = "http.get",
                                Outcome = RunOutcome.Failed
                            }
                        ]
                    }
                ]
            }
        };

        scope.State.Runs.Insert(0, failedRun);
        scope.State.SelectRun(failedRun);

        await scope.State.RerunFailedAsync();
        Assert.Equal(2, runner.Requests.Count);
        Assert.Single(runner.Requests[1].Options.FlowPaths);
        Assert.Equal("rerun-failed", runner.Requests[1].Options.Trigger);

        scope.State.SelectRunFlow(failedRun.Result.Flows[0]);
        scope.State.SelectRunStep(failedRun.Result.Flows[0].Steps[0]);
        await scope.State.RerunFromStepAsync();

        Assert.Equal(3, runner.Requests.Count);
        Assert.Equal("rerun-from-step", runner.Requests[2].Options.Trigger);
        Assert.Equal("http.get", runner.Requests[2].Options.StartFromStep);
        Assert.Equal("Search flow", scope.State.LiveCurrentFlow);
        Assert.Equal("—", scope.State.LiveCurrentStep);
        Assert.NotEmpty(scope.State.LiveEvents);
    }

    [Fact]
    public async Task WorkspaceState_blocks_browser_runs_when_local_base_url_is_unreachable()
    {
        var runner = new FakeRunnerService();
        using var scope = CreateState(runner);
        var projectRoot = CreateBrowserProject("browser-run-blocked", "http://127.0.0.1:1");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        await scope.State.RunSelectedAsync();

        Assert.Empty(runner.Requests);
        Assert.Equal("Blocked", scope.State.LiveRunStatus);
        Assert.Contains("127.0.0.1:1", scope.State.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(scope.State.LiveTimelineEntries, entry => entry.Category == "Validation");
    }

    [Fact]
    public async Task WorkspaceState_can_cancel_an_active_run_and_track_live_progress()
    {
        var runner = new FakeRunnerService
        {
            WaitForCancellation = true
        };
        using var scope = CreateState(runner);
        var projectRoot = CreateProject("runner-cancel");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var runTask = scope.State.RunSelectedAsync();
        await Task.Delay(150);

        Assert.True(scope.State.IsBusy);
        Assert.Equal("Search flow", scope.State.LiveCurrentFlow);
        Assert.Equal("http.get", scope.State.LiveCurrentStep);
        Assert.True(scope.State.LiveCurrentFlowStepCount >= 2);

        scope.State.ToggleLiveLogVisibility();
        Assert.True(scope.State.IsLiveLogVisible);

        scope.State.StopActiveRun();
        await runTask;

        Assert.False(scope.State.IsBusy);
        Assert.Equal("Canceled", scope.State.LiveRunStatus);
        Assert.Contains(scope.State.LiveEvents, entry => entry.Contains("Run cancellation requested.", StringComparison.Ordinal));
        Assert.Contains(scope.State.LiveTimelineEntries, entry => entry.Headline.Contains("Run cancellation requested.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RerunFailedAsync_reports_when_selected_run_has_no_failed_flows()
    {
        var runner = new FakeRunnerService();
        using var scope = CreateState(runner);
        var projectRoot = CreateProject("rerun-passed");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var passedRun = new StoredRunResult
        {
            ArtifactDirectory = CreateDirectory("passed-run"),
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-passed",
                    ArtifactRoot = CreateDirectory("passed-run-artifacts"),
                    Profile = "local",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                    DurationMs = 25
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        SourceFile = Path.Combine(projectRoot, "flows", "search.flow.yaml"),
                        Outcome = RunOutcome.Passed
                    }
                ]
            }
        };

        scope.State.Runs.Insert(0, passedRun);
        scope.State.SelectRun(passedRun);

        await scope.State.RerunFailedAsync();

        Assert.Empty(runner.Requests);
        Assert.Contains("no failed flows", scope.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunSelectedSuiteAsync_dispatches_suite_options_when_flow_ids_are_empty()
    {
        var runner = new FakeRunnerService();
        using var scope = CreateState(runner);
        var projectRoot = CreateProject("suite-runner");
        WriteFile(projectRoot, Path.Combine(".cress", "profiles", "ci.yaml"), """
        baseUrl: https://ci.example.test
        evidence:
          screenshotPolicy: off
        """);
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        scope.State.CreateNewSuite();

        scope.State.SelectedSuite!.Name = "Smoke suite";
        scope.State.SelectedSuite.Profile = "ci";
        scope.State.SelectedSuite.ReportFormatsText = "html, markdown";

        await scope.State.RunSelectedSuiteAsync();

        var request = Assert.Single(runner.Requests);
        Assert.Null(request.Options.FlowPath);
        Assert.Empty(request.Options.FlowPaths);
        Assert.Equal("ci", request.Options.Profile);
        Assert.Equal(["html", "markdown"], request.Options.ReportFormats);
        Assert.Contains("Smoke suite completed", scope.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunSelectedSuiteAsync_blocks_unresolved_flow_selection_before_dispatch()
    {
        var runner = new FakeRunnerService();
        using var scope = CreateState(runner);
        var projectRoot = CreateProject("suite-missing-flow");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        scope.State.CreateNewSuite();

        scope.State.SelectedSuite!.FlowIds.Add("flow.missing");

        await scope.State.RunSelectedSuiteAsync();

        Assert.Empty(runner.Requests);
        Assert.Contains(scope.State.Diagnostics, diagnostic => diagnostic.Code == "STE003");
        Assert.Equal("Suite selection did not resolve to runnable flows.", scope.State.StatusMessage);
    }

    [Fact]
    public async Task RunSelectedSuiteAsync_preserves_canceled_suite_status()
    {
        var runner = new FakeRunnerService
        {
            WaitForCancellation = true
        };
        using var scope = CreateState(runner);
        var projectRoot = CreateProject("suite-cancel");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();
        scope.State.CreateNewSuite();
        scope.State.SelectedSuite!.Name = "Cancelable suite";
        scope.State.SelectedSuite.FlowIds.Add("flow.search");

        var runTask = scope.State.RunSelectedSuiteAsync();
        await Task.Delay(150);
        scope.State.StopActiveRun();
        await runTask;

        Assert.Equal("Canceled", scope.State.LiveRunStatus);
        Assert.Equal("Suite Cancelable suite canceled.", scope.State.StatusMessage);
        Assert.Equal("Suite Cancelable suite canceled.", scope.State.LiveRunHeadline);
    }

    [Fact]
    public void SelectArtifact_with_unknown_extension_uses_fallback_preview_text()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("artifact-fallback");
        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        var artifactRoot = CreateDirectory("artifact-fallback-root");
        var binaryPath = Path.Combine(artifactRoot, "payload.bin");
        File.WriteAllBytes(binaryPath, [5, 4, 3, 2, 1]);

        var run = new StoredRunResult
        {
            ArtifactDirectory = artifactRoot,
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-binary",
                    ArtifactRoot = artifactRoot,
                    Profile = "local",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                    DurationMs = 10
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        SourceFile = Path.Combine(projectRoot, "flows", "search.flow.yaml"),
                        Outcome = RunOutcome.Passed,
                        Steps =
                        [
                            new StepRunResult
                            {
                                Name = "http.get",
                                Outcome = RunOutcome.Passed,
                                Artifacts =
                                [
                                    new EvidenceArtifact
                                    {
                                        Category = "binary",
                                        RelativePath = "payload.bin",
                                        Description = "Binary payload"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        scope.State.SelectRun(run);

        Assert.Contains(".bin files", scope.State.PreviewText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(binaryPath, scope.State.PreviewText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, scope.State.PreviewImageDataUrl);
    }

    private StateScope CreateState(FakeRunnerService? runner = null)
    {
        var services = new ServiceCollection();
        services.AddCressStudioBackend();
        services.AddSingleton<IStudioRecorderService, FakeStudioRecorderService>();
        if (runner is not null)
        {
            services.AddSingleton<IStudioRunnerService>(runner);
        }
        services.AddSingleton<StudioWorkspaceState>();
        var provider = services.BuildServiceProvider();
        return new StateScope(provider, provider.GetRequiredService<StudioWorkspaceState>());
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "cress-studio-run-tests", Guid.NewGuid().ToString("N"), name);
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
          name: Studio run sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        defaults:
          retries: 0
        """);
        WriteFile(root, Path.Combine(".cress", "profiles", "local.yaml"), """
        baseUrl: https://example.test
        evidence:
          screenshotPolicy: on-failure
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
          - name: http.assert-status
            implementation:
              plugin: builtin.http
              operation: assert-status
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

    private string CreateBrowserProject(string name, string baseUrl)
    {
        var root = CreateDirectory(name);
        WriteFile(root, Path.Combine(".cress", "config.yaml"), """
        version: 1
        project:
          name: Studio browser sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        drivers:
          playwright:
            enabled: true
        """);
        WriteFile(root, Path.Combine(".cress", "profiles", "local.yaml"), $$"""
        baseUrl: {{baseUrl}}
        playwright:
          headless: true
        """);
        WriteFile(root, Path.Combine("capabilities", "web.md"), """
        ---
        version: 1
        id: capability.web
        owner: qa
        risk: medium
        ---

        # Web capability
        """);
        WriteFile(root, Path.Combine("flows", "browser.flow.yaml"), """
        version: 1
        id: flow.browser
        name: Browser flow
        capability: capability.web
        when:
          - step: browser.navigate
            with:
              url: /login
        then:
          - expect: ui.assert-text
            with:
              testId: heading
              text: Login
        """);
        WriteFile(root, Path.Combine("steps", "web.yaml"), """
        version: 1
        steps:
          - name: browser.navigate
            implementation:
              plugin: builtin.playwright
              operation: navigate
          - name: ui.assert-text
            implementation:
              plugin: builtin.playwright
              operation: assert-text
        """);
        return root;
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
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

    private sealed class FakeRunnerService : IStudioRunnerService
    {
        public event Action? Changed;

        public List<StudioRunnerDispatchRequest> Requests { get; } = [];
        public bool WaitForCancellation { get; set; }

        private readonly IReadOnlyList<StudioRunnerNodeSnapshot> _nodes =
        [
            new(
                Id: StudioEmbeddedRunnerNode.LocalNodeId,
                Name: "Local embedded node",
                DisplayName: "Test runner",
                Description: "Fake runner for tests",
                Transport: StudioRunnerTransportKind.Embedded,
                Location: "This machine",
                Capabilities: ["http", "playwright"],
                Status: StudioRunnerNodeStatus.Healthy,
                LastHeartbeatUtc: DateTimeOffset.UtcNow,
                LastCompletedUtc: null,
                ActiveDispatchId: null,
                ActiveRunId: null,
                LastRunId: null,
                QueueDepth: 0,
                LastError: null)
        ];

        public IReadOnlyList<StudioRunnerNodeSnapshot> ListNodes()
            => _nodes;

        public async Task<StudioRunnerDispatchResult> DispatchAsync(
            StudioRunnerDispatchRequest request,
            IProgress<RuntimeProgressUpdate>? progress,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.RunStarted,
                RunId = "run-test",
                FlowCount = 1,
                Message = "Started"
            });
            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.FlowStarted,
                RunId = "run-test",
                FlowId = "flow.search",
                FlowName = "Search flow",
                FlowIndex = 1,
                FlowCount = 1,
                StepCount = 2,
                Message = "Starting Search flow."
            });
            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.StepStarted,
                RunId = "run-test",
                FlowId = "flow.search",
                FlowName = "Search flow",
                FlowIndex = 1,
                FlowCount = 1,
                StepIndex = 1,
                StepCount = 2,
                Step = new StepRunResult
                {
                    Kind = "action",
                    Name = "http.get",
                    StartedAt = DateTimeOffset.UtcNow
                },
                Message = "action: http.get attempt 1"
            });
            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.Log,
                RunId = "run-test",
                FlowId = "flow.search",
                FlowName = "Search flow",
                FlowIndex = 1,
                FlowCount = 1,
                LogLevel = "INFO",
                Message = "Waiting 5s before click."
            });

            if (WaitForCancellation)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }

            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.StepCompleted,
                RunId = "run-test",
                FlowId = "flow.search",
                FlowName = "Search flow",
                FlowIndex = 1,
                FlowCount = 1,
                StepIndex = 1,
                StepCount = 2,
                Step = new StepRunResult
                {
                    Kind = "action",
                    Name = "http.get",
                    Outcome = RunOutcome.Passed,
                    Message = "Clicked!"
                },
                Message = "action: http.get attempt 1 -> Passed"
            });
            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.FlowCompleted,
                RunId = "run-test",
                FlowId = "flow.search",
                FlowName = "Search flow",
                FlowIndex = 1,
                FlowCount = 1,
                Flow = new FlowRunResult
                {
                    FlowId = "flow.search",
                    Name = "Search flow",
                    Outcome = RunOutcome.Passed
                },
                Message = "Search flow finished with Passed."
            });
            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.RunCompleted,
                RunId = "run-test",
                Message = "Completed"
            });

            Changed?.Invoke();

            var result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-test",
                    ArtifactRoot = request.ProjectRoot,
                    Profile = request.Options.Profile ?? "local",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                    DurationMs = 100
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        SourceFile = request.Options.FlowPath ?? request.Options.FlowPaths.FirstOrDefault(),
                        Outcome = RunOutcome.Passed
                    }
                ]
            };

            return new StudioRunnerDispatchResult(
                request.NodeId,
                request.DispatchId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                result);
        }
    }
}
