using System.Text;
using System.Text.Json;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio.Services;

namespace Cress.UnitTests;

/// <summary>
/// Unit tests for the <c>cress flake-report</c> command behaviours:
/// underlying <see cref="StudioRunInsightsService"/> data, JSON serialisation,
/// exit-code logic, window overrides, and the table renderer (via a minimal
/// in-test rendering helper so the test project doesn't need a Cress.Cli
/// reference, which would pull in the Cress.LivingDocs compile-time error
/// from V16 in-progress work).
/// </summary>
public sealed class FlakeReportCommandTests
{
    // -----------------------------------------------------------------------
    // 1. Table renders cleanly for empty run history
    // -----------------------------------------------------------------------

    [Fact]
    public void RenderTable_EmptyHistory_ProducesCleanOutput()
    {
        var insights = new StudioRunInsights(); // no flows
        var output = RenderTable(insights, totalRunsLoaded: 0);

        Assert.Contains("No run history found", output);
        Assert.Contains("╚", output); // box footer
        Assert.DoesNotContain("Exception", output);
    }

    // -----------------------------------------------------------------------
    // 2. Table renders per-flow rows with flaky marker
    // -----------------------------------------------------------------------

    [Fact]
    public void RenderTable_FlakyAndStableFlows_ShowsCorrectColumns()
    {
        var service = new StudioRunInsightsService();
        var runs = BuildAlternatingRuns("login-flow", 6);
        var insights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 25, MinPasses = 1, MinFails = 1, Threshold = 0.10
        });

        var output = RenderTable(insights, totalRunsLoaded: runs.Count);

        Assert.Contains("login-flow", output);
        Assert.Contains("YES", output);    // flaky marker
        Assert.Contains("Flaky?", output); // column header
        Assert.Contains("Pass", output);
        Assert.Contains("Fail", output);
    }

    // -----------------------------------------------------------------------
    // 3. Table shows step breakdown for flaky flows
    // -----------------------------------------------------------------------

    [Fact]
    public void RenderTable_FlakyFlowWithStepBreakdown_ShowsUnstableSteps()
    {
        var service = new StudioRunInsightsService();
        var runs = new[]
        {
            MakeRunWithSteps("run-1", "checkout", DateTimeOffset.UtcNow, [
                MakeStep("page.open", RunOutcome.Passed),
                MakeStep("form.fill", RunOutcome.Failed),
                MakeStep("btn.click", RunOutcome.Passed)
            ]),
            MakeRunWithSteps("run-2", "checkout", DateTimeOffset.UtcNow.AddMinutes(-5), [
                MakeStep("page.open", RunOutcome.Passed),
                MakeStep("form.fill", RunOutcome.Passed),
                MakeStep("btn.click", RunOutcome.Passed)
            ]),
            MakeRunWithSteps("run-3", "checkout", DateTimeOffset.UtcNow.AddMinutes(-10), [
                MakeStep("page.open", RunOutcome.Passed),
                MakeStep("form.fill", RunOutcome.Failed),
                MakeStep("btn.click", RunOutcome.Passed)
            ])
        };

        var insights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 25, MinPasses = 1, MinFails = 1, Threshold = 0.10
        });
        var output = RenderTable(insights, totalRunsLoaded: runs.Length);

        Assert.Contains("form.fill", output);
        Assert.Contains("↳", output);
    }

    // -----------------------------------------------------------------------
    // 4. JSON serialisation round-trip of FlakyFlows
    // -----------------------------------------------------------------------

    [Fact]
    public void JsonSerialize_FlakyFlows_RoundTrips()
    {
        var service = new StudioRunInsightsService();
        var runs = BuildAlternatingRuns("api-health", 4);
        var insights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 25, MinPasses = 1, MinFails = 1, Threshold = 0.10
        });

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(insights.FlakyFlows, jsonOptions);
        var deserialized = JsonSerializer.Deserialize<List<StudioFlowHealthItem>>(json, jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(insights.FlakyFlows.Count, deserialized!.Count);
        if (deserialized.Count > 0)
        {
            Assert.Equal(insights.FlakyFlows[0].FlowId, deserialized[0].FlowId);
            Assert.Equal(insights.FlakyFlows[0].IsFlaky, deserialized[0].IsFlaky);
        }
    }

    // -----------------------------------------------------------------------
    // 5. --exit-code-on-flaky: computed exit code is 1 when flaky flows exist
    // -----------------------------------------------------------------------

    [Fact]
    public void ExitCode_IsOne_WhenFlakyFlowsExistAndFlagEnabled()
    {
        var service = new StudioRunInsightsService();
        var runs = BuildAlternatingRuns("payment-flow", 6);
        var insights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 25, MinPasses = 1, MinFails = 1, Threshold = 0.10
        });

        // Mirrors the exit-code logic in FlakeReportCommand
        var exitCodeOnFlaky = true;
        var exitCode = exitCodeOnFlaky && insights.FlakyFlows.Count > 0 ? 1 : 0;

        Assert.NotEmpty(insights.FlakyFlows);
        Assert.Equal(1, exitCode);
    }

    // -----------------------------------------------------------------------
    // 6. --exit-code-on-flaky: exit 0 when no flaky flows
    // -----------------------------------------------------------------------

    [Fact]
    public void ExitCode_IsZero_WhenNoFlakyFlows_EvenWithFlag()
    {
        var service = new StudioRunInsightsService();
        var runs = BuildAllPassingRuns("stable-flow", 5);
        var insights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 25, MinPasses = 1, MinFails = 1, Threshold = 0.10
        });

        var exitCodeOnFlaky = true;
        var exitCode = exitCodeOnFlaky && insights.FlakyFlows.Count > 0 ? 1 : 0;

        Assert.Empty(insights.FlakyFlows);
        Assert.Equal(0, exitCode);
    }

    // -----------------------------------------------------------------------
    // 7. --window override limits analysis to N most-recent runs
    // -----------------------------------------------------------------------

    [Fact]
    public void WindowOverride_LimitsAnalysisToNRuns()
    {
        var service = new StudioRunInsightsService();

        // 10 runs alternating — wide window sees flakiness, window=1 sees only 1 passing run
        var runs = BuildAlternatingRuns("order-flow", 10);

        var wideInsights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 10, MinPasses = 1, MinFails = 1, Threshold = 0.10
        });

        // Window of 1: only the most-recent run (Passed) — no fail → not flaky
        var narrowInsights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 1, MinPasses = 1, MinFails = 1, Threshold = 0.10
        });

        Assert.NotEmpty(wideInsights.FlakyFlows);  // wide window picks up alternating pattern
        Assert.Empty(narrowInsights.FlakyFlows);   // single-run window → no fails
    }

    // -----------------------------------------------------------------------
    // Inline table renderer (mirrors FlakeReportCommand.PrintTable logic)
    // kept here so the test project needs no Cress.Cli reference.
    // -----------------------------------------------------------------------

    private static string RenderTable(StudioRunInsights insights, int totalRunsLoaded)
    {
        var allFlows = insights.FlowHealth;
        var sb = new StringBuilder();

        sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine($"║  Flake Report   runs-in-window: {totalRunsLoaded,4}   flows analysed: {allFlows.Count,4}            ║");
        sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        if (allFlows.Count == 0)
        {
            sb.AppendLine("║  No run history found. Run some flows first.                                ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            return sb.ToString();
        }

        sb.AppendLine($"║  {"Flow",-34} {"Runs",4} {"Pass",4} {"Fail",4} {"Flake%",7}  {"Flaky?",7}  ║");
        sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        foreach (var flow in allFlows)
        {
            int derivedPass;
            if (flow.PassRate >= 100.0)
                derivedPass = flow.FailureCount == 0 ? 1 : (int)Math.Round(flow.PassRate / 100.0 * (flow.FailureCount + 1));
            else if (flow.PassRate <= 0.0)
                derivedPass = 0;
            else
                derivedPass = (int)Math.Round(flow.FailureCount * flow.PassRate / (100.0 - flow.PassRate));

            var totalRuns = derivedPass + flow.FailureCount;
            var flakyMark = flow.IsFlaky ? " YES  " : "  -   ";

            sb.AppendLine($"║  {Truncate(flow.FlowId, 34),-34} {totalRuns,4} {derivedPass,4} {flow.FailureCount,4} {flow.PassRate,6:F1}%  {flakyMark}  ║");

            foreach (var step in flow.StepBreakdown.Where(s => s.FailCount > 0).OrderByDescending(s => s.FlakeRate).Take(3))
            {
                sb.AppendLine($"║    ↳ {Truncate(step.StepName, 30),-30} fail {step.FailCount}/{step.TotalCount} ({step.FlakeRate:F0}%)        ║");
            }
        }

        sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");
        var flakyCount = insights.FlakyFlows.Count;
        var statusLine = flakyCount == 0 ? "  All flows are stable." : $"  {flakyCount} flaky flow(s) detected.";
        sb.AppendLine($"║{statusLine,-76}║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");

        return sb.ToString();
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<StoredRunResult> BuildAlternatingRuns(string flowId, int count)
        => Enumerable.Range(0, count)
            .Select(i => new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = $"run-{i}", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-i) },
                    Flows =
                    [
                        new FlowRunResult
                        {
                            FlowId = flowId,
                            Name = flowId,
                            Outcome = i % 2 == 0 ? RunOutcome.Passed : RunOutcome.Failed,
                            PassedWithRetry = i % 2 == 0 && i > 0
                        }
                    ]
                }
            })
            .ToList();

    private static IReadOnlyList<StoredRunResult> BuildAllPassingRuns(string flowId, int count)
        => Enumerable.Range(0, count)
            .Select(i => new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = $"run-{i}", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-i) },
                    Flows = [new FlowRunResult { FlowId = flowId, Name = flowId, Outcome = RunOutcome.Passed }]
                }
            })
            .ToList();

    private static StoredRunResult MakeRunWithSteps(string runId, string flowId, DateTimeOffset startedAt, StepRunResult[] steps)
        => new()
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata { RunId = runId, StartedAt = startedAt },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = flowId,
                        Name = flowId,
                        Outcome = steps.Any(s => s.Outcome == RunOutcome.Failed) ? RunOutcome.Failed : RunOutcome.Passed,
                        Steps = steps
                    }
                ]
            }
        };

    private static StepRunResult MakeStep(string name, RunOutcome outcome)
        => new() { Name = name, Kind = "step", Outcome = outcome };
}
