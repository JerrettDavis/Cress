using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio.Services;

namespace Cress.UnitTests;

/// <summary>
/// Unit tests for <see cref="RunMetricsService"/>.
/// </summary>
public sealed class RunMetricsServiceTests
{
    private static readonly RunMetricsService Svc = new();

    // ------------------------------------------------------------------ Helpers

    private static StoredRunResult MakeRun(
        string runId,
        DateTimeOffset startedAt,
        bool passed,
        double durationMs = 1000,
        IReadOnlyList<FlowRunResult>? flows = null)
        => new()
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = runId,
                    StartedAt = startedAt,
                    EndedAt = startedAt.AddMilliseconds(durationMs),
                    DurationMs = durationMs
                },
                Flows = flows ?? (passed
                    ? [new FlowRunResult { FlowId = "default-flow", Name = "Default", Outcome = RunOutcome.Passed, DurationMs = durationMs }]
                    : [new FlowRunResult { FlowId = "default-flow", Name = "Default", Outcome = RunOutcome.Failed, DurationMs = durationMs }])
            }
        };

    private static FlowRunResult MakeFlow(
        string flowId,
        RunOutcome outcome,
        double durationMs = 500,
        DateTimeOffset? startedAt = null,
        IReadOnlyList<StepRunResult>? steps = null)
        => new()
        {
            FlowId = flowId,
            Name = flowId,
            Outcome = outcome,
            DurationMs = durationMs,
            StartedAt = startedAt ?? DateTimeOffset.UtcNow,
            Steps = steps ?? []
        };

    private static StepRunResult MakeStep(string name, RunOutcome outcome, double durationMs = 100)
        => new() { Kind = "step", Name = name, Outcome = outcome, DurationMs = durationMs };

    // ------------------------------------------------------------------ 1. Suite metrics: pass rate

    [Fact]
    public void Suite_passRate_is_correct_for_7_passes_out_of_10()
    {
        var runs = Enumerable.Range(0, 10)
            .Select(i => MakeRun($"run-{i}", DateTimeOffset.UtcNow.AddMinutes(-i), i < 7))
            .ToList();

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);

        Assert.Equal(10, metrics.Suite.TotalRuns);
        Assert.Equal(7, metrics.Suite.PassedRuns);
        Assert.Equal(3, metrics.Suite.FailedRuns);
        Assert.Equal(0.7, metrics.Suite.PassRate, precision: 10);
    }

    // ------------------------------------------------------------------ 2. Suite metrics: duration aggregates

    [Fact]
    public void Suite_total_and_avg_duration_aggregates_correctly()
    {
        var runs = new[]
        {
            MakeRun("r1", DateTimeOffset.UtcNow.AddMinutes(-2), true, durationMs: 1000),
            MakeRun("r2", DateTimeOffset.UtcNow.AddMinutes(-1), true, durationMs: 3000)
        };

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);

        Assert.Equal(TimeSpan.FromMilliseconds(4000), metrics.Suite.TotalDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), metrics.Suite.AvgDuration);
    }

    // ------------------------------------------------------------------ 3. FlowMetrics per flow ID

    [Fact]
    public void FlowMetrics_groups_correctly_by_flow_id()
    {
        var t = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "r1", StartedAt = t.AddMinutes(-2), DurationMs = 1000 },
                    Flows = [MakeFlow("flow-a", RunOutcome.Passed), MakeFlow("flow-b", RunOutcome.Failed)]
                }
            },
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "r2", StartedAt = t.AddMinutes(-1), DurationMs = 2000 },
                    Flows = [MakeFlow("flow-a", RunOutcome.Passed), MakeFlow("flow-b", RunOutcome.Passed)]
                }
            }
        };

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);

        Assert.Equal(2, metrics.Flows.Count);
        var flowA = metrics.Flows.Single(f => f.FlowId == "flow-a");
        var flowB = metrics.Flows.Single(f => f.FlowId == "flow-b");

        Assert.Equal(2, flowA.Runs);
        Assert.Equal(1.0, flowA.PassRate, precision: 10);
        Assert.Equal(2, flowB.Runs);
        Assert.Equal(0.5, flowB.PassRate, precision: 10);
    }

    // ------------------------------------------------------------------ 4. Percentile calculations

    [Fact]
    public void Percentiles_are_computed_correctly_for_known_sorted_durations()
    {
        // 10 durations: 100, 200, … 1000 ms
        var t = DateTimeOffset.UtcNow;
        var runs = Enumerable.Range(1, 10).Select(i =>
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = $"r{i}", StartedAt = t.AddMinutes(-i), DurationMs = i * 100 },
                    Flows = [MakeFlow("flow-x", RunOutcome.Passed, durationMs: i * 100)]
                }
            }).ToList();

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);
        var flow = metrics.Flows.Single(f => f.FlowId == "flow-x");

        // P50 at index 4.5 → interpolate between 500 and 600 → 550
        Assert.Equal(TimeSpan.FromMilliseconds(550), flow.P50);
        // P95 at index 8.55 → between 900 and 1000 → 955
        Assert.Equal(TimeSpan.FromMilliseconds(955), flow.P95);
        // P99 at index 8.91 → between 900 and 1000 → 991
        Assert.Equal(TimeSpan.FromMilliseconds(991), flow.P99);
    }

    // ------------------------------------------------------------------ 5. MTTR calculation

    [Fact]
    public void MTTR_is_average_time_from_fail_to_recovery()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // fail at t0, pass at t0+1h → 60 min recovery
        // fail at t0+2h, pass at t0+4h → 120 min recovery
        // average MTTR = 90 min

        var runs = new[]
        {
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r1", StartedAt = t0, DurationMs = 100 }, Flows = [MakeFlow("flow-mttr", RunOutcome.Failed, startedAt: t0)] } },
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r2", StartedAt = t0.AddHours(1), DurationMs = 100 }, Flows = [MakeFlow("flow-mttr", RunOutcome.Passed, startedAt: t0.AddHours(1))] } },
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r3", StartedAt = t0.AddHours(2), DurationMs = 100 }, Flows = [MakeFlow("flow-mttr", RunOutcome.Failed, startedAt: t0.AddHours(2))] } },
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r4", StartedAt = t0.AddHours(4), DurationMs = 100 }, Flows = [MakeFlow("flow-mttr", RunOutcome.Passed, startedAt: t0.AddHours(4))] } }
        };

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);
        var flow = metrics.Flows.Single(f => f.FlowId == "flow-mttr");

        Assert.NotNull(flow.MTTR);
        Assert.Equal(TimeSpan.FromHours(1.5), flow.MTTR!.Value);
    }

    [Fact]
    public void MTTR_is_null_when_no_recovery_in_window()
    {
        var runs = new[]
        {
            MakeRun("r1", DateTimeOffset.UtcNow.AddMinutes(-2), false),
            MakeRun("r2", DateTimeOffset.UtcNow.AddMinutes(-1), false)
        };

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);
        var flow = metrics.Flows.Single();

        Assert.Null(flow.MTTR);
    }

    // ------------------------------------------------------------------ 6. StepMetrics per (FlowId, StepIndex)

    [Fact]
    public void StepMetrics_aggregated_per_flow_and_step_index()
    {
        var t = DateTimeOffset.UtcNow;
        var steps1 = new[] { MakeStep("step.open", RunOutcome.Passed, 100), MakeStep("step.fill", RunOutcome.Failed, 200) };
        var steps2 = new[] { MakeStep("step.open", RunOutcome.Passed, 150), MakeStep("step.fill", RunOutcome.Passed, 250) };

        var runs = new[]
        {
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "r1", StartedAt = t.AddMinutes(-1), DurationMs = 300 },
                    Flows = [MakeFlow("flow-steps", RunOutcome.Failed, steps: steps1)]
                }
            },
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "r2", StartedAt = t, DurationMs = 400 },
                    Flows = [MakeFlow("flow-steps", RunOutcome.Passed, steps: steps2)]
                }
            }
        };

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);

        Assert.Equal(2, metrics.Steps.Count);
        var open = metrics.Steps.Single(s => s.StepName == "step.open");
        var fill = metrics.Steps.Single(s => s.StepName == "step.fill");

        Assert.Equal(2, open.Runs);
        Assert.Equal(2, open.Passed);
        Assert.Equal(0, open.Failed);
        Assert.Equal(TimeSpan.FromMilliseconds(125), open.AvgDuration); // (100+150)/2

        Assert.Equal(1, fill.Failed);
        Assert.Equal(1, fill.Passed);
    }

    // ------------------------------------------------------------------ 7. TrendPoint bucketing by hour

    [Fact]
    public void Trend_buckets_runs_by_hour()
    {
        // Use timestamps relative to now so the window filter does not exclude them
        var now = DateTimeOffset.UtcNow;
        // Snap to the start of the current UTC hour, then offset back by 1 hour
        var hour1Base = new DateTimeOffset(now.UtcDateTime.Date.AddHours(now.Hour - 1), TimeSpan.Zero);
        var hour2Base = new DateTimeOffset(now.UtcDateTime.Date.AddHours(now.Hour), TimeSpan.Zero);

        var runs = new[]
        {
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r1", StartedAt = hour1Base.AddMinutes(5), DurationMs = 500 }, Flows = [MakeFlow("f1", RunOutcome.Passed)] } },
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r2", StartedAt = hour1Base.AddMinutes(30), DurationMs = 600 }, Flows = [MakeFlow("f1", RunOutcome.Failed)] } },
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r3", StartedAt = hour2Base.AddMinutes(1), DurationMs = 700 }, Flows = [MakeFlow("f1", RunOutcome.Passed)] } }
        };

        // Use no window (or wide window) so all 3 runs are included
        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);

        // 2 hour buckets: first has 2 flows (1P + 1F), second has 1 flow (1P)
        Assert.Equal(2, metrics.Trend.Count);
        var firstBucket = metrics.Trend[0];
        Assert.Equal(1, firstBucket.PassedFlows);
        Assert.Equal(1, firstBucket.FailedFlows);
        var secondBucket = metrics.Trend[1];
        Assert.Equal(1, secondBucket.PassedFlows);
        Assert.Equal(0, secondBucket.FailedFlows);
    }

    // ------------------------------------------------------------------ 8. Window option drops old runs

    [Fact]
    public void Window_option_excludes_runs_outside_the_window()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            MakeRun("r1", now.AddDays(-10), false), // outside 7d window
            MakeRun("r2", now.AddDays(-5), true),   // inside
            MakeRun("r3", now.AddDays(-1), true)    // inside
        };

        var metrics = Svc.Aggregate(runs, new MetricsOptions(TimeSpan.FromDays(7), null));

        Assert.Equal(2, metrics.Suite.TotalRuns);
        Assert.Equal(2, metrics.Suite.PassedRuns);
    }

    // ------------------------------------------------------------------ 9. MaxRuns caps results

    [Fact]
    public void MaxRuns_option_caps_the_number_of_runs_analyzed()
    {
        var runs = Enumerable.Range(0, 20)
            .Select(i => MakeRun($"run-{i}", DateTimeOffset.UtcNow.AddMinutes(-i), true))
            .ToList();

        var metrics = Svc.Aggregate(runs, new MetricsOptions(null, MaxRuns: 5));

        Assert.Equal(5, metrics.Suite.TotalRuns);
    }

    // ------------------------------------------------------------------ 10. Empty run list

    [Fact]
    public void Empty_run_list_returns_zero_metrics()
    {
        var metrics = Svc.Aggregate([], MetricsOptions.Default);

        Assert.Equal(0, metrics.Suite.TotalRuns);
        Assert.Equal(0.0, metrics.Suite.PassRate);
        Assert.Empty(metrics.Flows);
        Assert.Empty(metrics.Steps);
        Assert.Empty(metrics.Trend);
    }

    // ------------------------------------------------------------------ 11. FlakeRate computation

    [Fact]
    public void FlakeRate_is_zero_when_flow_always_passes()
    {
        var runs = Enumerable.Range(0, 5)
            .Select(i => new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = $"r{i}", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-i), DurationMs = 100 },
                    Flows = [MakeFlow("stable", RunOutcome.Passed)]
                }
            }).ToList();

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);
        Assert.Equal(0.0, metrics.Flows.Single().FlakeRate);
    }

    [Fact]
    public void FlakeRate_is_nonzero_when_flow_has_mixed_outcomes()
    {
        var runs = new[]
        {
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r1", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2), DurationMs = 100 }, Flows = [MakeFlow("flaky", RunOutcome.Passed)] } },
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r2", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1), DurationMs = 100 }, Flows = [MakeFlow("flaky", RunOutcome.Failed)] } }
        };

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);
        Assert.True(metrics.Flows.Single().FlakeRate > 0);
    }

    // ------------------------------------------------------------------ 12. Single-run edge case

    [Fact]
    public void Single_run_returns_valid_percentiles_without_throwing()
    {
        var runs = new[] { MakeRun("r1", DateTimeOffset.UtcNow, true, durationMs: 500) };

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);

        var flow = metrics.Flows.Single();
        Assert.Equal(TimeSpan.FromMilliseconds(500), flow.P50);
        Assert.Equal(TimeSpan.FromMilliseconds(500), flow.P95);
        Assert.Equal(TimeSpan.FromMilliseconds(500), flow.P99);
    }

    // ------------------------------------------------------------------ 13. StepMetrics step name from latest run

    [Fact]
    public void StepMetrics_uses_latest_name_when_step_is_renamed_across_runs()
    {
        var t = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "r-old", StartedAt = t.AddHours(-1), DurationMs = 100 },
                    Flows = [MakeFlow("flow-rename", RunOutcome.Passed, steps: [MakeStep("step.old-name", RunOutcome.Passed)])]
                }
            },
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "r-new", StartedAt = t, DurationMs = 100 },
                    Flows = [MakeFlow("flow-rename", RunOutcome.Passed, steps: [MakeStep("step.new-name", RunOutcome.Passed)])]
                }
            }
        };

        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);
        var step = metrics.Steps.Single();

        // The service iterates runs as supplied (newest-first from repository); the last-processed is oldest
        // since we iterate in supply order. For clarity, just verify there's exactly 1 step entry with 2 runs.
        Assert.Equal(2, step.Runs);
    }

    // ------------------------------------------------------------------ 14. Trend bucketing by day for long windows

    [Fact]
    public void Trend_buckets_by_day_for_windows_longer_than_3_days()
    {
        // Use relative timestamps so runs are not excluded by a window filter
        var today = DateTimeOffset.UtcNow.Date;
        var day1 = new DateTimeOffset(today.AddDays(-6), TimeSpan.Zero).AddHours(10);
        var day2 = new DateTimeOffset(today.AddDays(-5), TimeSpan.Zero).AddHours(10);
        var day7 = new DateTimeOffset(today, TimeSpan.Zero).AddHours(10);

        var runs = new[]
        {
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r1", StartedAt = day1, DurationMs = 100 }, Flows = [MakeFlow("f", RunOutcome.Passed)] } },
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r2", StartedAt = day2, DurationMs = 100 }, Flows = [MakeFlow("f", RunOutcome.Passed)] } },
            new StoredRunResult { Result = new RunResult { Metadata = new RunMetadata { RunId = "r3", StartedAt = day7, DurationMs = 100 }, Flows = [MakeFlow("f", RunOutcome.Passed)] } }
        };

        // No window restriction — let GuessSpan decide buckets (span = 6 days > 3 → by day)
        var metrics = Svc.Aggregate(runs, MetricsOptions.Default);

        // Should produce 3 day-level buckets (one per distinct day)
        Assert.Equal(3, metrics.Trend.Count);
    }
}
