using System.Reflection;
using Bunit;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class ResultsPanelTests : TestContext
{
    private static StoredRunResult CreateRun(string runId, RunOutcome outcome, string profile = "local")
        => new()
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = runId,
                    Profile = profile,
                    StartedAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero)
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = $"{runId}-flow",
                        Name = $"{runId} flow",
                        Outcome = outcome,
                        Steps =
                        [
                            new StepRunResult
                            {
                                Name = "step-1",
                                Kind = "action",
                                Attempt = 1,
                                Outcome = outcome
                            }
                        ]
                    }
                ]
            }
        };

    private StudioWorkspaceState CreateState()
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
    }

    [Fact]
    public void ResultsPanel_renders_empty_state_when_no_runs_loaded()
    {
        CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        Assert.Contains("Runs, evidence, and diagnostics", cut.Markup);
        Assert.Contains("Live run", cut.Markup);
        Assert.Contains("Recent runs", cut.Markup);
        Assert.Empty(cut.FindAll(".stack-list .explorer-button"));
    }

    [Fact]
    public void ResultsPanel_renders_run_list_with_two_run_entries()
    {
        var state = CreateState();

        var run1 = CreateRun("run-001", RunOutcome.Passed);
        var run2 = CreateRun("run-002", RunOutcome.Passed);

        state.Runs.Add(run1);
        state.Runs.Add(run2);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        Assert.Contains("run-001", cut.Markup);
        Assert.Contains("run-002", cut.Markup);
    }

    [Fact]
    public void ResultsPanel_diagnostics_section_renders_when_diagnostics_exist()
    {
        var state = CreateState();

        state.Diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Code = "W001",
            Message = "Missing fixture definition",
            File = @"C:\workspace\flows\login.flow.yaml"
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        Assert.Contains("Diagnostics", cut.Markup);
        Assert.Contains("W001", cut.Markup);
        Assert.Contains("Missing fixture definition", cut.Markup);
        Assert.Contains(@"C:\workspace\flows\login.flow.yaml", cut.Markup);
    }

    [Fact]
    public void ResultsPanel_live_run_subpanel_shows_headline_when_set()
    {
        var state = CreateState();

        SetPrivate(state, "LiveRunHeadline", "run-abc passed");

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        Assert.Contains("run-abc passed", cut.Markup);
        Assert.Contains("live-headline", cut.Markup);
    }

    [Fact]
    public void ResultsPanel_failed_only_toggle_filters_to_problem_runs()
    {
        var state = CreateState();
        state.Runs.Add(CreateRun("run-pass", RunOutcome.Passed));
        state.Runs.Add(CreateRun("run-error", RunOutcome.Errored));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        cut.Find("[data-testid='results-failed-only']").Change(true);

        Assert.DoesNotContain("run-pass", cut.Markup);
        Assert.Contains("run-error", cut.Markup);
        Assert.Contains("1 shown", cut.Markup);
        Assert.Contains("1 failed", cut.Markup);
        Assert.Contains("Failed-only filter on", cut.Markup);
    }

    [Fact]
    public void ResultsPanel_search_matches_status_terms_and_clear_button_resets_filter()
    {
        var state = CreateState();
        state.Runs.Add(CreateRun("run-pass", RunOutcome.Passed));
        state.Runs.Add(CreateRun("run-error", RunOutcome.Errored));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        cut.Find("[data-testid='results-run-filter']").Input("failed");

        Assert.Equal("failed", state.RunFilter);
        Assert.DoesNotContain("run-pass", cut.Markup);
        Assert.Contains("run-error", cut.Markup);

        cut.Find("[aria-label='Clear run filter']").Click();

        Assert.Equal(string.Empty, state.RunFilter);
        Assert.Contains("run-pass", cut.Markup);
        Assert.Contains("run-error", cut.Markup);
    }

    [Fact]
    public void ResultsPanel_uses_failure_status_style_for_errored_runs_and_flows()
    {
        var state = CreateState();
        var erroredRun = CreateRun("run-error", RunOutcome.Errored);
        state.Runs.Add(erroredRun);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        var runButton = cut.Find("[data-testid='results-run-run-error']");
        Assert.Contains("run-status--failed", runButton.InnerHtml);

        runButton.Click();

        var flowButton = cut.Find("[data-testid='results-flow-run-error-flow']");
        Assert.Contains("run-status--failed", flowButton.InnerHtml);
    }

    [Fact]
    public void ResultsPanel_live_events_render_when_run_is_active()
    {
        var state = CreateState();
        state.LiveEvents.AddRange(["Run queued", "Flow started"]);
        SetPrivate(state, "LiveRunHeadline", "Executing run-123");

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        Assert.DoesNotContain("results-live-empty", cut.Markup);
        Assert.Contains("Run queued", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Flow started", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPanel_shows_filter_empty_message_when_runs_exist_but_filters_exclude_all()
    {
        var state = CreateState();
        state.Runs.Add(CreateRun("run-pass", RunOutcome.Passed));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();
        cut.Find("[data-testid='results-run-filter']").Input("missing");

        Assert.NotNull(cut.Find("[data-testid='results-runs-filter-empty']"));
        Assert.Contains("0 shown", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPanel_filters_runs_by_profile_terms()
    {
        var state = CreateState();
        state.Runs.Add(CreateRun("run-skip", RunOutcome.Skipped, profile: "qa"));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();
        cut.Find("[data-testid='results-run-filter']").Input("qa");

        Assert.Contains("run-skip", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Filter: qa", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPanel_selects_run_flow_and_step_and_renders_empty_follow_up_states()
    {
        var state = CreateState();
        var run = CreateRun("run-001", RunOutcome.Passed);
        state.Runs.Add(run);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        cut.Find("[data-testid='results-run-run-001']").Click();
        cut.Find("[data-testid='results-flow-run-001-flow']").Click();
        cut.Find("[data-testid='results-step-step-1']").Click();

        Assert.Contains("selected", cut.Find("[data-testid='results-run-run-001']").GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("selected", cut.Find("[data-testid='results-flow-run-001-flow']").GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("selected", cut.Find("[data-testid='results-step-step-1']").GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);

        state.SelectRun(new StoredRunResult
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata { RunId = "run-empty" },
                Flows = []
            }
        });
        cut.Render();
        Assert.Contains("This run has no flow results.", cut.Markup, StringComparison.Ordinal);

        state.SelectRun(run);
        state.SelectRunFlow(new FlowRunResult { FlowId = "flow-empty", Name = "Flow empty", Outcome = RunOutcome.Passed, Steps = [] });
        cut.Render();
        Assert.Contains("No steps recorded for this flow.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPanel_renders_diagnostic_icons_and_comparison_states()
    {
        var state = CreateState();
        state.Diagnostics.Add(new Diagnostic { Severity = DiagnosticSeverity.Error, Code = "E001", Message = "Broken flow" });
        state.Diagnostics.Add(new Diagnostic { Severity = DiagnosticSeverity.Info, Code = "I001", Message = "Helpful note" });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ResultsPanel>();

        Assert.Contains("diagnostic-item--error", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("diagnostic-item--info", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No comparison available yet", cut.Markup, StringComparison.Ordinal);

        SetPrivate(state, nameof(StudioWorkspaceState.SelectedRunComparison), new StudioRunComparison
        {
            Summary = "1 regression, 1 recovery"
        });
        cut.Render();

        Assert.Contains("1 regression, 1 recovery", cut.Markup, StringComparison.Ordinal);
    }
}
