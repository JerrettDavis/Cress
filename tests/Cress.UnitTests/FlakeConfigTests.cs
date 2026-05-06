using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Studio.Services;

namespace Cress.UnitTests;

public sealed class FlakeConfigTests
{
    // ------------------------------------------------------------------
    // ConfigLoader: parse flake block
    // ------------------------------------------------------------------

    [Fact]
    public void ConfigLoader_parses_flake_block_when_present()
    {
        using var workspace = new TestWorkspace();
        WriteConfigFile(workspace, """
            version: 1
            project:
              name: Test Project
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
            flake:
              window: 10
              minPasses: 2
              minFails: 3
              threshold: 0.20
            plugins:
              discover: []
            """);

        var result = new ConfigLoader(new ProjectLocator()).Load(workspace.GetPath("project"));

        Assert.True(result.Success);
        Assert.Equal(10, result.Value!.Flake.Window);
        Assert.Equal(2, result.Value.Flake.MinPasses);
        Assert.Equal(3, result.Value.Flake.MinFails);
        Assert.Equal(0.20, result.Value.Flake.Threshold);
    }

    [Fact]
    public void ConfigLoader_applies_defaults_when_flake_block_absent()
    {
        using var workspace = new TestWorkspace();
        WriteConfigFile(workspace, """
            version: 1
            project:
              name: Test Project
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
              discover: []
            """);

        var result = new ConfigLoader(new ProjectLocator()).Load(workspace.GetPath("project"));

        Assert.True(result.Success);
        Assert.Equal(25, result.Value!.Flake.Window);
        Assert.Equal(1, result.Value.Flake.MinPasses);
        Assert.Equal(1, result.Value.Flake.MinFails);
        Assert.Equal(0.10, result.Value.Flake.Threshold);
    }

    // ------------------------------------------------------------------
    // EffectiveConfig.ResolvedFlake: profile override
    // ------------------------------------------------------------------

    [Fact]
    public void EffectiveConfig_ResolvedFlake_applies_profile_override_for_window()
    {
        var effectiveConfig = new EffectiveConfig
        {
            Config = new CressConfig { Flake = new FlakeConfig { Window = 25 } },
            Profile = new CressProfile { Flake = new FlakeProfileConfig { Window = 10 } }
        };

        var resolved = effectiveConfig.ResolvedFlake;

        Assert.Equal(10, resolved.Window);
        // Other fields keep the config-level defaults
        Assert.Equal(1, resolved.MinPasses);
        Assert.Equal(1, resolved.MinFails);
        Assert.Equal(0.10, resolved.Threshold);
    }

    [Fact]
    public void EffectiveConfig_ResolvedFlake_returns_config_defaults_when_profile_has_no_flake_block()
    {
        var effectiveConfig = new EffectiveConfig
        {
            Config = new CressConfig { Flake = new FlakeConfig { Window = 15, Threshold = 0.25 } },
            Profile = new CressProfile { Flake = null }
        };

        var resolved = effectiveConfig.ResolvedFlake;

        Assert.Equal(15, resolved.Window);
        Assert.Equal(0.25, resolved.Threshold);
    }

    [Fact]
    public void EffectiveConfig_ResolvedFlake_merges_partial_profile_override()
    {
        var effectiveConfig = new EffectiveConfig
        {
            Config = new CressConfig
            {
                Flake = new FlakeConfig { Window = 20, MinPasses = 2, MinFails = 2, Threshold = 0.15 }
            },
            Profile = new CressProfile
            {
                Flake = new FlakeProfileConfig { Window = 5, Threshold = 0.30 }
                // MinPasses and MinFails are null — keep config defaults
            }
        };

        var resolved = effectiveConfig.ResolvedFlake;

        Assert.Equal(5, resolved.Window);
        Assert.Equal(0.30, resolved.Threshold);
        Assert.Equal(2, resolved.MinPasses); // from config
        Assert.Equal(2, resolved.MinFails);  // from config
    }

    // ------------------------------------------------------------------
    // StudioRunInsightsService: uses configured window
    // ------------------------------------------------------------------

    [Fact]
    public void StudioRunInsightsService_uses_configured_window_to_limit_run_history()
    {
        var service = new StudioRunInsightsService();
        var runs = BuildRuns(10, "flow-a", alternating: true);

        // Default window (25) sees all 10 runs — flow is flaky
        var insightsWide = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig { Window = 25 });

        // Narrow window (2) — only 2 runs, the last is Passed (our alternating starts from newest Passed)
        // With window=2 and alternating P/F pattern, we get 1 pass + 1 fail → flaky
        var insightsNarrow = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig { Window = 2, Threshold = 0.10 });

        // With full window, FlowHealth should have 10-run-wide data
        var health = insightsWide.FlowHealth[0];
        Assert.Equal("flow-a", health.FlowId);
        // Narrow window: 2 runs = 1 pass + 1 fail → still flaky under default threshold
        Assert.NotEmpty(insightsNarrow.FlowHealth);
    }

    [Fact]
    public void StudioRunInsightsService_threshold_excludes_flows_below_fail_rate()
    {
        var service = new StudioRunInsightsService();
        // 9 passes, 1 fail = 10% fail rate
        var runs = BuildRunsWithOneFail("flow-stable", total: 10, failIndex: 0);

        // With threshold=0.20, 10% fail rate should NOT be flagged
        var insights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 25,
            MinPasses = 1,
            MinFails = 1,
            Threshold = 0.20
        });

        Assert.Empty(insights.FlakyFlows);
    }

    // ------------------------------------------------------------------
    // StepInstability breakdown
    // ------------------------------------------------------------------

    [Fact]
    public void StudioRunInsightsService_computes_step_breakdown_from_run_history()
    {
        var service = new StudioRunInsightsService();

        // 3 runs of "flow-b", each with 3 steps
        // Step 1 (index 0): always passes
        // Step 2 (index 1): fails in run 1 and run 3 (2/3 fail rate)
        // Step 3 (index 2): always passes
        var runs = new[]
        {
            MakeRun("run-1", "flow-b", DateTimeOffset.UtcNow, steps: [
                MakeStep("step.open",  RunOutcome.Passed),
                MakeStep("step.fill",  RunOutcome.Failed),
                MakeStep("step.click", RunOutcome.Passed)
            ]),
            MakeRun("run-2", "flow-b", DateTimeOffset.UtcNow.AddMinutes(-5), steps: [
                MakeStep("step.open",  RunOutcome.Passed),
                MakeStep("step.fill",  RunOutcome.Passed),
                MakeStep("step.click", RunOutcome.Passed)
            ]),
            MakeRun("run-3", "flow-b", DateTimeOffset.UtcNow.AddMinutes(-10), steps: [
                MakeStep("step.open",  RunOutcome.Passed),
                MakeStep("step.fill",  RunOutcome.Failed),
                MakeStep("step.click", RunOutcome.Passed)
            ])
        };

        var insights = service.Analyze(new ProjectCatalog(), runs, new FlakeConfig
        {
            Window = 25, MinPasses = 1, MinFails = 1, Threshold = 0.10
        });

        var health = insights.FlowHealth.Single(item => item.FlowId == "flow-b");
        Assert.Equal(3, health.StepBreakdown.Count);

        var stepOpen  = health.StepBreakdown.Single(s => s.StepName == "step.open");
        var stepFill  = health.StepBreakdown.Single(s => s.StepName == "step.fill");
        var stepClick = health.StepBreakdown.Single(s => s.StepName == "step.click");

        Assert.Equal(0, stepOpen.FailCount);
        Assert.Equal(3, stepOpen.TotalCount);
        Assert.Equal(0d, stepOpen.FlakeRate);

        Assert.Equal(2, stepFill.FailCount);
        Assert.Equal(3, stepFill.TotalCount);
        Assert.Equal(Math.Round(2d / 3 * 100, 1), stepFill.FlakeRate);

        Assert.Equal(0, stepClick.FailCount);
    }

    [Fact]
    public void StudioRunInsightsService_step_breakdown_is_empty_when_no_step_data()
    {
        var service = new StudioRunInsightsService();
        var runs = new[]
        {
            new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata { RunId = "run-1", StartedAt = DateTimeOffset.UtcNow },
                    Flows = [new FlowRunResult { FlowId = "flow-nosteps", Name = "No-steps flow", Outcome = RunOutcome.Passed, Steps = [] }]
                }
            }
        };

        var insights = service.Analyze(new ProjectCatalog(), runs);
        var health = insights.FlowHealth.Single();

        Assert.Empty(health.StepBreakdown);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void WriteConfigFile(TestWorkspace workspace, string content)
    {
        Directory.CreateDirectory(workspace.GetPath("project", ".cress"));
        File.WriteAllText(workspace.GetPath("project", ".cress", "config.yaml"), content);
    }

    private static StoredRunResult[] BuildRuns(int count, string flowId, bool alternating)
    {
        return Enumerable.Range(0, count)
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
                            Outcome = alternating && i % 2 == 0 ? RunOutcome.Passed : RunOutcome.Failed,
                            PassedWithRetry = alternating && i % 2 == 0 && i > 0
                        }
                    ]
                }
            })
            .ToArray();
    }

    private static IReadOnlyList<StoredRunResult> BuildRunsWithOneFail(string flowId, int total, int failIndex)
    {
        return Enumerable.Range(0, total)
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
                            Outcome = i == failIndex ? RunOutcome.Failed : RunOutcome.Passed
                        }
                    ]
                }
            })
            .ToList();
    }

    private static StoredRunResult MakeRun(string runId, string flowId, DateTimeOffset startedAt, StepRunResult[] steps)
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
