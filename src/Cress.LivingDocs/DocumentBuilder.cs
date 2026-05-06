using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Studio.Services;

namespace Cress.LivingDocs;

/// <summary>
/// Assembles a <see cref="DocumentModel"/> by pulling data from the project's run history,
/// flow definitions, and metrics service.
/// </summary>
public sealed class DocumentBuilder
{
    private readonly RunResultRepository _repository;
    private readonly RunMetricsService _metricsService;
    private readonly FlowParser _flowParser;
    private readonly ConfigLoader _configLoader;

    public DocumentBuilder(
        RunResultRepository repository,
        RunMetricsService metricsService,
        FlowParser flowParser,
        ConfigLoader configLoader)
    {
        _repository = repository;
        _metricsService = metricsService;
        _flowParser = flowParser;
        _configLoader = configLoader;
    }

    public Task<DocumentModel> BuildAsync(DocumentBuildOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Load project config to find artifact/report paths
        var configResult = _configLoader.Load(options.ProjectPath);
        var config = configResult.Value;
        var artifactsRelPath = config?.Paths.Artifacts ?? ".cress/artifacts";

        // Load run history
        var storedRuns = _repository.ListRuns(options.ProjectPath, artifactsRelPath, options.MaxRecentRuns * 2);

        // Compute metrics
        RunMetrics? metrics = null;
        if (storedRuns.Count > 0)
        {
            metrics = _metricsService.Aggregate(storedRuns, new MetricsOptions(null, options.MaxRecentRuns));
        }

        // Build recent run history entries
        var recentRuns = storedRuns
            .Take(options.MaxRecentRuns)
            .Select(r => new RunHistoryEntry(
                r.Result.Metadata.RunId,
                r.Result.Metadata.StartedAt,
                r.Result.Flows.Count(f => f.Outcome == RunOutcome.Passed),
                r.Result.Flows.Count(f => f.Outcome != RunOutcome.Passed)))
            .ToList();

        // Build flow summaries from metrics (if available) or fall back to latest run
        var flowSummaries = BuildFlowSummaries(metrics, storedRuns);

        // Build suite summary
        var suite = BuildSuiteSummary(metrics, storedRuns, flowSummaries);

        // Collect screenshot evidence from artifact directories
        var screenshots = CollectScreenshots(storedRuns, options.MaxScreenshots);

        // Collect diagnostics from the latest run
        var diagnostics = CollectDiagnostics(storedRuns);

        // Meta
        var meta = new DocumentMeta(
            ProjectName: config?.Project.Name is { Length: > 0 } n ? n : System.IO.Path.GetFileName(options.ProjectPath),
            GeneratedAt: DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            Version: "1.0",
            GitSha: options.GitSha);

        var model = new DocumentModel(
            Meta: meta,
            Branding: options.Branding,
            Suite: suite,
            Flows: flowSummaries,
            Metrics: metrics,
            RecentRuns: recentRuns,
            Screenshots: screenshots,
            Diagnostics: diagnostics);

        return Task.FromResult(model);
    }

    // ------------------------------------------------------------------ Helpers

    private static IReadOnlyList<FlowSummary> BuildFlowSummaries(
        RunMetrics? metrics,
        IReadOnlyList<StoredRunResult> runs)
    {
        if (metrics is not null && metrics.Flows.Count > 0)
        {
            return metrics.Flows
                .Select(f => new FlowSummary(
                    Id: f.FlowId,
                    Name: ResolveFlowName(f.FlowId, runs),
                    Description: null,
                    Status: f.PassRate >= 1.0 ? "passing" : f.PassRate == 0.0 ? "failing" : "flaky",
                    PassRate: f.PassRate,
                    AvgDuration: f.AvgDuration))
                .ToList();
        }

        // Fall back: use the most recent run
        if (runs.Count == 0)
        {
            return [];
        }

        var latestRun = runs[0];
        return latestRun.Result.Flows
            .Select(f => new FlowSummary(
                Id: f.FlowId,
                Name: f.Name,
                Description: null,
                Status: f.Outcome == RunOutcome.Passed ? "passing" : "failing",
                PassRate: f.Outcome == RunOutcome.Passed ? 1.0 : 0.0,
                AvgDuration: TimeSpan.FromMilliseconds(f.DurationMs)))
            .ToList();
    }

    private static SuiteSummary BuildSuiteSummary(
        RunMetrics? metrics,
        IReadOnlyList<StoredRunResult> runs,
        IReadOnlyList<FlowSummary> flows)
    {
        if (metrics is not null)
        {
            return new SuiteSummary(
                TotalFlows: flows.Count,
                PassedRuns: metrics.Suite.PassedRuns,
                FailedRuns: metrics.Suite.FailedRuns,
                PassRate: metrics.Suite.PassRate,
                AvgDuration: metrics.Suite.AvgDuration);
        }

        if (runs.Count == 0)
        {
            return new SuiteSummary(0, 0, 0, 0.0, TimeSpan.Zero);
        }

        var latest = runs[0].Result;
        var passed = latest.Flows.Count(f => f.Outcome == RunOutcome.Passed);
        var failed = latest.Flows.Count - passed;
        var passRate = latest.Flows.Count == 0 ? 0.0 : (double)passed / latest.Flows.Count;
        var avgMs = latest.Flows.Count == 0 ? 0 : latest.Flows.Average(f => f.DurationMs);
        return new SuiteSummary(latest.Flows.Count, passed, failed, passRate, TimeSpan.FromMilliseconds(avgMs));
    }

    private static IReadOnlyList<ScreenshotEvidence> CollectScreenshots(
        IReadOnlyList<StoredRunResult> runs,
        int maxScreenshots)
    {
        var screenshots = new List<ScreenshotEvidence>();

        foreach (var run in runs)
        {
            if (screenshots.Count >= maxScreenshots) break;

            foreach (var flow in run.Result.Flows)
            {
                foreach (var step in flow.Steps)
                {
                    foreach (var artifact in step.Artifacts)
                    {
                        if (artifact.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                            || artifact.RelativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            || artifact.RelativePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                        {
                            var fullPath = System.IO.Path.Combine(run.ArtifactDirectory, artifact.RelativePath);
                            screenshots.Add(new ScreenshotEvidence(flow.FlowId, step.Name, fullPath));

                            if (screenshots.Count >= maxScreenshots) break;
                        }
                    }
                    if (screenshots.Count >= maxScreenshots) break;
                }
                if (screenshots.Count >= maxScreenshots) break;
            }
        }

        return screenshots;
    }

    private static IReadOnlyList<DiagnosticEntry> CollectDiagnostics(IReadOnlyList<StoredRunResult> runs)
    {
        if (runs.Count == 0) return [];

        return runs[0].Result.Diagnostics
            .Select(d => new DiagnosticEntry(
                Severity: d.Severity.ToString(),
                FlowId: d.File ?? string.Empty,
                Message: d.Message))
            .ToList();
    }

    private static string ResolveFlowName(string flowId, IReadOnlyList<StoredRunResult> runs)
    {
        foreach (var run in runs)
        {
            var flow = run.Result.Flows.FirstOrDefault(f =>
                string.Equals(f.FlowId, flowId, StringComparison.OrdinalIgnoreCase));
            if (flow is not null && !string.IsNullOrWhiteSpace(flow.Name))
            {
                return flow.Name;
            }
        }

        return flowId;
    }
}
