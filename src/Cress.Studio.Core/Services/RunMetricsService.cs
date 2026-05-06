using Cress.Core.Models;
using Cress.Execution;

namespace Cress.Studio.Services;

/// <summary>
/// Aggregates run history into structured metrics: suite pass rates, per-flow percentiles,
/// per-step timing, MTTR, and time-bucketed trend points.
/// </summary>
public sealed class RunMetricsService
{
    public RunMetrics Aggregate(IReadOnlyList<StoredRunResult> runs, MetricsOptions options)
    {
        // Apply window and maxRuns caps (newest first — runs are already newest-first from the repository)
        var filtered = runs.AsEnumerable();

        if (options.Window.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow - options.Window.Value;
            filtered = filtered.Where(r => r.Result.Metadata.StartedAt >= cutoff);
        }

        if (options.MaxRuns.HasValue)
        {
            filtered = filtered.Take(options.MaxRuns.Value);
        }

        var window = filtered.ToList();

        if (window.Count == 0)
        {
            return new RunMetrics(
                Suite: new SuiteMetrics(0, 0, 0, 0.0, TimeSpan.Zero, TimeSpan.Zero),
                Flows: [],
                Steps: [],
                Trend: []);
        }

        var suite = ComputeSuite(window);
        var flows = ComputeFlows(window);
        var steps = ComputeSteps(window);
        var trend = ComputeTrend(window, options.Window);

        return new RunMetrics(suite, flows, steps, trend);
    }

    // ------------------------------------------------------------------ Suite

    private static SuiteMetrics ComputeSuite(IReadOnlyList<StoredRunResult> runs)
    {
        var passed = runs.Count(r => r.Result.Passed);
        var failed = runs.Count - passed;
        var passRate = runs.Count == 0 ? 0.0 : (double)passed / runs.Count;
        var durations = runs.Select(r => r.Result.Metadata.DurationMs).ToList();
        var total = TimeSpan.FromMilliseconds(durations.Sum());
        var avg = TimeSpan.FromMilliseconds(runs.Count == 0 ? 0 : durations.Average());
        return new SuiteMetrics(runs.Count, passed, failed, passRate, total, avg);
    }

    // ------------------------------------------------------------------ Flows

    private static IReadOnlyList<FlowMetrics> ComputeFlows(IReadOnlyList<StoredRunResult> runs)
    {
        // Flatten to (startedAt, flow) pairs
        var allFlows = runs
            .SelectMany(r => r.Result.Flows.Select(f => (StartedAt: r.Result.Metadata.StartedAt, Flow: f)))
            .GroupBy(pair => pair.Flow.FlowId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return allFlows
            .Select(group =>
            {
                // Oldest first for chronological MTTR
                var ordered = group.OrderBy(pair => pair.StartedAt).ToList();
                var outcomes = ordered.Select(pair => pair.Flow.Outcome).ToList();
                var passed = outcomes.Count(o => o == RunOutcome.Passed);
                var failed = outcomes.Count - passed;
                var passRate = ordered.Count == 0 ? 0.0 : (double)passed / ordered.Count;

                // Flake rate: flows that have mixed outcomes / total flows in window
                var hasFlake = passed > 0 && failed > 0;
                var flakeRate = hasFlake ? (double)failed / ordered.Count : 0.0;

                // Duration percentiles
                var durations = ordered.Select(pair => pair.Flow.DurationMs).OrderBy(d => d).ToList();
                var avgDuration = TimeSpan.FromMilliseconds(durations.Count == 0 ? 0 : durations.Average());
                var p50 = Percentile(durations, 0.50);
                var p95 = Percentile(durations, 0.95);
                var p99 = Percentile(durations, 0.99);

                // MTTR: average time from failure to next pass
                var mttr = ComputeMttr(ordered.Select(pair => (pair.StartedAt, pair.Flow.Outcome)).ToList());

                var latest = ordered.Last();
                return new FlowMetrics(
                    FlowId: group.Key,
                    Runs: ordered.Count,
                    Passed: passed,
                    Failed: failed,
                    PassRate: passRate,
                    FlakeRate: flakeRate,
                    AvgDuration: avgDuration,
                    P50: p50,
                    P95: p95,
                    P99: p99,
                    MTTR: mttr);
            })
            .OrderBy(f => f.FlowId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static TimeSpan? ComputeMttr(IReadOnlyList<(DateTimeOffset StartedAt, RunOutcome Outcome)> chronological)
    {
        var recoveries = new List<double>();

        DateTimeOffset? firstFailAt = null;
        foreach (var (startedAt, outcome) in chronological)
        {
            var isFail = outcome is RunOutcome.Failed or RunOutcome.Errored;
            var isPass = outcome == RunOutcome.Passed;

            if (isFail && firstFailAt is null)
            {
                firstFailAt = startedAt;
            }
            else if (isPass && firstFailAt.HasValue)
            {
                recoveries.Add((startedAt - firstFailAt.Value).TotalMilliseconds);
                firstFailAt = null;
            }
            else if (isPass)
            {
                // reset — no failure in progress
                firstFailAt = null;
            }
        }

        return recoveries.Count == 0 ? null : TimeSpan.FromMilliseconds(recoveries.Average());
    }

    // ------------------------------------------------------------------ Steps

    private static IReadOnlyList<StepMetrics> ComputeSteps(IReadOnlyList<StoredRunResult> runs)
    {
        // Group by (flowId, stepIndex)
        var map = new Dictionary<(string FlowId, int StepIndex), StepAgg>(StringComparer.Ordinal.ToTuple());

        foreach (var run in runs)
        {
            foreach (var flow in run.Result.Flows)
            {
                for (var i = 0; i < flow.Steps.Count; i++)
                {
                    var step = flow.Steps[i];
                    var key = (flow.FlowId, i);
                    if (!map.TryGetValue(key, out var agg))
                    {
                        agg = new StepAgg(step.Name);
                        map[key] = agg;
                    }

                    // Always update name to latest
                    agg.LatestName = step.Name;
                    agg.Runs++;
                    if (step.Outcome == RunOutcome.Passed)
                        agg.Passed++;
                    else
                        agg.Failed++;
                    agg.Durations.Add(step.DurationMs);
                }
            }
        }

        return map
            .Select(kv =>
            {
                var agg = kv.Value;
                var sorted = agg.Durations.OrderBy(d => d).ToList();
                return new StepMetrics(
                    FlowId: kv.Key.FlowId,
                    StepIndex: kv.Key.StepIndex,
                    StepName: agg.LatestName,
                    Runs: agg.Runs,
                    Passed: agg.Passed,
                    Failed: agg.Failed,
                    AvgDuration: TimeSpan.FromMilliseconds(sorted.Count == 0 ? 0 : sorted.Average()),
                    P50: Percentile(sorted, 0.50),
                    P95: Percentile(sorted, 0.95),
                    P99: Percentile(sorted, 0.99));
            })
            .OrderBy(s => s.FlowId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.StepIndex)
            .ToList();
    }

    private sealed class StepAgg
    {
        public StepAgg(string name) { LatestName = name; }
        public string LatestName { get; set; }
        public int Runs { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public List<double> Durations { get; } = [];
    }

    // ------------------------------------------------------------------ Trend

    private static IReadOnlyList<TrendPoint> ComputeTrend(
        IReadOnlyList<StoredRunResult> runs,
        TimeSpan? window)
    {
        // Decide bucket granularity: bucket by hour for windows ≤ 3 days, else by day
        var totalSpan = window ?? GuessSpan(runs);
        var byDay = totalSpan > TimeSpan.FromDays(3);

        var buckets = runs
            .GroupBy(r => BucketKey(r.Result.Metadata.StartedAt, byDay))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var timestamp = g.Min(r => r.Result.Metadata.StartedAt);
                var allFlows = g.SelectMany(r => r.Result.Flows).ToList();
                var passedFlows = allFlows.Count(f => f.Outcome == RunOutcome.Passed);
                var failedFlows = allFlows.Count - passedFlows;
                var avgFlowMs = allFlows.Count == 0 ? 0 : allFlows.Average(f => f.DurationMs);
                return new TrendPoint(timestamp, passedFlows, failedFlows, TimeSpan.FromMilliseconds(avgFlowMs));
            })
            .ToList();

        return buckets;
    }

    private static string BucketKey(DateTimeOffset ts, bool byDay)
        => byDay
            ? ts.UtcDateTime.ToString("yyyy-MM-dd")
            : ts.UtcDateTime.ToString("yyyy-MM-dd-HH");

    private static TimeSpan GuessSpan(IReadOnlyList<StoredRunResult> runs)
    {
        if (runs.Count < 2)
        {
            return TimeSpan.FromDays(1);
        }

        var oldest = runs.Min(r => r.Result.Metadata.StartedAt);
        var newest = runs.Max(r => r.Result.Metadata.StartedAt);
        return newest - oldest;
    }

    // ------------------------------------------------------------------ Helpers

    private static TimeSpan Percentile(IReadOnlyList<double> sorted, double quantile)
    {
        if (sorted.Count == 0)
        {
            return TimeSpan.Zero;
        }

        if (sorted.Count == 1)
        {
            return TimeSpan.FromMilliseconds(sorted[0]);
        }

        var index = quantile * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return TimeSpan.FromMilliseconds(sorted[lower]);
        }

        var fraction = index - lower;
        var value = sorted[lower] * (1 - fraction) + sorted[upper] * fraction;
        return TimeSpan.FromMilliseconds(value);
    }
}

// Extension to allow (string, int) tuple as dictionary key with string OrdinalIgnoreCase comparison
file static class TupleKeyHelper
{
    public static IEqualityComparer<(string FlowId, int StepIndex)> ToTuple(this StringComparer _)
        => new FlowStepComparer();

    private sealed class FlowStepComparer : IEqualityComparer<(string FlowId, int StepIndex)>
    {
        public bool Equals((string FlowId, int StepIndex) x, (string FlowId, int StepIndex) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.FlowId, y.FlowId) && x.StepIndex == y.StepIndex;

        public int GetHashCode((string FlowId, int StepIndex) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FlowId), obj.StepIndex);
    }
}

// ------------------------------------------------------------------ Data model

/// <summary>Top-level metrics record returned by <see cref="RunMetricsService.Aggregate"/>.</summary>
public record RunMetrics(
    SuiteMetrics Suite,
    IReadOnlyList<FlowMetrics> Flows,
    IReadOnlyList<StepMetrics> Steps,
    IReadOnlyList<TrendPoint> Trend);

public record SuiteMetrics(
    int TotalRuns,
    int PassedRuns,
    int FailedRuns,
    double PassRate,
    TimeSpan TotalDuration,
    TimeSpan AvgDuration);

public record FlowMetrics(
    string FlowId,
    int Runs,
    int Passed,
    int Failed,
    double PassRate,
    double FlakeRate,
    TimeSpan AvgDuration,
    TimeSpan P50,
    TimeSpan P95,
    TimeSpan P99,
    TimeSpan? MTTR);

public record StepMetrics(
    string FlowId,
    int StepIndex,
    string StepName,
    int Runs,
    int Passed,
    int Failed,
    TimeSpan AvgDuration,
    TimeSpan P50,
    TimeSpan P95,
    TimeSpan P99);

public record TrendPoint(
    DateTimeOffset Timestamp,
    int PassedFlows,
    int FailedFlows,
    TimeSpan AvgFlowDuration);

/// <summary>Options for <see cref="RunMetricsService.Aggregate"/>.</summary>
public record MetricsOptions(TimeSpan? Window, int? MaxRuns)
{
    public static readonly MetricsOptions Default = new(null, null);
}
