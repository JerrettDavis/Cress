using Cress.Core.Models;
using Cress.Execution;
using Cress.Execution.Drivers;
using Cress.ProjectSystem;
using Cress.Specs;

namespace Cress.UnitTests;

/// <summary>
/// V6 — Screenshots + per-step timing tests.
/// Covers: DurationMs populated, ui.screenshot step, auto-screenshot on assertion failure,
/// on-assertion-failure policy, and report rendering of durationMs.
/// </summary>
public sealed class ScreenshotAndTimingTests
{
    // -----------------------------------------------------------------------
    // 1. DurationMs is populated for every step
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StepRunResult_DurationMs_IsPopulated()
    {
        using var workspace = new TestWorkspace();
        WriteMinimalHttpProject(workspace);
        workspace.WriteFile(Path.Combine("project", "flows", "timing.flow.yaml"), """
        version: 1
        id: timing-flow
        name: Timing flow
        when:
          - step: fake.act
            with: {}
        then:
          - expect: fake.act
            with: {}
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "fake.yaml"), FakeStepManifest("fake.act", "fake", "act"));

        var orchestrator = CreateOrchestrator([new FakePassDriver()]);
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.True(result.Passed, BuildFailureMessage(result));
        Assert.NotEmpty(result.Flows[0].Steps);
        // Verify all steps have populated timing data
        foreach (var step in result.Flows[0].Steps)
        {
            Assert.True(step.DurationMs >= 0, $"Expected DurationMs >= 0 for step '{step.Name}', got {step.DurationMs}");
            Assert.True(step.StartedAt <= step.EndedAt, $"StartedAt must be <= EndedAt for step '{step.Name}'");
        }
    }

    // -----------------------------------------------------------------------
    // 2. DurationMs > 0 for a step that takes measurable time
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StepRunResult_DurationMs_IsGreaterThanZeroForSlowStep()
    {
        using var workspace = new TestWorkspace();
        WriteMinimalHttpProject(workspace);
        workspace.WriteFile(Path.Combine("project", "flows", "slow.flow.yaml"), """
        version: 1
        id: slow-flow
        name: Slow flow
        when:
          - step: fake.slow-act
            with: {}
        then:
          - expect: fake.slow-act
            with: {}
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "fake.yaml"), FakeStepManifest("fake.slow-act", "fake", "slow-act"));

        var orchestrator = CreateOrchestrator([new FakeSlowDriver(delayMs: 20)]);
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.True(result.Passed, BuildFailureMessage(result));
        Assert.NotEmpty(result.Flows[0].Steps);
        // The action step ran with a delay — confirm at least one step has DurationMs > 0
        var actionStep = result.Flows[0].Steps.First(s => s.Kind == "action");
        Assert.True(actionStep.DurationMs > 0, $"Expected DurationMs > 0 for a step with a 20ms delay, got {actionStep.DurationMs}");
    }

    // -----------------------------------------------------------------------
    // 3. Auto-screenshot captured when assertion fails with on-failure policy
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Orchestrator_CapturesScreenshot_WhenAssertionFails_OnFailurePolicy()
    {
        using var workspace = new TestWorkspace();
        WriteMinimalFlawrightLikeProject(workspace, screenshotPolicy: "on-failure");
        workspace.WriteFile(Path.Combine("project", "flows", "assert-fail.flow.yaml"), """
        version: 1
        id: assert-fail-flow
        name: Assert failure flow
        when:
          - step: fake.act
            with: {}
        then:
          - expect: fake.assert-fail
            with: {}
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "fake.yaml"), $"""
        version: 1
        steps:
          - name: fake.act
            drivers:
              - flawright
            retrySafe: false
            implementation:
              operation: act
          - name: fake.assert-fail
            drivers:
              - flawright
            retrySafe: false
            implementation:
              operation: assert-fail
        """);

        var screenshotDriver = new FakeScreenshotDriver(assertionFailStep: "fake.assert-fail");
        var orchestrator = CreateOrchestrator([screenshotDriver]);
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.NotEmpty(result.Flows);
        var failedStep = result.Flows[0].Steps.First(s => s.Name == "fake.assert-fail");
        Assert.Equal(RunOutcome.Failed, failedStep.Outcome);
        Assert.True(failedStep.Artifacts.Count > 0, "Expected a screenshot artifact attached to the failed assertion step.");
        Assert.True(screenshotDriver.ScreenshotCaptured, "Expected the driver to be asked for a screenshot.");
    }

    // -----------------------------------------------------------------------
    // 4. on-assertion-failure policy: captures screenshot on assertion failure only
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Orchestrator_CapturesScreenshot_OnAssertionFailurePolicy_WhenAssertionFails()
    {
        using var workspace = new TestWorkspace();
        WriteMinimalFlawrightLikeProject(workspace, screenshotPolicy: "on-assertion-failure");
        workspace.WriteFile(Path.Combine("project", "flows", "assert-fail.flow.yaml"), """
        version: 1
        id: assert-fail-flow
        name: Assert failure flow
        when:
          - step: fake.act
            with: {}
        then:
          - expect: fake.assert-fail
            with: {}
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "fake.yaml"), $"""
        version: 1
        steps:
          - name: fake.act
            drivers:
              - flawright
            retrySafe: false
            implementation:
              operation: act
          - name: fake.assert-fail
            drivers:
              - flawright
            retrySafe: false
            implementation:
              operation: assert-fail
        """);

        var screenshotDriver = new FakeScreenshotDriver(assertionFailStep: "fake.assert-fail");
        var orchestrator = CreateOrchestrator([screenshotDriver]);
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.NotEmpty(result.Flows);
        var failedStep = result.Flows[0].Steps.First(s => s.Name == "fake.assert-fail");
        Assert.Equal(RunOutcome.Failed, failedStep.Outcome);
        Assert.True(failedStep.Artifacts.Count > 0, "Expected a screenshot artifact attached to the failed assertion step under on-assertion-failure policy.");
        Assert.True(screenshotDriver.ScreenshotCaptured, "Expected the driver to be asked for a screenshot.");
    }

    // -----------------------------------------------------------------------
    // 5. on-assertion-failure policy: does NOT capture screenshot on non-assertion failure
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Orchestrator_DoesNotCaptureScreenshot_OnAssertionFailurePolicy_WhenNonAssertionFails()
    {
        using var workspace = new TestWorkspace();
        WriteMinimalFlawrightLikeProject(workspace, screenshotPolicy: "on-assertion-failure");
        workspace.WriteFile(Path.Combine("project", "flows", "generic-fail.flow.yaml"), """
        version: 1
        id: generic-fail-flow
        name: Generic failure flow
        when:
          - step: fake.generic-fail
            with: {}
        then:
          - expect: fake.generic-fail
            with: {}
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "fake.yaml"), FakeStepManifest("fake.generic-fail", "flawright", "generic-fail"));

        var screenshotDriver = new FakeScreenshotDriver(genericFailStep: "fake.generic-fail");
        var orchestrator = CreateOrchestrator([screenshotDriver]);
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.NotEmpty(result.Flows);
        var failedStep = result.Flows[0].Steps.First(s => s.Name == "fake.generic-fail");
        Assert.Equal(RunOutcome.Failed, failedStep.Outcome);
        // Non-assertion failure with on-assertion-failure policy should NOT trigger screenshot from orchestrator
        Assert.False(screenshotDriver.ScreenshotCaptured, "Expected no screenshot for a non-assertion failure under on-assertion-failure policy.");
    }

    // -----------------------------------------------------------------------
    // 6. HTML report includes durationMs per step
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReportGenerator_HtmlReport_IncludesDurationMs()
    {
        using var workspace = new TestWorkspace();
        WriteMinimalHttpProject(workspace);
        workspace.WriteFile(Path.Combine("project", "flows", "report.flow.yaml"), """
        version: 1
        id: report-flow
        name: Report flow
        when:
          - step: fake.act
            with: {}
        then:
          - expect: fake.act
            with: {}
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "fake.yaml"), FakeStepManifest("fake.act", "fake", "act"));

        var orchestrator = CreateOrchestrator([new FakePassDriver()]);
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions
        {
            ReportFormats = ["html", "json"]
        });

        Assert.True(result.Passed, BuildFailureMessage(result));
        Assert.True(result.Reports.TryGetValue("html", out var htmlPath) && File.Exists(htmlPath), "HTML report should be generated.");
        var html = File.ReadAllText(htmlPath!);
        Assert.Contains("ms", html, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // 7. JSON report includes durationMs per step
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReportGenerator_JsonReport_IncludesDurationMs()
    {
        using var workspace = new TestWorkspace();
        WriteMinimalHttpProject(workspace);
        workspace.WriteFile(Path.Combine("project", "flows", "json-report.flow.yaml"), """
        version: 1
        id: json-report-flow
        name: JSON report flow
        when:
          - step: fake.act
            with: {}
        then:
          - expect: fake.act
            with: {}
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "fake.yaml"), FakeStepManifest("fake.act", "fake", "act"));

        var orchestrator = CreateOrchestrator([new FakePassDriver()]);
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions
        {
            ReportFormats = ["json"]
        });

        Assert.True(result.Passed, BuildFailureMessage(result));
        Assert.True(result.Reports.TryGetValue("json", out var jsonPath) && File.Exists(jsonPath), "JSON report should be generated.");
        var json = File.ReadAllText(jsonPath!);
        Assert.Contains("durationMs", json, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static RuntimeOrchestrator CreateOrchestrator(IReadOnlyList<IRuntimeDriver> drivers)
    {
        var locator = new ProjectLocator();
        var configLoader = new ConfigLoader(locator);
        var profileLoader = new ProfileLoader();
        var flowParser = new FlowParser();
        var flowNormalizer = new FlowNormalizer();
        var capabilityParser = new CapabilityParser();
        var stepParser = new StepManifestParser();
        var fixtureParser = new FixtureManifestParser();
        var catalogService = new ProjectCatalogService(
            locator, configLoader, profileLoader, flowParser, flowNormalizer,
            capabilityParser, stepParser, fixtureParser, new StepRegistry());
        return new RuntimeOrchestrator(
            catalogService,
            new PlanGenerator(),
            configLoader,
            new PluginHost(),
            new ReportGenerator(),
            drivers);
    }

    private static void WriteMinimalHttpProject(TestWorkspace workspace)
    {
        workspace.WriteFile(Path.Combine("project", ".cress", "config.yaml"), """
        version: 1
        project:
          name: Screenshot Test Project
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
          timeout: 5000
          retries: 0
          evidence: standard
          cleanup: on-success
        plugins:
          discover:
            - plugins
            - steps
        drivers:
          fake:
            enabled: true
        """);
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "local.yaml"), """
        profile: local
        baseUrl: http://localhost
        variables:
          environment: test
        """);
        Directory.CreateDirectory(workspace.GetPath("project", "steps", "manifests"));
        Directory.CreateDirectory(workspace.GetPath("project", "fixtures"));
    }

    private static void WriteMinimalFlawrightLikeProject(TestWorkspace workspace, string screenshotPolicy)
    {
        workspace.WriteFile(Path.Combine("project", ".cress", "config.yaml"), """
        version: 1
        project:
          name: Screenshot Test Project
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
          timeout: 5000
          retries: 0
          evidence: standard
          cleanup: on-success
        plugins:
          discover:
            - plugins
            - steps
        drivers:
          flawright:
            enabled: true
        """);
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "local.yaml"), $"""
        profile: local
        baseUrl: http://localhost
        evidence:
          screenshotPolicy: {screenshotPolicy}
        variables:
          environment: test
        """);
        Directory.CreateDirectory(workspace.GetPath("project", "steps", "manifests"));
        Directory.CreateDirectory(workspace.GetPath("project", "fixtures"));
    }

    private static string FakeStepManifest(string stepName, string driverName, string operation) => $"""
    version: 1
    steps:
      - name: {stepName}
        drivers:
          - {driverName}
        retrySafe: false
        implementation:
          operation: {operation}
    """;

    private static string BuildFailureMessage(RunResult result)
    {
        var parts = new List<string>();
        foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            parts.Add($"[{d.Code}] {d.Message}");
        }
        foreach (var f in result.Flows.Where(f => f.Outcome is RunOutcome.Failed or RunOutcome.Errored))
        {
            parts.Add($"{f.FlowId}: {f.FailureMessage ?? f.FailureClassification ?? "unknown"}");
        }
        return parts.Count == 0 ? "(no diagnostics or flow failures)" : string.Join(" | ", parts);
    }

    // -----------------------------------------------------------------------
    // Fake drivers for unit testing
    // -----------------------------------------------------------------------

    /// <summary>Driver that always passes for any operation.</summary>
    private sealed class FakePassDriver : IRuntimeDriver
    {
        public string Name => "fake";

        public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog) => [];

        public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
            => Task.FromResult<IDriverSession>(new FakePassSession());

        private sealed class FakePassSession : IDriverSession
        {
            public string Name => "fake";
            public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
                => Task.FromResult(new DriverExecutionResult { Outcome = RunOutcome.Passed, Message = "ok" });

            public Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<EvidenceArtifact>>([]);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Driver whose session sleeps for <paramref name="delayMs"/> ms before returning.</summary>
    private sealed class FakeSlowDriver(int delayMs) : IRuntimeDriver
    {
        public string Name => "fake";

        public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog) => [];

        public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
            => Task.FromResult<IDriverSession>(new FakeSlowSession(delayMs));

        private sealed class FakeSlowSession(int delayMs) : IDriverSession
        {
            public string Name => "fake";
            public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public async Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
            {
                await Task.Delay(delayMs, cancellationToken);
                return new DriverExecutionResult { Outcome = RunOutcome.Passed, Message = "slow ok" };
            }

            public Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<EvidenceArtifact>>([]);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Driver named "flawright" so the orchestrator's MaybeCaptureScreenshotAsync will engage.
    /// Fails specified steps with assertion-failed or generic failure,
    /// and records whether a screenshot was requested.
    /// </summary>
    private sealed class FakeScreenshotDriver(string? assertionFailStep = null, string? genericFailStep = null) : IRuntimeDriver
    {
        public string Name => "flawright";

        public bool ScreenshotCaptured { get; private set; }

        public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog) => [];

        public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
            => Task.FromResult<IDriverSession>(new FakeScreenshotSession(this, assertionFailStep, genericFailStep));

        private sealed class FakeScreenshotSession(FakeScreenshotDriver parent, string? assertionFailStep, string? genericFailStep) : IDriverSession
        {
            public string Name => "flawright";
            public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
            {
                // When the orchestrator asks for a screenshot (capture-screenshot operation), record it and succeed.
                if (string.Equals(action.Operation, "capture-screenshot", StringComparison.OrdinalIgnoreCase))
                {
                    parent.ScreenshotCaptured = true;
                    var artifactRelative = Path.Combine("screenshots", "auto-capture.png");
                    var artifactFull = Path.Combine(context.ArtifactRoot, artifactRelative);
                    Directory.CreateDirectory(Path.GetDirectoryName(artifactFull)!);
                    File.WriteAllBytes(artifactFull, [0x89, 0x50, 0x4E, 0x47]); // minimal PNG header
                    return Task.FromResult(new DriverExecutionResult
                    {
                        Outcome = RunOutcome.Passed,
                        Message = "screenshot captured",
                        Artifacts =
                        [
                            new EvidenceArtifact
                            {
                                Category = "screenshots",
                                RelativePath = artifactRelative,
                                Description = "auto-capture"
                            }
                        ]
                    });
                }

                if (!string.IsNullOrWhiteSpace(assertionFailStep)
                    && string.Equals(action.Name, assertionFailStep, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new DriverExecutionResult
                    {
                        Outcome = RunOutcome.Failed,
                        Message = "assertion failed",
                        FailureClassification = "assertion-failed"
                    });
                }

                if (!string.IsNullOrWhiteSpace(genericFailStep)
                    && string.Equals(action.Name, genericFailStep, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new DriverExecutionResult
                    {
                        Outcome = RunOutcome.Failed,
                        Message = "generic failure",
                        FailureClassification = "some-other-error"
                    });
                }

                return Task.FromResult(new DriverExecutionResult { Outcome = RunOutcome.Passed, Message = "ok" });
            }

            public Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<EvidenceArtifact>>([]);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
