using Cress.Core.Models;
using Cress.ProjectSystem;
using Cress.Sdk;

namespace Cress.Execution;

public sealed class RuntimeOrchestrator
{
    private readonly ProjectCatalogService _catalogService;
    private readonly PlanGenerator _planGenerator;
    private readonly ConfigLoader _configLoader;
    private readonly PluginHost _pluginHost;
    private readonly ReportGenerator _reportGenerator;
    private readonly IReadOnlyDictionary<string, IRuntimeDriver> _drivers;

    public RuntimeOrchestrator(
        ProjectCatalogService catalogService,
        PlanGenerator planGenerator,
        ConfigLoader configLoader,
        PluginHost pluginHost,
        ReportGenerator reportGenerator,
        IEnumerable<IRuntimeDriver> drivers)
    {
        _catalogService = catalogService;
        _planGenerator = planGenerator;
        _configLoader = configLoader;
        _pluginHost = pluginHost;
        _reportGenerator = reportGenerator;
        _drivers = drivers.ToDictionary(driver => driver.Name, StringComparer.OrdinalIgnoreCase);
    }

    public Task<RunResult> ExecuteAsync(string startDirectory, RunOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(startDirectory, options, progress: null, cancellationToken);

    public async Task<RunResult> ExecuteAsync(string startDirectory, RunOptions options, IProgress<RuntimeProgressUpdate>? progress, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();
        var catalogResult = _catalogService.Load(startDirectory, options.Profile);
        diagnostics.AddRange(catalogResult.Diagnostics);
        if (catalogResult.Value is null)
        {
            return new RunResult { Diagnostics = diagnostics };
        }

        var catalog = ApplyOverrides(catalogResult.Value, options);
        var selectedFlows = SelectFlows(catalog, options, diagnostics);
        if (selectedFlows.Count == 0)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "RUN001",
                Message = "No flows matched the requested selection.",
                File = options.FlowPath ?? string.Join(", ", options.FlowPaths)
            });
            return new RunResult { Diagnostics = diagnostics };
        }

        var planCollection = _planGenerator.Generate(catalog, selectedFlows);
        diagnostics.AddRange(planCollection.Diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new RunResult { Diagnostics = diagnostics };
        }

        var startedAt = DateTimeOffset.UtcNow;
        var runId = $"run-{startedAt:yyyyMMddHHmmssfff}";
        var artifactRoot = Path.Combine(catalog.ProjectRoot, catalog.EffectiveConfig.Config.Paths.Artifacts, runId);
        var evidenceStore = new EvidenceStore(artifactRoot);
        var metadata = new RunMetadata
        {
            RunId = runId,
            ArtifactRoot = artifactRoot,
            ProjectName = catalog.EffectiveConfig.Config.Project.Name,
            Profile = catalog.EffectiveConfig.ActiveProfile,
            Environment = catalog.EffectiveConfig.Profile.Variables?.TryGetValue("environment", out var environment) == true ? environment : null,
            StartedAt = startedAt
        };
        var invocation = BuildInvocation(options, catalog, selectedFlows);
        progress?.Report(new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.RunStarted,
            RunId = runId,
            Message = $"Running {selectedFlows.Count} flow(s)."
        });

        evidenceStore.WriteText("config.effective.yaml", _configLoader.Serialize(catalog.EffectiveConfig), "root", "Effective configuration");
        evidenceStore.WriteJson("flow.normalized.json", selectedFlows, "root", "Normalized flows");
        evidenceStore.WriteJson("plan.json", planCollection.Plans, "root", "Execution plan");

        var flowResults = new List<FlowRunResult>();
        if (options.Parallel.GetValueOrDefault() > 1)
        {
            var semaphore = new SemaphoreSlim(options.Parallel!.Value);
            var tasks = planCollection.Plans.Select(async plan =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var flow = selectedFlows.First(item => item.FlowId.Equals(plan.FlowId, StringComparison.OrdinalIgnoreCase));
                    return await ExecuteFlowAsync(catalog, flow, plan, evidenceStore, options, progress, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            flowResults.AddRange(await Task.WhenAll(tasks));
        }
        else
        {
            foreach (var plan in planCollection.Plans)
            {
                var flow = selectedFlows.First(item => item.FlowId.Equals(plan.FlowId, StringComparison.OrdinalIgnoreCase));
                var result = await ExecuteFlowAsync(catalog, flow, plan, evidenceStore, options, progress, cancellationToken);
                flowResults.Add(result);
                if (!options.ContinueOnFailure && result.Outcome is RunOutcome.Failed or RunOutcome.Errored)
                {
                    break;
                }
            }
        }

        var endedAt = DateTimeOffset.UtcNow;
        var provisional = new RunResult
        {
            Metadata = metadata with
            {
                EndedAt = endedAt,
                DurationMs = (endedAt - startedAt).TotalMilliseconds
            },
            Invocation = invocation,
            Flows = flowResults.OrderBy(result => result.FlowId, StringComparer.OrdinalIgnoreCase).ToList(),
            Diagnostics = diagnostics,
            ArtifactIndex = evidenceStore.SnapshotIndex()
        };

        var reports = _reportGenerator.Generate(catalog, provisional, options.ReportFormats);
        var final = provisional with { Reports = reports };
        evidenceStore.WriteJson("run.json", final.Metadata, "root", "Run metadata");
        evidenceStore.WriteJson("invocation.json", final.Invocation, "root", "Run invocation");
        evidenceStore.WriteJson("timeline.json", final.Flows.SelectMany(flow => flow.Steps).ToList(), "root", "Run timeline");
        evidenceStore.WriteJson("result.json", final, "root", "Run result");
        evidenceStore.WriteJson("index.json", final.ArtifactIndex, "root", "Artifact index");
        evidenceStore.WriteText("failure-analysis.md", BuildFailureSummary(final), "root", "Failure summary");
        var completed = final with { ArtifactIndex = evidenceStore.SnapshotIndex() };
        progress?.Report(new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.RunCompleted,
            RunId = runId,
            Run = completed,
            Message = completed.Passed ? "Run passed." : "Run finished with failures."
        });
        return completed;
    }

    private async Task<FlowRunResult> ExecuteFlowAsync(ProjectCatalog catalog, NormalizedFlow flow, ExecutionPlan plan, EvidenceStore evidenceStore, RunOptions options, IProgress<RuntimeProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stepResults = new List<StepRunResult>();
        var sessions = new Dictionary<string, IDriverSession>(StringComparer.OrdinalIgnoreCase);
        var driverMetadata = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var flowFailed = false;
        var cleanupFailed = false;
        var reachedRequestedStart = string.IsNullOrWhiteSpace(options.StartFromStep);
        var passedWithRetry = false;
        string? failureMessage = null;
        string? failureClassification = null;
        var logger = new CollectingLogger(evidenceStore, flow.FlowId);
        progress?.Report(new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.FlowStarted,
            FlowId = flow.FlowId,
            FlowName = flow.Name,
            Message = $"Starting {flow.Name}."
        });

        try
        {
            foreach (var driverName in plan.RequiredDrivers)
            {
                if (!_drivers.TryGetValue(driverName, out var driver))
                {
                    flowFailed = true;
                    failureMessage = $"Driver '{driverName}' is not available.";
                    failureClassification = "driver-not-found";
                    break;
                }

                var health = driver.HealthCheck(catalog);
                if (health.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    flowFailed = true;
                    failureMessage = health.First(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Message;
                    failureClassification = "driver-health-failed";
                    break;
                }

                var session = await driver.StartSessionAsync(new DriverSessionStartContext
                {
                    ProjectRoot = catalog.ProjectRoot,
                    FlowId = flow.FlowId,
                    ArtifactRoot = evidenceStore.ArtifactRoot,
                    EvidenceStore = evidenceStore,
                    EffectiveConfig = catalog.EffectiveConfig
                }, cancellationToken);
                sessions[driverName] = session;
                driverMetadata[driverName] = session.Metadata;
            }

            var executionContext = new FlowExecutionContext
            {
                FlowId = flow.FlowId,
                FlowName = flow.Name,
                ArtifactRoot = evidenceStore.ArtifactRoot,
                EffectiveConfig = catalog.EffectiveConfig,
                EvidenceStore = evidenceStore,
                Variables = new Dictionary<string, string>(catalog.EffectiveConfig.Profile.Variables ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
                Fixtures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Logger = logger,
                Drivers = new DictionaryDriverAccessor(driverMetadata)
            };

            if (!reachedRequestedStart
                && !plan.Actions.Any(action => action.Kind != "setup" && action.Kind != "cleanup" && MatchesRequestedStart(action, options.StartFromStep)))
            {
                flowFailed = true;
                failureMessage = $"Requested rerun step '{options.StartFromStep}' was not found in flow '{flow.FlowId}'.";
                failureClassification = "rerun-start-not-found";
            }

            foreach (var action in plan.Actions)
            {
                if (!reachedRequestedStart && action.Kind != "setup" && action.Kind != "cleanup")
                {
                    if (MatchesRequestedStart(action, options.StartFromStep))
                    {
                        reachedRequestedStart = true;
                    }
                    else
                    {
                        stepResults.Add(new StepRunResult
                        {
                            Kind = action.Kind,
                            Name = action.Name,
                            Driver = action.Driver,
                            Owner = action.Owner,
                            Outcome = RunOutcome.Skipped,
                            Message = $"Skipped because the rerun started from '{options.StartFromStep}'.",
                            StartedAt = DateTimeOffset.UtcNow,
                            EndedAt = DateTimeOffset.UtcNow,
                            Inputs = new Dictionary<string, string>(action.Inputs, StringComparer.OrdinalIgnoreCase)
                        });
                        continue;
                    }
                }

                if (flowFailed && action.Kind != "cleanup")
                {
                    continue;
                }

                if (action.Kind == "cleanup")
                {
                    var cleanupPolicy = action.Inputs.TryGetValue("cleanupPolicy", out var value) ? value : "on-success";
                    if (cleanupPolicy.Equals("on-success", StringComparison.OrdinalIgnoreCase) && flowFailed)
                    {
                        continue;
                    }

                    if (cleanupPolicy.Equals("on-failure", StringComparison.OrdinalIgnoreCase) && !flowFailed)
                    {
                        continue;
                    }
                }

                var maxAttempts = action.Kind == "cleanup" || !action.RetrySafe
                    ? 1
                    : Math.Max(1, (options.RetryCountOverride ?? catalog.EffectiveConfig.Config.Defaults.Retries) + 1);

                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var stepStartedAt = DateTimeOffset.UtcNow;
                    var driverResult = await ExecuteActionAsync(catalog, action, executionContext, sessions, cancellationToken);
                    foreach (var output in driverResult.Outputs)
                    {
                        executionContext.Variables[output.Key] = output.Value;
                    }

                    var stepResult = new StepRunResult
                    {
                        Kind = action.Kind,
                        Name = action.Name,
                        Driver = action.Driver,
                        Owner = action.Owner,
                        Outcome = driverResult.Outcome,
                        Message = driverResult.Message,
                        FailureClassification = driverResult.FailureClassification,
                        Attempt = attempt,
                        StartedAt = stepStartedAt,
                        EndedAt = DateTimeOffset.UtcNow,
                        DurationMs = (DateTimeOffset.UtcNow - stepStartedAt).TotalMilliseconds,
                        Inputs = new Dictionary<string, string>(action.Inputs, StringComparer.OrdinalIgnoreCase),
                        Artifacts = driverResult.Artifacts
                    };
                    stepResults.Add(stepResult);
                    progress?.Report(new RuntimeProgressUpdate
                    {
                        Kind = RuntimeProgressKind.StepCompleted,
                        FlowId = flow.FlowId,
                        FlowName = flow.Name,
                        Step = stepResult,
                        Message = $"{action.Kind}: {action.Name} attempt {attempt} -> {stepResult.Outcome}"
                    });

                    if (driverResult.Outcome == RunOutcome.Passed)
                    {
                        if (attempt > 1)
                        {
                            passedWithRetry = true;
                        }

                        break;
                    }

                    if (attempt == maxAttempts)
                    {
                        if (action.Kind == "cleanup")
                        {
                            cleanupFailed = true;
                        }
                        else
                        {
                            flowFailed = true;
                            failureMessage ??= driverResult.Message;
                            failureClassification ??= driverResult.FailureClassification;
                        }
                    }
                }
            }

            foreach (var session in sessions.Values)
            {
                var artifacts = await session.CaptureFinalEvidenceAsync(executionContext, cancellationToken);
                if (artifacts.Count > 0)
                {
                    stepResults.Add(new StepRunResult
                    {
                        Kind = "evidence",
                        Name = $"{session.Name}.capture",
                        Driver = session.Name,
                        Outcome = RunOutcome.Passed,
                        StartedAt = DateTimeOffset.UtcNow,
                        EndedAt = DateTimeOffset.UtcNow,
                        Artifacts = artifacts
                    });
                    progress?.Report(new RuntimeProgressUpdate
                    {
                        Kind = RuntimeProgressKind.StepCompleted,
                        FlowId = flow.FlowId,
                        FlowName = flow.Name,
                        Step = stepResults[^1],
                        Message = $"{session.Name} final evidence captured."
                    });
                }
            }
        }
        catch (Exception ex)
        {
            flowFailed = true;
            failureMessage = ex.Message;
            failureClassification = "unexpected-error";
        }
        finally
        {
            foreach (var session in sessions.Values)
            {
                await session.DisposeAsync();
            }
        }

        var endedAt = DateTimeOffset.UtcNow;
        var flowResult = new FlowRunResult
        {
            FlowId = flow.FlowId,
            Name = flow.Name,
            CapabilityId = flow.CapabilityId,
            SourceFile = flow.SourceFile,
            Outcome = flowFailed ? RunOutcome.Failed : RunOutcome.Passed,
            FailureMessage = failureMessage,
            FailureClassification = failureClassification,
            CleanupFailed = cleanupFailed,
            PassedWithRetry = passedWithRetry,
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationMs = (endedAt - startedAt).TotalMilliseconds,
            Drivers = plan.RequiredDrivers,
            Traceability = flow.Traceability,
            Steps = stepResults
        };
        progress?.Report(new RuntimeProgressUpdate
        {
            Kind = RuntimeProgressKind.FlowCompleted,
            FlowId = flow.FlowId,
            FlowName = flow.Name,
            Flow = flowResult,
            Message = $"{flow.Name} finished with {flowResult.Outcome}."
        });
        return flowResult;
    }

    private IReadOnlyList<NormalizedFlow> SelectFlows(ProjectCatalog catalog, RunOptions options, ICollection<Diagnostic> diagnostics)
    {
        if (options.FlowPaths.Count == 0)
        {
            return _catalogService.SelectFlows(catalog, options.FlowPath, options.Tag, diagnostics);
        }

        var selectors = options.FlowPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(catalog.ProjectRoot, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var flows = catalog.NormalizedFlows
            .Where(flow => !string.IsNullOrWhiteSpace(flow.SourceFile) && selectors.Contains(Path.GetFullPath(flow.SourceFile)))
            .OrderBy(flow => flow.FlowId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(options.Tag))
        {
            flows = flows.Where(flow => flow.Tags.Contains(options.Tag, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        if (flows.Count == 0)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "SEL002",
                Message = "No flows matched the requested selection list.",
                File = string.Join(", ", options.FlowPaths)
            });
        }

        return flows;
    }

    private static ProjectCatalog ApplyOverrides(ProjectCatalog catalog, RunOptions options)
    {
        var evidence = (catalog.EffectiveConfig.Profile.Evidence ?? new EvidenceProfileConfig()) with
        {
            Mode = options.EvidenceModeOverride ?? catalog.EffectiveConfig.Profile.Evidence?.Mode ?? catalog.EffectiveConfig.Config.Defaults.Evidence,
            ScreenshotPolicy = options.ScreenshotPolicyOverride ?? catalog.EffectiveConfig.Profile.Evidence?.ScreenshotPolicy ?? "on-failure"
        };

        return catalog with
        {
            EffectiveConfig = catalog.EffectiveConfig with
            {
                Profile = catalog.EffectiveConfig.Profile with { Evidence = evidence },
                Config = catalog.EffectiveConfig.Config with
                {
                    Defaults = catalog.EffectiveConfig.Config.Defaults with
                    {
                        Evidence = options.EvidenceModeOverride ?? catalog.EffectiveConfig.Config.Defaults.Evidence,
                        Retries = options.RetryCountOverride ?? catalog.EffectiveConfig.Config.Defaults.Retries
                    }
                }
            }
        };
    }

    private static RunInvocation BuildInvocation(RunOptions options, ProjectCatalog catalog, IReadOnlyList<NormalizedFlow> flows)
        => new()
        {
            Trigger = string.IsNullOrWhiteSpace(options.Trigger) ? "manual" : options.Trigger!,
            RequestedFlows = flows.Select(flow => flow.SourceFile ?? flow.FlowId).ToList(),
            Tag = options.Tag,
            StartFromStep = options.StartFromStep,
            RetryCount = options.RetryCountOverride ?? catalog.EffectiveConfig.Config.Defaults.Retries,
            EvidenceMode = options.EvidenceModeOverride ?? catalog.EffectiveConfig.Profile.Evidence?.Mode ?? catalog.EffectiveConfig.Config.Defaults.Evidence,
            ScreenshotPolicy = options.ScreenshotPolicyOverride ?? catalog.EffectiveConfig.Profile.Evidence?.ScreenshotPolicy ?? "on-failure"
        };

    private async Task<DriverExecutionResult> ExecuteActionAsync(
        ProjectCatalog catalog,
        PlanAction action,
        FlowExecutionContext executionContext,
        IReadOnlyDictionary<string, IDriverSession> sessions,
        CancellationToken cancellationToken)
    {
        DriverExecutionResult result;
        if (action.Kind is "setup" or "cleanup")
        {
            result = await _pluginHost.InvokeFixtureAsync(catalog, action, executionContext, cancellationToken);
            if (result.Outcome == RunOutcome.Passed && action.Kind == "setup" && action.Inputs.TryGetValue("alias", out var alias))
            {
                executionContext.Fixtures[alias] = result.Outputs.TryGetValue("id", out var id) ? id : action.Fixture ?? alias;
            }
        }
        else if (!string.IsNullOrWhiteSpace(action.Driver) && sessions.TryGetValue(action.Driver, out var session))
        {
            result = await session.ExecuteAsync(action, executionContext, cancellationToken);
            result = await MaybeCaptureScreenshotAsync(action, result, executionContext, session, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(action.Plugin))
        {
            result = await _pluginHost.InvokeStepAsync(catalog, action, executionContext, cancellationToken);
        }
        else
        {
            result = new DriverExecutionResult
            {
                Outcome = RunOutcome.Passed,
                Message = $"No execution backend was required for '{action.Name}'."
            };
        }

        return result;
    }

    private static async Task<DriverExecutionResult> MaybeCaptureScreenshotAsync(PlanAction action, DriverExecutionResult result, FlowExecutionContext context, IDriverSession session, CancellationToken cancellationToken)
    {
        var policy = context.EffectiveConfig.Profile.Evidence?.ScreenshotPolicy;

        // HTTP driver and other non-UI drivers do not support screenshots.
        if (!string.Equals(session.Name, "flaui", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        if (string.Equals(policy, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy, "never", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        var isFailed = result.Outcome != RunOutcome.Passed;
        var isAssertionFailure = isFailed
            && string.Equals(result.FailureClassification, "assertion-failed", StringComparison.OrdinalIgnoreCase);

        var shouldCapture = string.Equals(policy, "every-step", StringComparison.OrdinalIgnoreCase)
            || (isFailed && string.Equals(policy, "on-failure", StringComparison.OrdinalIgnoreCase))
            || (isAssertionFailure && string.Equals(policy, "on-assertion-failure", StringComparison.OrdinalIgnoreCase));

        if (!shouldCapture)
        {
            return result;
        }

        if (action.Operation?.Contains("screenshot", StringComparison.OrdinalIgnoreCase) == true
            || action.Name.Contains("screenshot", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        try
        {
            var screenshotAction = action with
            {
                Name = $"{action.Name}.auto-capture",
                Operation = "capture-screenshot",
                Inputs = new Dictionary<string, string>(action.Inputs, StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = action.Name
                }
            };
            var capture = await session.ExecuteAsync(screenshotAction, context, cancellationToken);
            return capture.Outcome != RunOutcome.Passed || capture.Artifacts.Count == 0
                ? result
                : result with { Artifacts = result.Artifacts.Concat(capture.Artifacts).ToList() };
        }
        catch
        {
            return result;
        }
    }

    private static bool MatchesRequestedStart(PlanAction action, string? requestedStart)
    {
        if (string.IsNullOrWhiteSpace(requestedStart))
        {
            return true;
        }

        return string.Equals(action.Name, requestedStart, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action.Step, requestedStart, StringComparison.OrdinalIgnoreCase)
            || string.Equals($"{action.Kind}:{action.Name}", requestedStart, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFailureSummary(RunResult result)
    {
        var failed = result.Flows.Where(flow => flow.Outcome is RunOutcome.Failed or RunOutcome.Errored).ToList();
        if (failed.Count == 0)
        {
            return "All flows passed.";
        }

        return string.Join(Environment.NewLine, failed.Select(flow =>
            $"- {flow.FlowId}: {flow.FailureClassification} - {flow.FailureMessage}"));
    }

    private sealed class DictionaryDriverAccessor : IDriverAccessor
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _metadata;

        public DictionaryDriverAccessor(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> metadata)
        {
            _metadata = metadata;
        }

        public bool TryGetMetadata(string driverName, out IReadOnlyDictionary<string, string>? metadata)
            => _metadata.TryGetValue(driverName, out metadata);

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Snapshot() => _metadata;
    }

    private sealed class CollectingLogger : ICressLogger
    {
        private readonly EvidenceStore _evidenceStore;
        private readonly string _flowId;
        private readonly List<string> _entries = [];

        public CollectingLogger(EvidenceStore evidenceStore, string flowId)
        {
            _evidenceStore = evidenceStore;
            _flowId = flowId;
        }

        public void Info(string message, IReadOnlyDictionary<string, string>? data = null)
            => Write("INFO", message, data);

        public void Warning(string message, IReadOnlyDictionary<string, string>? data = null)
            => Write("WARN", message, data);

        public void Error(string message, IReadOnlyDictionary<string, string>? data = null)
            => Write("ERROR", message, data);

        private void Write(string level, string message, IReadOnlyDictionary<string, string>? data)
        {
            _entries.Add($"{DateTimeOffset.UtcNow:o} {level} {message}");
            _evidenceStore.WriteText(Path.Combine("logs", $"{EvidenceStore.SanitizeFileName(_flowId)}.log"), string.Join(Environment.NewLine, _entries), "logs", "Flow log");
        }
    }
}
