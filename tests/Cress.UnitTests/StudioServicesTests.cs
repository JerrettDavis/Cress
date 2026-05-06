using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Studio.Services;
using Cress.Validation;

namespace Cress.UnitTests;

public sealed class StudioServicesTests
{
    [Fact]
    public void ProjectCatalogService_LoadsCapabilitiesAndProfiles()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "capabilities", "orders.md"), """
        ---
        version: 1
        id: orders
        owner: QE
        risk: high
        tags:
          - checkout
        ---

        # Capability: Order placement

        ## Rules

        - Orders must validate inventory.

        ## Acceptance Criteria

        ### ORD-1

        User can place an order.
        """);
        workspace.WriteFile(Path.Combine("project", "flows", "order.flow.yaml"), """
        version: 1
        id: order-flow
        name: Order flow
        capability: orders
        when:
          - step: order.start
        then:
          - expect: order.completed
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "order.yaml"), """
        version: 1
        steps:
          - name: order.start
            retrySafe: true
            implementation:
              plugin: sample
              operation: Execute
          - name: order.completed
            retrySafe: true
            implementation:
              plugin: sample
              operation: Execute
        """);
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "ci.yaml"), """
        profile: ci
        variables:
          environment: ci
        """);

        var result = CreateCatalogService().Load(workspace.GetPath("project"));

        Assert.NotNull(result.Value);
        Assert.Single(result.Value!.Capabilities);
        Assert.Equal(2, result.Value.Profiles.Count);
    }

    [Fact]
    public void FlowDocumentService_RoundTripsDesignerDocument()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        var service = new FlowDocumentService(new FlowParser());
        var document = service.CreateNew(workspace.GetPath("project"), "flows");
        var edited = new FlowEditorDocument
        {
            FilePath = document.FilePath,
            Id = "checkout-flow",
            Name = "Checkout flow",
            CapabilityId = "orders",
            Summary = "Validates checkout.",
            Status = "ready",
            TagsText = "checkout, smoke",
            Fixtures =
            [
                new EditableFixture
                {
                    Alias = "customer",
                    Use = "persona.customer"
                }
            ],
            Actions =
            [
                new EditableExecutable
                {
                    Name = "order.start",
                    InputsText = "path=/checkout"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "order.completed",
                    InputsText = "status=200"
                }
            ]
        };

        var save = service.Save(edited);
        var load = service.Load(edited.FilePath);

        Assert.NotNull(save.Value);
        Assert.NotNull(load.Value);
        Assert.Equal("checkout-flow", load.Value!.Id);
        Assert.Equal("orders", load.Value.CapabilityId);
        Assert.Single(load.Value.Actions);
        Assert.Contains("checkout, smoke", load.Value.TagsText);
    }

    [Fact]
    public void RunResultRepository_LoadsStoredRuns()
    {
        using var workspace = new TestWorkspace();
        var repository = new RunResultRepository();
        var artifactsRoot = workspace.GetPath("project", "artifacts", "runs");
        Directory.CreateDirectory(Path.Combine(artifactsRoot, "run-20240101000000001"));
        Directory.CreateDirectory(Path.Combine(artifactsRoot, "run-20240101000000002"));
        File.WriteAllText(Path.Combine(artifactsRoot, "run-20240101000000001", "result.json"),
            System.Text.Json.JsonSerializer.Serialize(new RunResult
            {
                Metadata = new RunMetadata { RunId = "run-20240101000000001", ArtifactRoot = Path.Combine(artifactsRoot, "run-20240101000000001") }
            }));
        File.WriteAllText(Path.Combine(artifactsRoot, "run-20240101000000002", "result.json"),
            System.Text.Json.JsonSerializer.Serialize(new RunResult
            {
                Metadata = new RunMetadata { RunId = "run-20240101000000002", ArtifactRoot = Path.Combine(artifactsRoot, "run-20240101000000002") }
            }));

        var runs = repository.ListRuns(workspace.GetPath("project"), Path.Combine("artifacts", "runs"));

        Assert.Equal(2, runs.Count);
        Assert.Equal("run-20240101000000002", runs[0].Result.Metadata.RunId);
    }

    [Fact]
    public void StudioSuiteService_RoundTripsSuiteDefinitions()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "flows", "order.flow.yaml"), """
        version: 1
        id: order-flow
        name: Order flow
        capability: orders
        tags:
          - smoke
        when:
          - step: order.start
        then:
          - expect: order.completed
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "order.yaml"), """
        version: 1
        steps:
          - name: order.start
            retrySafe: true
            implementation:
              plugin: sample
              operation: Execute
          - name: order.completed
            retrySafe: true
            implementation:
              plugin: sample
              operation: Execute
        """);

        var suiteService = new StudioSuiteService();
        var suite = suiteService.CreateNew(workspace.GetPath("project")) with
        {
            Id = "smoke-suite",
            Name = "Smoke suite",
            Tag = "smoke",
            FlowIds = ["order-flow"],
            ReportFormats = ["html", "json"]
        };

        var save = suiteService.Save(suite);
        var loadAll = suiteService.LoadAll(workspace.GetPath("project"));
        var catalog = CreateCatalogService().Load(workspace.GetPath("project")).Value!;
        var resolved = suiteService.ResolveFlows(catalog, loadAll.Value!.Single());

        Assert.NotNull(save.Value);
        Assert.Single(loadAll.Value!);
        Assert.Single(resolved);
        Assert.Equal("order-flow", resolved[0].FlowId);
    }

    [Fact]
    public async Task RuntimeOrchestrator_ReportsProgress()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "flows", "http.flow.yaml"), """
        version: 1
        id: http-flow
        name: HTTP flow
        when:
          - step: api.request
            with:
              method: GET
              path: /health
        then:
          - expect: api.status_ok
            with:
              status: "200"
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "http.yaml"), """
        version: 1
        steps:
          - name: api.request
            drivers:
              - http
            retrySafe: true
            implementation:
              plugin: builtin.http
              operation: request
          - name: api.status_ok
            drivers:
              - http
            retrySafe: true
            implementation:
              plugin: builtin.http
              operation: assert-status
        """);

        var orchestrator = new RuntimeOrchestrator(
            CreateCatalogService(),
            new PlanGenerator(),
            new ConfigLoader(new ProjectLocator()),
            new PluginHost(),
            new ReportGenerator(),
            [new Cress.Execution.Drivers.HttpRuntimeDriver(() => new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)))]);
        var updates = new List<RuntimeProgressUpdate>();
        var progress = new CollectingProgress(updates);

        await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions(), progress);

        Assert.Contains(updates, update => update.Kind == RuntimeProgressKind.RunStarted);
        Assert.Contains(updates, update => update.Kind == RuntimeProgressKind.StepCompleted && update.Step?.Name == "api.request");
        Assert.Contains(updates, update => update.Kind == RuntimeProgressKind.RunCompleted);
    }

    [Fact]
    public void StudioAuthoringService_Finds_diagnostics_and_applies_quick_actions()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "authoring.yaml"), """
        version: 1
        steps:
          - name: app.open
            retrySafe: true
            implementation:
              plugin: sample
              operation: Execute
          - name: app.is_visible
            retrySafe: true
            implementation:
              plugin: sample
              operation: Execute
        """);

        var catalog = CreateCatalogService().Load(workspace.GetPath("project")).Value!;
        var service = new StudioAuthoringService();
        var document = new FlowEditorDocument
        {
            FilePath = workspace.GetPath("project", "flows", "new.flow.yaml"),
            Id = "Needs Fixing",
            Name = string.Empty,
            Actions =
            [
                new EditableExecutable
                {
                    Name = "missing.step",
                    InputsText = "bad-line"
                }
            ]
        };

        var analysis = service.Analyze(catalog, document);
        var updated = service.ApplyQuickAction(catalog, document, "template.desktop-smoke");

        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("kebab-case", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.QuickActions, action => action.Id == "template.desktop-smoke");
        Assert.Contains(updated.Actions, action => action.Name == "app.open");
        Assert.Contains(updated.Expectations, action => action.Name == "app.is_visible");
    }

    [Fact]
    public async Task RuntimeOrchestrator_Retries_retry_safe_steps_and_supports_start_from_step()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", ".cress", "config.yaml"), """
        version: 1
        project:
          name: Studio Project
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: artifacts/runs
          reports: reports
        defaults:
          timeout: 30000
          retries: 0
          evidence: standard
          cleanup: on-success
        plugins:
          discover:
            - plugins
            - steps
        drivers:
          http:
            enabled: true
          fake:
            enabled: true
        """);
        workspace.WriteFile(Path.Combine("project", "flows", "retry.flow.yaml"), """
        version: 1
        id: retry-flow
        name: Retry flow
        when:
          - step: fake.first
          - step: fake.second
        then:
          - expect: fake.expect
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "fake.yaml"), """
        version: 1
        steps:
          - name: fake.first
            drivers:
              - fake
            retrySafe: true
            implementation:
              operation: execute
          - name: fake.second
            drivers:
              - fake
            retrySafe: true
            implementation:
              operation: execute
          - name: fake.expect
            drivers:
              - fake
            retrySafe: true
            implementation:
              operation: execute
        """);

        var driver = new FakeRetryDriver();
        var orchestrator = new RuntimeOrchestrator(
            CreateCatalogService(),
            new PlanGenerator(),
            new ConfigLoader(new ProjectLocator()),
            new PluginHost(),
            new ReportGenerator(),
            [driver]);

        var retried = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions
        {
            RetryCountOverride = 1,
            ScreenshotPolicyOverride = "off"
        });
        var rerunFromStep = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions
        {
            FlowPath = workspace.GetPath("project", "flows", "retry.flow.yaml"),
            StartFromStep = "fake.second",
            ScreenshotPolicyOverride = "off"
        });

        Assert.True(retried.Passed);
        Assert.True(retried.Flows[0].PassedWithRetry);
        Assert.Equal(2, retried.Flows[0].Steps.Count(step => step.Name == "fake.first"));
        Assert.Contains(rerunFromStep.Flows[0].Steps, step => step.Name == "fake.first" && step.Outcome == RunOutcome.Skipped);
        Assert.DoesNotContain(rerunFromStep.Flows[0].Steps, step => step.Name == "fake.first" && step.Attempt > 1);
    }

    [Fact]
    public void StudioRunInsightsService_Computes_flake_history()
    {
        var service = new StudioRunInsightsService();
        var runs = new[]
        {
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "run-2", StartedAt = DateTimeOffset.UtcNow },
                    Flows =
                    [
                        new FlowRunResult { FlowId = "checkout", Name = "Checkout", Outcome = RunOutcome.Passed, PassedWithRetry = true }
                    ]
                }
            },
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "run-1", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
                    Flows =
                    [
                        new FlowRunResult { FlowId = "checkout", Name = "Checkout", Outcome = RunOutcome.Failed, FailureMessage = "boom" }
                    ]
                }
            }
        };

        var insights = service.Analyze(new ProjectCatalog(), runs);

        Assert.Single(insights.FlakyFlows);
        Assert.Equal("checkout", insights.FlakyFlows[0].FlowId);
        Assert.Contains("regression", service.Compare(runs[0], runs[1]).Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteProjectFiles(TestWorkspace workspace)
    {
        workspace.WriteFile(Path.Combine("project", ".cress", "config.yaml"), """
        version: 1
        project:
          name: Studio Project
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: artifacts/runs
          reports: reports
        defaults:
          timeout: 30000
          retries: 0
          evidence: standard
          cleanup: on-success
        plugins:
          discover:
            - plugins
            - steps
        drivers:
          http:
            enabled: true
        """);
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "local.yaml"), """
        profile: local
        baseUrl: http://localhost:5000
        variables:
          environment: local
        """);
        Directory.CreateDirectory(workspace.GetPath("project", "capabilities"));
        Directory.CreateDirectory(workspace.GetPath("project", "flows"));
        Directory.CreateDirectory(workspace.GetPath("project", "steps", "manifests"));
        Directory.CreateDirectory(workspace.GetPath("project", "fixtures"));
    }

    private static ProjectCatalogService CreateCatalogService()
    {
        var locator = new ProjectLocator();
        return new ProjectCatalogService(
            locator,
            new ConfigLoader(locator),
            new ProfileLoader(),
            new FlowParser(),
            new FlowNormalizer(),
            new CapabilityParser(),
            new StepManifestParser(),
            new FixtureManifestParser(),
            new StepRegistry());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class CollectingProgress : IProgress<RuntimeProgressUpdate>
    {
        private readonly IList<RuntimeProgressUpdate> _updates;

        public CollectingProgress(IList<RuntimeProgressUpdate> updates)
        {
            _updates = updates;
        }

        public void Report(RuntimeProgressUpdate value) => _updates.Add(value);
    }

    private sealed class FakeRetryDriver : IRuntimeDriver
    {
        public string Name => "fake";

        public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog) => [];

        public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
            => Task.FromResult<IDriverSession>(new FakeRetrySession());

        private sealed class FakeRetrySession : IDriverSession
        {
            private readonly Dictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);

            public string Name => "fake";

            public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
            {
                _attempts.TryGetValue(action.Name, out var attempt);
                _attempts[action.Name] = ++attempt;
                if (action.Name == "fake.first" && attempt == 1)
                {
                    return Task.FromResult(new DriverExecutionResult
                    {
                        Outcome = RunOutcome.Failed,
                        Message = "first attempt failed",
                        FailureClassification = "fake"
                    });
                }

                return Task.FromResult(new DriverExecutionResult
                {
                    Outcome = RunOutcome.Passed,
                    Message = $"{action.Name} passed"
                });
            }

            public Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<EvidenceArtifact>>([]);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
