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
}
