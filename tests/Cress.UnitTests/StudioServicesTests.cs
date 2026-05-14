using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Recorder;
using Cress.Specs;
using Cress.Studio.Services;
using Cress.Validation;
using System.Reflection;
using System.Threading;

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
    public void FlowDocumentService_CreateNew_increments_file_name_when_default_exists()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        var service = new FlowDocumentService(new FlowParser());
        workspace.WriteFile(Path.Combine("project", "flows", "new-flow.flow.yaml"), """
        version: 1
        id: existing
        name: Existing flow
        when:
          - step: order.start
        then:
          - expect: order.completed
        """);

        var document = service.CreateNew(workspace.GetPath("project"), "flows");

        Assert.EndsWith("new-flow-1.flow.yaml", document.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("new-flow-1", document.Id);
        Assert.Contains("app.open", document.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void FlowDocumentService_LoadFromSource_round_trips_metadata_fixtures_and_input_formats()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        var service = new FlowDocumentService(new FlowParser());

        var loaded = service.LoadFromSource("""
        version: 1
        id: checkout-flow
        name: Checkout flow
        capability: orders
        summary: Validates checkout
        status: ready
        tags:
          - checkout
          - smoke
        fixtures:
          customer:
            use: persona.customer
            source: fixtures/customer.json
            for: order.start
        when:
          - step: order.start
            with:
              path: /checkout
              method: GET
        then:
          - expect: order.completed
            with:
              status: "200"
              body=ok: ignored
        """, Path.Combine(workspace.GetPath("project"), "flows", "checkout.flow.yaml"));

        Assert.NotNull(loaded.Value);
        Assert.Equal("orders", loaded.Value!.CapabilityId);
        Assert.Equal("ready", loaded.Value.Status);
        Assert.Contains("checkout, smoke", loaded.Value.TagsText, StringComparison.Ordinal);
        Assert.Single(loaded.Value.Fixtures);
        Assert.Equal("customer", loaded.Value.Fixtures[0].Alias);
        Assert.Equal("persona.customer", loaded.Value.Fixtures[0].Use);
        Assert.Equal("fixtures/customer.json", loaded.Value.Fixtures[0].Source);
        Assert.Equal("order.start", loaded.Value.Fixtures[0].For);
        Assert.Contains("path=/checkout", loaded.Value.Actions[0].InputsText, StringComparison.Ordinal);
        Assert.Contains("method=GET", loaded.Value.Actions[0].InputsText, StringComparison.Ordinal);
        Assert.Contains("status=200", loaded.Value.Expectations[0].InputsText, StringComparison.Ordinal);
        Assert.Contains("body=ok=ignored", loaded.Value.Expectations[0].InputsText, StringComparison.Ordinal);
    }

    [Fact]
    public void FlowDocumentService_Load_preserves_original_source_text_from_file()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        var service = new FlowDocumentService(new FlowParser());
        var contents = """
        version: 1
        id: commented-flow
        name: Commented flow
        # this comment should remain in SourceText
        when:
          - step: order.start
        then:
          - expect: order.completed
        """;
        workspace.WriteFile(Path.Combine("project", "flows", "commented.flow.yaml"), contents);

        var loaded = service.Load(workspace.GetPath("project", "flows", "commented.flow.yaml"));

        Assert.NotNull(loaded.Value);
        static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n");

        Assert.Equal(NormalizeLineEndings(contents), NormalizeLineEndings(loaded.Value!.SourceText));
    }

    [Fact]
    public void FlowDocumentService_LoadFromSource_returns_diagnostics_for_invalid_yaml()
    {
        var service = new FlowDocumentService(new FlowParser());

        var loaded = service.LoadFromSource("""
        version: 1
        id: broken-flow
        when:
          - step: order.start
            with:
              url: [
        """);

        Assert.Null(loaded.Value);
        Assert.NotEmpty(loaded.Diagnostics);
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
    public void StudioSuiteService_Load_returns_missing_file_diagnostic()
    {
        var service = new StudioSuiteService();

        var result = service.Load(@"C:\missing\smoke.suite.yaml");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STE001", diagnostic.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void StudioSuiteService_Save_requires_file_path()
    {
        var service = new StudioSuiteService();

        var result = service.Save(new StudioSuiteDocument
        {
            Id = "smoke-suite",
            Name = "Smoke suite"
        });

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STE006", diagnostic.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void StudioSuiteService_CreateNew_increments_filename_when_default_exists()
    {
        using var workspace = new TestWorkspace();
        var suitesRoot = StudioSuiteService.GetSuitesRoot(workspace.GetPath("project"));
        Directory.CreateDirectory(suitesRoot);
        File.WriteAllText(Path.Combine(suitesRoot, "new-suite.suite.yaml"), "version: 1");

        var service = new StudioSuiteService();

        var document = service.CreateNew(workspace.GetPath("project"));

        Assert.EndsWith("new-suite-1.suite.yaml", document.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("new-suite-1", document.Id);
    }

    [Fact]
    public void StudioSuiteService_LoadAll_collects_validation_diagnostics_and_keeps_valid_suites()
    {
        using var workspace = new TestWorkspace();
        var suitesRoot = StudioSuiteService.GetSuitesRoot(workspace.GetPath("project"));
        Directory.CreateDirectory(suitesRoot);
        File.WriteAllText(Path.Combine(suitesRoot, "broken.suite.yaml"), ":");
        File.WriteAllText(Path.Combine(suitesRoot, "valid.suite.yaml"), """
        version: 1
        id: smoke-suite
        name: Smoke suite
        """);

        var service = new StudioSuiteService();

        var result = service.LoadAll(workspace.GetPath("project"));

        var suite = Assert.Single(result.Value!);
        Assert.Equal("smoke-suite", suite.Id);
        Assert.Collection(
            result.Diagnostics.OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal),
            diagnostic => Assert.Equal("STE004", diagnostic.Code),
            diagnostic => Assert.Equal("STE005", diagnostic.Code));
    }

    [Fact]
    public void StudioSuiteService_ResolveFlows_reports_when_selection_matches_nothing()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "flows", "order.flow.yaml"), """
        version: 1
        id: order-flow
        name: Order flow
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
        var diagnostics = new List<Diagnostic>();
        var service = new StudioSuiteService();
        var catalog = CreateCatalogService().Load(workspace.GetPath("project")).Value!;

        var flows = service.ResolveFlows(catalog, new StudioSuiteDocument
        {
            FilePath = workspace.GetPath("project", ".cress", "suites", "missing.suite.yaml"),
            FlowIds = ["missing-flow"]
        }, diagnostics);

        Assert.Empty(flows);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STE003", diagnostic.Code);
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
    public void StudioAuthoringService_reports_ready_state_and_supports_additional_quick_actions()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "fixtures", "shared.yaml"), """
        version: 1
        fixtures:
          shared.fixture:
            type: seed.customer
            strategy: static
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "authoring-extra.yaml"), """
        version: 1
        steps:
          - name: api.request
            retrySafe: true
            inputs:
              method:
                required: true
                description: HTTP method
              path:
                required: true
                description: Request path
            implementation:
              plugin: sample
              operation: Execute
          - name: api.status_ok
            retrySafe: true
            inputs:
              status:
                required: true
                description: Expected status
            implementation:
              plugin: sample
              operation: Execute
          - name: browser.screenshot
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
            Id = "api-health",
            Name = "API health",
            Actions =
            [
                new EditableExecutable
                {
                    Name = "api.request",
                    InputsText = "method=GET\npath=/health"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "api.status_ok",
                    InputsText = "status=200"
                }
            ]
        };

        var analysis = service.Analyze(catalog, document);
        var smoke = service.ApplyQuickAction(catalog, document, "metadata.smoke");
        var fixture = service.ApplyQuickAction(catalog, document, "fixture.session");
        var apiTemplate = service.ApplyQuickAction(catalog, document, "template.api-health");
        var screenshot = service.ApplyQuickAction(catalog, document, "action.screenshot");
        var visible = service.ApplyQuickAction(catalog, document, "expect.visible");

        Assert.Equal("Designer is ready to save.", analysis.Summary);
        Assert.Empty(analysis.Diagnostics);
        Assert.Contains(analysis.QuickActions, action => action.Id == "fixture.session");
        Assert.Contains(analysis.QuickActions, action => action.Id == "template.api-health");
        Assert.Contains(analysis.QuickActions, action => action.Id == "action.screenshot");
        Assert.Contains(analysis.QuickActions, action => action.Id == "expect.visible");

        Assert.Contains("smoke", smoke.TagsText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("draft", smoke.Status);
        Assert.Equal("Created from a studio quick action.", smoke.Summary);

        var addedFixture = Assert.Single(fixture.Fixtures);
        Assert.Equal("session", addedFixture.Alias);
        Assert.Equal("shared.fixture", addedFixture.Use);

        Assert.Equal("api.request", apiTemplate.Actions[^1].Name);
        Assert.Contains("path=/health", apiTemplate.Actions[^1].InputsText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("api.status_ok", apiTemplate.Expectations[^1].Name);
        Assert.Contains("studio", apiTemplate.TagsText, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("browser.screenshot", screenshot.Actions[^1].Name);
        Assert.Equal("app.is_visible", visible.Expectations[^1].Name);
    }

    [Fact]
    public void StudioAuthoringService_surfaces_composite_diagnostics_for_catalog_mismatches()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "authoring-composite.yaml"), """
        version: 1
        steps:
          - name: browser.goto
            retrySafe: true
            inputs:
              path:
                required: true
                description: Route to open
            implementation:
              plugin: sample
              operation: Execute
          - name: api.request
            retrySafe: true
            inputs:
              method:
                required: true
                description: HTTP method
              path:
                required: true
                description: Request path
            implementation:
              plugin: sample
              operation: Execute
        """);

        var catalog = CreateCatalogService().Load(workspace.GetPath("project")).Value!;
        var service = new StudioAuthoringService();
        var document = new FlowEditorDocument
        {
            Id = "authoring-check",
            Name = "Authoring check",
            CapabilityId = "missing-capability",
            Fixtures =
            [
                new EditableFixture { Alias = "session", Use = "shared.fixture" },
                new EditableFixture { Alias = "SESSION", Use = "other.fixture" }
            ],
            Actions =
            [
                new EditableExecutable
                {
                    Name = "api.request",
                    InputsText = "method=GET"
                },
                new EditableExecutable
                {
                    Name = string.Empty,
                    InputsText = "ignored"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "browser.goto",
                    InputsText = "bad-line"
                }
            ]
        };

        var analysis = service.Analyze(catalog, document);

        Assert.Equal("0 error(s), 6 warning(s)", analysis.Summary);
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("Capability 'missing-capability' was not found.", StringComparison.Ordinal));
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("Fixture alias 'session' is duplicated.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("'api.request' is missing required input 'path'.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("Step name is empty.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("'bad-line' should use key=value syntax.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("'browser.goto' is missing required input 'path'.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StudioAuthoringService_supports_catalog_free_templates_and_does_not_mutate_original_document()
    {
        var service = new StudioAuthoringService();
        var original = new FlowEditorDocument
        {
            Id = "fallback-flow",
            Name = "Fallback flow",
            TagsText = "existing",
            Actions =
            [
                new EditableExecutable
                {
                    Name = "existing.action",
                    InputsText = "value=1"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "existing.expectation",
                    InputsText = "value=2"
                }
            ]
        };

        var analysis = service.Analyze(null, original);
        var webTemplate = service.ApplyQuickAction(null, original, "template.web-smoke");
        var unknown = service.ApplyQuickAction(null, original, "unknown.action");

        Assert.Equal("0 error(s), 2 warning(s)", analysis.Summary);
        Assert.Contains(analysis.QuickActions, action => action.Id == "metadata.smoke");
        Assert.Contains(analysis.QuickActions, action => action.Id == "template.web-smoke");
        Assert.Contains(analysis.QuickActions, action => action.Id == "template.desktop-smoke");
        Assert.Contains(analysis.QuickActions, action => action.Id == "template.api-health");
        Assert.Contains(analysis.QuickActions, action => action.Id == "action.screenshot");
        Assert.Contains(analysis.QuickActions, action => action.Id == "expect.visible");
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("Step 'existing.action' is not registered.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Message.Contains("Step 'existing.expectation' is not registered.", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("browser.goto", webTemplate.Actions[^1].Name);
        Assert.Contains("path=/", webTemplate.Actions[^1].InputsText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("expect_visible", webTemplate.Expectations[^1].Name);
        Assert.Contains("text=Ready", webTemplate.Expectations[^1].InputsText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing, studio", webTemplate.TagsText);

        Assert.Single(original.Actions);
        Assert.Single(original.Expectations);
        Assert.Equal("existing", original.TagsText);

        Assert.NotSame(original, unknown);
        Assert.Equal(original.TagsText, unknown.TagsText);
        Assert.Equal(original.Actions.Count, unknown.Actions.Count);
        Assert.Equal(original.Expectations.Count, unknown.Expectations.Count);
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

    [Fact]
    public async Task StudioRecorderService_StopRecordingAsync_returns_empty_result_when_idle()
    {
        var service = new StudioRecorderService(CreateRuntimeOrchestrator(new AlwaysPassingDriver()));

        var result = await service.StopRecordingAsync();

        Assert.Empty(result.Events);
        Assert.Empty(result.Steps);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Null(result.ProcessName);
    }

    [Fact]
    public async Task StudioRecorderService_ReplayRecordedFlowAsync_returns_pass_summary_for_successful_flow()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        EnableFakeDriver(workspace);
        WriteReplayFlow(workspace);

        var service = new StudioRecorderService(CreateRuntimeOrchestrator(new AlwaysPassingDriver()));

        var result = await service.ReplayRecordedFlowAsync(
            workspace.GetPath("project", "flows", "replay.flow.yaml"),
            workspace.GetPath("project"));

        Assert.True(result.Passed);
        Assert.Contains("Passed", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.StepResults, step => step.Contains("fake.first", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StudioRecorderService_ReplayRecordedFlowAsync_returns_failure_summary_for_failed_flow()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        EnableFakeDriver(workspace);
        WriteReplayFlow(workspace);

        var service = new StudioRecorderService(CreateRuntimeOrchestrator(new AlwaysFailingDriver("driver failed")));

        var result = await service.ReplayRecordedFlowAsync(
            workspace.GetPath("project", "flows", "replay.flow.yaml"),
            workspace.GetPath("project"));

        Assert.False(result.Passed);
        Assert.Contains("Failed at step fake.first", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.StepResults, step => step.Contains("driver failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StudioRecorderService_StartWebRecordingAsync_and_StopRecordingAsync_capture_streamed_events()
    {
        using var script = TemporaryRecordScript.Create();
        using var scope = new EnvironmentVariableScope("CRESS_RECORD_MJS", script.ScriptPath);
        using var service = new StudioRecorderService(CreateRuntimeOrchestrator(new AlwaysPassingDriver()));
        var notifications = 0;
        service.StateChanged += () => Interlocked.Increment(ref notifications);

        await service.StartWebRecordingAsync("https://example.test", "chromium");
        await WaitForAsync(() => service.CapturedEventCount >= 2 && service.CurrentEvents.Count >= 2, TimeSpan.FromSeconds(10));
        await Task.Delay(400);
        var result = await service.StopRecordingAsync();

        Assert.False(service.IsRecording);
        Assert.Equal("browser", service.CurrentTarget?.ProcessName);
        Assert.Equal(2, result.Events.Count);
        Assert.NotEmpty(result.Steps);
        Assert.True(notifications > 0);
    }

    [Fact]
    public async Task StudioRecorderService_StartRecordingAsync_and_StopRecordingAsync_capture_desktop_events()
    {
        var session = new FakeDesktopRecordingSession();
        var service = new StudioRecorderService(
            CreateRuntimeOrchestrator(new AlwaysPassingDriver()),
            new FakeDesktopRecordingSessionFactory(session),
            () => new FakeWebRecordingClient(),
            processId => new RecordingTargetInfo
            {
                ProcessId = processId,
                ProcessName = "sample-app",
                MainWindowTitle = "Sample App",
                IsAttachable = true
            });
        var notifications = 0;
        service.StateChanged += () => notifications++;

        await service.StartRecordingAsync(123);

        var recordedEvent = new RecordedEvent
        {
            Kind = EventKind.Invoke,
            Element = new ElementInfo { AutomationId = "save-button", ControlType = "button" }
        };
        session.StopEvents = [recordedEvent];
        session.Emit(recordedEvent);
        InvokePrivateInstanceMethod(service, "FirePendingNotification");

        Assert.Single(service.CurrentEvents);

        var result = await service.StopRecordingAsync();

        Assert.True(session.StartCalled);
        Assert.False(service.IsRecording);
        Assert.Equal("sample-app", service.CurrentTarget?.ProcessName);
        Assert.Equal(1, service.CapturedEventCount);
        Assert.Single(result.Events);
        Assert.Equal("sample-app", result.ProcessName);
        Assert.True(notifications > 0);
    }

    [Fact]
    public async Task StudioRecorderService_StartRecordingAsync_replaces_existing_session()
    {
        var firstSession = new FakeDesktopRecordingSession();
        var secondSession = new FakeDesktopRecordingSession();
        var service = new StudioRecorderService(
            CreateRuntimeOrchestrator(new AlwaysPassingDriver()),
            new FakeDesktopRecordingSessionFactory(firstSession, secondSession),
            () => new FakeWebRecordingClient(),
            processId => new RecordingTargetInfo
            {
                ProcessId = processId,
                ProcessName = $"app-{processId}",
                MainWindowTitle = $"App {processId}",
                IsAttachable = true
            });

        await service.StartRecordingAsync(101);
        await service.StartRecordingAsync(202);

        Assert.True(firstSession.Disposed);
        Assert.True(secondSession.StartCalled);
        Assert.Equal(202, service.CurrentTarget?.ProcessId);
    }

    [Fact]
    public async Task StudioRecorderService_StartWebRecordingAsync_replaces_existing_desktop_session()
    {
        var desktopSession = new FakeDesktopRecordingSession();
        var webClient = new FakeWebRecordingClient();
        using var service = new StudioRecorderService(
            CreateRuntimeOrchestrator(new AlwaysPassingDriver()),
            new FakeDesktopRecordingSessionFactory(desktopSession),
            () => webClient,
            processId => new RecordingTargetInfo
            {
                ProcessId = processId,
                ProcessName = "desktop-app",
                MainWindowTitle = "Desktop App",
                IsAttachable = true
            });

        await service.StartRecordingAsync(123);
        await service.StartWebRecordingAsync("https://example.test", "firefox");

        Assert.True(desktopSession.Disposed);
        Assert.True(webClient.StartCalled);
        Assert.Equal("browser", service.CurrentTarget?.ProcessName);
    }

    [Fact]
    public async Task StudioRecorderService_StartRecordingAsync_replaces_existing_web_session()
    {
        var desktopSession = new FakeDesktopRecordingSession();
        var webClient = new FakeWebRecordingClient();
        using var service = new StudioRecorderService(
            CreateRuntimeOrchestrator(new AlwaysPassingDriver()),
            new FakeDesktopRecordingSessionFactory(desktopSession),
            () => webClient,
            processId => new RecordingTargetInfo
            {
                ProcessId = processId,
                ProcessName = "desktop-app",
                MainWindowTitle = "Desktop App",
                IsAttachable = true
            });

        await service.StartWebRecordingAsync("https://example.test", "firefox");
        await service.StartRecordingAsync(456);

        Assert.True(webClient.Disposed);
        Assert.True(desktopSession.StartCalled);
        Assert.Equal(456, service.CurrentTarget?.ProcessId);
    }

    [Fact]
    public async Task StudioRecorderService_StartWebRecordingAsync_and_StopRecordingAsync_use_injected_client()
    {
        var webClient = new FakeWebRecordingClient
        {
            StopEvents =
            [
                new RecordedEvent
                {
                    Kind = EventKind.Navigate,
                    Url = "https://example.test/next",
                    Element = new ElementInfo { ControlType = "document", Name = "Next" }
                }
            ]
        };
        using var service = new StudioRecorderService(
            CreateRuntimeOrchestrator(new AlwaysPassingDriver()),
            new FakeDesktopRecordingSessionFactory(new FakeDesktopRecordingSession()),
            () => webClient,
            processId => new RecordingTargetInfo { ProcessId = processId, ProcessName = "unused", MainWindowTitle = "unused", IsAttachable = true });

        await service.StartWebRecordingAsync("https://example.test", "firefox");
        var result = await service.StopRecordingAsync();

        Assert.True(webClient.StartCalled);
        Assert.True(webClient.StopCalled);
        Assert.Equal("browser", service.CurrentTarget?.ProcessName);
        Assert.Single(result.Events);
    }

    [Fact]
    public async Task StudioRecorderService_tracks_snapshot_and_live_inference_during_web_recording()
    {
        var webClient = new FakeWebRecordingClient();
        using var service = new StudioRecorderService(
            CreateRuntimeOrchestrator(new AlwaysPassingDriver()),
            new FakeDesktopRecordingSessionFactory(new FakeDesktopRecordingSession()),
            () => webClient,
            processId => new RecordingTargetInfo { ProcessId = processId, ProcessName = "unused", MainWindowTitle = "unused", IsAttachable = true });

        await service.StartWebRecordingAsync("https://example.test", "firefox");

        webClient.Emit(new RecordedEvent
        {
            Kind = EventKind.Navigate,
            Url = "https://example.test/checkout",
            Element = new ElementInfo { ControlType = "document", Name = "Checkout page" }
        });
        InvokePrivateInstanceMethod(service, "FirePendingNotification");
        var firstSnapshot = service.CurrentEvents;

        webClient.Emit(new RecordedEvent
        {
            Kind = EventKind.Invoke,
            Element = new ElementInfo { TestId = "submit-order", Role = "button", Label = "Submit order" }
        });
        InvokePrivateInstanceMethod(service, "FirePendingNotification");

        Assert.Single(firstSnapshot);
        Assert.Equal(2, service.CurrentEvents.Count);
        Assert.Equal(2, service.CapturedEventCount);
        Assert.NotEmpty(service.CurrentInferredSteps);

        await service.StopRecordingAsync();

        Assert.Empty(service.CurrentEvents);
        Assert.Empty(service.CurrentInferredSteps);
    }

    [Fact]
    public async Task StudioRecorderService_ReplayRecordedFlowAsync_returns_structured_failure_for_invalid_arguments()
    {
        var service = new StudioRecorderService(CreateRuntimeOrchestrator(new AlwaysPassingDriver()));

        var result = await service.ReplayRecordedFlowAsync(
            null!,
            null!);

        Assert.False(result.Passed);
        Assert.StartsWith("Failed at step ?:", result.Summary, StringComparison.Ordinal);
        Assert.Contains("Could not locate a Cress project", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public async Task StudioRecorderService_disposed_instance_rejects_new_recording_sessions()
    {
        var service = new StudioRecorderService(CreateRuntimeOrchestrator(new AlwaysPassingDriver()));
        service.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartRecordingAsync(123));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartWebRecordingAsync("https://example.test", "chromium"));
    }

    [Fact]
    public async Task StudioRecorderService_Dispose_disposes_active_desktop_session()
    {
        var session = new FakeDesktopRecordingSession();
        var service = new StudioRecorderService(
            CreateRuntimeOrchestrator(new AlwaysPassingDriver()),
            new FakeDesktopRecordingSessionFactory(session),
            () => new FakeWebRecordingClient(),
            processId => new RecordingTargetInfo
            {
                ProcessId = processId,
                ProcessName = "sample-app",
                MainWindowTitle = "Sample App",
                IsAttachable = true
            });

        await service.StartRecordingAsync(123);
        service.Dispose();

        Assert.True(session.Disposed);
    }

    [Fact]
    public void StudioRecorderService_Dispose_is_idempotent()
    {
        var service = new StudioRecorderService(CreateRuntimeOrchestrator(new AlwaysPassingDriver()));

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void DesktopRecordingSessionAdapter_proxies_lifecycle_calls()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);
        var adapter = new DesktopRecordingSessionAdapter(session);
        Action<RecordedEvent> handler = _ => { };

        adapter.EventCaptured += handler;
        adapter.EventCaptured -= handler;

        Assert.Empty(adapter.Stop());
        adapter.Dispose();
        Assert.Throws<ObjectDisposedException>(() => adapter.Start());
    }

    [Fact]
    public void DesktopRecordingSessionFactory_creates_adapter_instances()
    {
        var factory = new DesktopRecordingSessionFactory();

        using var session = factory.Create(Environment.ProcessId);

        Assert.IsType<DesktopRecordingSessionAdapter>(session);
    }

    [Fact]
    public async Task WebRecordingClientAdapter_proxies_start_stop_and_dispose()
    {
        using var script = TemporaryRecordScript.Create();
        using var scope = new EnvironmentVariableScope("CRESS_RECORD_MJS", script.ScriptPath);
        var client = new WebRecorderClient();
        var adapter = new WebRecordingClientAdapter(client);
        Action<RecordedEvent> handler = _ => { };

        adapter.EventCaptured += handler;
        adapter.EventCaptured -= handler;

        await adapter.StartAsync("https://example.test", "chromium", CancellationToken.None);
        await WaitForAsync(() => client.CurrentEvents.Count >= 2, TimeSpan.FromSeconds(10));
        var events = await adapter.StopAsync();
        adapter.Dispose();

        Assert.Equal(2, events.Count);
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

    private static RuntimeOrchestrator CreateRuntimeOrchestrator(IRuntimeDriver driver)
        => new(
            CreateCatalogService(),
            new PlanGenerator(),
            new ConfigLoader(new ProjectLocator()),
            new PluginHost(),
            new ReportGenerator(),
            [driver]);

    private static void WriteReplayFlow(TestWorkspace workspace)
    {
        workspace.WriteFile(Path.Combine("project", "flows", "replay.flow.yaml"), """
        version: 1
        id: replay-flow
        name: Replay flow
        when:
          - step: fake.first
          - step: fake.second
        then:
          - expect: fake.expect
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "replay.yaml"), """
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
    }

    private static void EnableFakeDriver(TestWorkspace workspace)
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
          fake:
            enabled: true
        """);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for the recorder service to receive events.");
    }

    private static object? InvokePrivateInstanceMethod(object target, string methodName, params object?[]? args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(target, args);
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

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _originalValue);
    }

    private sealed class AlwaysPassingDriver : IRuntimeDriver
    {
        public string Name => "fake";

        public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog) => [];

        public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
            => Task.FromResult<IDriverSession>(new FixedOutcomeSession(RunOutcome.Passed, "driver passed"));
    }

    private sealed class AlwaysFailingDriver(string message) : IRuntimeDriver
    {
        public string Name => "fake";

        public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog) => [];

        public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
            => Task.FromResult<IDriverSession>(new FixedOutcomeSession(RunOutcome.Failed, message));
    }

    private sealed class FixedOutcomeSession(RunOutcome outcome, string message) : IDriverSession
    {
        public string Name => "fake";

        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new DriverExecutionResult
            {
                Outcome = outcome,
                Message = message,
                FailureClassification = outcome == RunOutcome.Passed ? null : "test-failure"
            });

        public Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<EvidenceArtifact>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TemporaryRecordScript : IDisposable
    {
        private readonly string _rootPath;

        private TemporaryRecordScript(string rootPath, string scriptPath)
        {
            _rootPath = rootPath;
            ScriptPath = scriptPath;
        }

        public string ScriptPath { get; }

        public static TemporaryRecordScript Create()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "cress-studio-recorder-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var scriptPath = Path.Combine(rootPath, "record.mjs");
            File.WriteAllText(scriptPath, """
            console.log(JSON.stringify({
              kind: "click",
              timestamp: "2026-05-13T12:00:00Z",
              element: { testId: "save-button", role: "button", label: "Save" }
            }));
            console.log(JSON.stringify({
              kind: "navigate",
              timestamp: "2026-05-13T12:00:01Z",
              url: "https://example.test/next",
              element: { role: "document", text: "Next page" }
            }));
            setInterval(() => {}, 1000);
            process.on("SIGTERM", () => process.exit(0));
            process.on("SIGINT", () => process.exit(0));
            """);
            return new TemporaryRecordScript(rootPath, scriptPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }

    private sealed class FakeDesktopRecordingSessionFactory(params FakeDesktopRecordingSession[] sessions) : IDesktopRecordingSessionFactory
    {
        private readonly Queue<FakeDesktopRecordingSession> _sessions = new(sessions);

        public IDesktopRecordingSession Create(int processId)
            => _sessions.Dequeue();
    }

    private sealed class FakeDesktopRecordingSession : IDesktopRecordingSession
    {
        public event Action<RecordedEvent>? EventCaptured;

        public bool StartCalled { get; private set; }

        public bool Disposed { get; private set; }

        public IReadOnlyList<RecordedEvent> StopEvents { get; set; } = [];

        public void Start() => StartCalled = true;

        public IReadOnlyList<RecordedEvent> Stop() => StopEvents;

        public void Dispose() => Disposed = true;

        public void Emit(RecordedEvent recordedEvent) => EventCaptured?.Invoke(recordedEvent);
    }

    private sealed class FakeWebRecordingClient : IWebRecordingClient
    {
        public event Action<RecordedEvent>? EventCaptured;

        public bool StartCalled { get; private set; }

        public bool StopCalled { get; private set; }

        public bool Disposed { get; private set; }

        public IReadOnlyList<RecordedEvent> StopEvents { get; set; } = [];

        public Task StartAsync(string url, string browserType, CancellationToken ct)
        {
            StartCalled = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RecordedEvent>> StopAsync()
        {
            StopCalled = true;
            return Task.FromResult(StopEvents);
        }

        public void Dispose() => Disposed = true;

        public void Emit(RecordedEvent recordedEvent) => EventCaptured?.Invoke(recordedEvent);
    }
}
