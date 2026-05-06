using Cress.Core.Models;
using Cress.Execution;

namespace Cress.Studio.Services;

public sealed class StudioRunInsightsService
{
    public StudioRunInsights Analyze(ProjectCatalog catalog, IReadOnlyList<StoredRunResult> runs)
        => Analyze(catalog, runs, new FlakeConfig());

    public StudioRunInsights Analyze(ProjectCatalog catalog, IReadOnlyList<StoredRunResult> runs, FlakeConfig flakeConfig)
    {
        var health = BuildFlowHealth(runs, flakeConfig);
        return new StudioRunInsights
        {
            TotalRunCount = runs.Count,
            PassingRunCount = runs.Count(run => run.Result.Passed),
            RecentActivity = runs.Take(8).Select(run => new StudioRecentActivityItem
            {
                Timestamp = run.Result.Metadata.StartedAt,
                Headline = $"{run.Result.Metadata.RunId} • {run.Result.Flows.Count(flow => flow.Outcome == RunOutcome.Passed)}/{run.Result.Flows.Count} passed",
                Detail = $"{run.Result.Metadata.Profile} • {run.Result.Flows.Count(flow => flow.PassedWithRetry)} recovered after retry",
                Outcome = run.Result.Passed ? RunOutcome.Passed : RunOutcome.Failed
            }).ToList(),
            FlowHealth = health,
            FlakyFlows = health.Where(item => item.IsFlaky).OrderByDescending(item => item.FlakeScore).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            LatestComparison = Compare(runs.FirstOrDefault(), runs.Skip(1).FirstOrDefault())
        };
    }

    public StudioRunComparison Compare(StoredRunResult? current, StoredRunResult? previous)
    {
        if (current is null)
        {
            return new StudioRunComparison { Summary = "No runs yet." };
        }

        if (previous is null)
        {
            return new StudioRunComparison
            {
                CurrentRunId = current.Result.Metadata.RunId,
                Summary = "No previous run available for comparison."
            };
        }

        var previousFlows = previous.Result.Flows.ToDictionary(flow => flow.FlowId, StringComparer.OrdinalIgnoreCase);
        var items = current.Result.Flows
            .Select(flow =>
            {
                previousFlows.TryGetValue(flow.FlowId, out var previousFlow);
                return new StudioRunComparisonItem
                {
                    FlowId = flow.FlowId,
                    Name = flow.Name,
                    CurrentOutcome = flow.Outcome,
                    PreviousOutcome = previousFlow?.Outcome,
                    DurationDeltaMs = previousFlow is null ? null : flow.DurationMs - previousFlow.DurationMs,
                    PassedWithRetry = flow.PassedWithRetry
                };
            })
            .Where(item => item.PreviousOutcome != item.CurrentOutcome || item.PassedWithRetry || item.DurationDeltaMs is not null && Math.Abs(item.DurationDeltaMs.Value) >= 100)
            .OrderByDescending(item => item.PreviousOutcome != item.CurrentOutcome)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StudioRunComparison
        {
            CurrentRunId = current.Result.Metadata.RunId,
            PreviousRunId = previous.Result.Metadata.RunId,
            Summary = $"{items.Count(item => item.PreviousOutcome == RunOutcome.Passed && item.CurrentOutcome != RunOutcome.Passed)} regression(s), {items.Count(item => item.PreviousOutcome is RunOutcome.Failed or RunOutcome.Errored && item.CurrentOutcome == RunOutcome.Passed)} recovery(s), {items.Count} notable change(s).",
            Items = items
        };
    }

    private static IReadOnlyList<StudioFlowHealthItem> BuildFlowHealth(IReadOnlyList<StoredRunResult> runs, FlakeConfig flakeConfig)
        => runs.SelectMany(run => run.Result.Flows.Select(flow => new { run.Result.Metadata.StartedAt, Flow = flow }))
            .GroupBy(item => item.Flow.FlowId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                // Apply configurable window: take only the most recent N runs for this flow
                var ordered = group.OrderByDescending(item => item.StartedAt).Take(flakeConfig.Window).ToList();
                var outcomes = ordered.Select(item => item.Flow.Outcome).ToList();
                var transitions = outcomes.Zip(outcomes.Skip(1)).Count(pair => pair.First != pair.Second);
                var passedCount = outcomes.Count(outcome => outcome == RunOutcome.Passed);
                var failedCount = outcomes.Count(outcome => outcome is RunOutcome.Failed or RunOutcome.Errored);
                var retryRecoveries = ordered.Count(item => item.Flow.PassedWithRetry);
                var flakeScore = Math.Min(100, transitions * 25 + retryRecoveries * 20 + (passedCount > 0 && failedCount > 0 ? 20 : 0));

                // Apply configurable flake criteria
                var failRate = ordered.Count == 0 ? 0d : (double)failedCount / ordered.Count;
                var isFlaky = flakeScore >= 20
                    && passedCount >= flakeConfig.MinPasses
                    && failedCount >= flakeConfig.MinFails
                    && failRate >= flakeConfig.Threshold;

                // Per-step instability breakdown within the window
                var stepBreakdown = BuildStepBreakdown(ordered.Select(item => item.Flow).ToList());

                return new StudioFlowHealthItem
                {
                    FlowId = group.Key,
                    Name = ordered[0].Flow.Name,
                    CapabilityId = ordered[0].Flow.CapabilityId,
                    LatestOutcome = ordered[0].Flow.Outcome,
                    PassRate = ordered.Count == 0 ? 0 : Math.Round((double)passedCount / ordered.Count * 100, 1),
                    FailureCount = failedCount,
                    RetryRecoveries = retryRecoveries,
                    OutcomeTransitions = transitions,
                    FlakeScore = flakeScore,
                    IsFlaky = isFlaky,
                    Trend = string.Join(" ", outcomes.Take(6).Select(outcome => outcome switch
                    {
                        RunOutcome.Passed => "P",
                        RunOutcome.Failed => "F",
                        RunOutcome.Errored => "E",
                        RunOutcome.Skipped => "S",
                        RunOutcome.Blocked => "B",
                        _ => "?"
                    })),
                    LastFailureMessage = ordered.Select(item => item.Flow.FailureMessage).FirstOrDefault(message => !string.IsNullOrWhiteSpace(message)),
                    StepBreakdown = stepBreakdown
                };
            })
            .OrderByDescending(item => item.FlakeScore)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<StepInstability> BuildStepBreakdown(IReadOnlyList<FlowRunResult> flowRuns)
    {
        // Collect all steps across the window runs, grouped by step index then by name
        var stepData = new Dictionary<int, (string Name, int TotalCount, int FailCount)>();

        foreach (var flow in flowRuns)
        {
            for (var i = 0; i < flow.Steps.Count; i++)
            {
                var step = flow.Steps[i];
                stepData.TryGetValue(i, out var existing);
                var isFailed = step.Outcome is RunOutcome.Failed or RunOutcome.Errored;
                stepData[i] = (
                    step.Name,
                    existing.TotalCount + 1,
                    existing.FailCount + (isFailed ? 1 : 0)
                );
            }
        }

        return stepData
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var (name, total, fails) = kv.Value;
                return new StepInstability
                {
                    StepIndex = kv.Key,
                    StepName = name,
                    FailCount = fails,
                    TotalCount = total,
                    FlakeRate = total == 0 ? 0d : Math.Round((double)fails / total * 100, 1)
                };
            })
            .Where(s => s.TotalCount > 0)
            .ToList();
    }
}

public sealed record StudioRunInsights
{
    public int TotalRunCount { get; init; }
    public int PassingRunCount { get; init; }
    public IReadOnlyList<StudioRecentActivityItem> RecentActivity { get; init; } = [];
    public IReadOnlyList<StudioFlowHealthItem> FlowHealth { get; init; } = [];
    public IReadOnlyList<StudioFlowHealthItem> FlakyFlows { get; init; } = [];
    public StudioRunComparison LatestComparison { get; init; } = new();
}

public sealed record StudioRecentActivityItem
{
    public DateTimeOffset Timestamp { get; init; }
    public string Headline { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public RunOutcome Outcome { get; init; }
}

public sealed record StudioFlowHealthItem
{
    public string FlowId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? CapabilityId { get; init; }
    public RunOutcome LatestOutcome { get; init; }
    public double PassRate { get; init; }
    public int FailureCount { get; init; }
    public int RetryRecoveries { get; init; }
    public int OutcomeTransitions { get; init; }
    public int FlakeScore { get; init; }
    public bool IsFlaky { get; init; }
    public string Trend { get; init; } = string.Empty;
    public string? LastFailureMessage { get; init; }
    public IReadOnlyList<StepInstability> StepBreakdown { get; init; } = [];
}

public sealed record StepInstability
{
    public int StepIndex { get; init; }
    public string StepName { get; init; } = string.Empty;
    public int FailCount { get; init; }
    public int TotalCount { get; init; }
    /// <summary>Failure rate expressed as a percentage (0–100), rounded to 1 decimal place.</summary>
    public double FlakeRate { get; init; }
}

public sealed record StudioRunComparison
{
    public string? CurrentRunId { get; init; }
    public string? PreviousRunId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<StudioRunComparisonItem> Items { get; init; } = [];
}

public sealed record StudioRunComparisonItem
{
    public string FlowId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public RunOutcome CurrentOutcome { get; init; }
    public RunOutcome? PreviousOutcome { get; init; }
    public double? DurationDeltaMs { get; init; }
    public bool PassedWithRetry { get; init; }
}
