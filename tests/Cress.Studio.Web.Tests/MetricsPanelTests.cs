using System.Reflection;
using Bunit;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

/// <summary>
/// bUnit tests for the MetricsPanel component (V15).
/// </summary>
public sealed class MetricsPanelTests : TestContext
{
    private StudioWorkspaceState CreateState()
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
    }

    // -------------------------------------------------------------------------
    // Fixture factory helpers
    // -------------------------------------------------------------------------

    private static RunMetrics MakeMetrics(int totalRuns = 10, double passRate = 0.9,
        int flowCount = 3, int trendPoints = 5)
    {
        var suite = new SuiteMetrics(
            TotalRuns: totalRuns,
            PassedRuns: (int)(totalRuns * passRate),
            FailedRuns: totalRuns - (int)(totalRuns * passRate),
            PassRate: passRate,
            TotalDuration: TimeSpan.FromSeconds(totalRuns * 5),
            AvgDuration: TimeSpan.FromSeconds(5));

        var flows = Enumerable.Range(1, flowCount).Select(i => new FlowMetrics(
            FlowId: $"flow-{i:00}",
            Runs: 10,
            Passed: 9,
            Failed: 1,
            PassRate: 0.9,
            FlakeRate: 0.1,
            AvgDuration: TimeSpan.FromMilliseconds(100 * i),
            P50: TimeSpan.FromMilliseconds(90 * i),
            P95: TimeSpan.FromMilliseconds(200 * i),
            P99: TimeSpan.FromMilliseconds(250 * i),
            MTTR: null)).ToList();

        var steps = Enumerable.Range(1, flowCount).SelectMany(fi =>
            Enumerable.Range(0, 2).Select(si => new StepMetrics(
                FlowId: $"flow-{fi:00}",
                StepIndex: si,
                StepName: $"step-{fi:00}-{si}",
                Runs: 10,
                Passed: 9,
                Failed: 1,
                AvgDuration: TimeSpan.FromMilliseconds(50 * (si + 1)),
                P50: TimeSpan.FromMilliseconds(45 * (si + 1)),
                P95: TimeSpan.FromMilliseconds(95 * (si + 1)),
                P99: TimeSpan.FromMilliseconds(110 * (si + 1))))).ToList();

        var now = DateTimeOffset.UtcNow;
        var trend = Enumerable.Range(0, trendPoints).Select(i => new TrendPoint(
            Timestamp: now.AddDays(-(trendPoints - 1 - i)),
            PassedFlows: 9,
            FailedFlows: 1,
            AvgFlowDuration: TimeSpan.FromMilliseconds(150))).ToList();

        return new RunMetrics(suite, flows, steps, trend);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void MetricsPanel_renders_empty_state_when_no_runs()
    {
        // Arrange: state with null CurrentMetrics (no project loaded)
        var state = CreateState();
        // CurrentMetrics is null by default — no snapshot set

        // Act
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // Assert: empty-state message
        Assert.Contains("No run history yet", cut.Markup);
        Assert.DoesNotContain("summary-card", cut.Markup);
    }

    [Fact]
    public void MetricsPanel_renders_4_stat_cards_when_metrics_populated()
    {
        // Arrange
        var state = CreateState();
        SetPrivate(state, "CurrentMetrics", MakeMetrics());

        // Act
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // Assert: 4 summary-card elements
        var cards = cut.FindAll(".summary-card");
        Assert.Equal(4, cards.Count);

        // Each card should have a label
        Assert.Contains("Total runs", cut.Markup);
        Assert.Contains("Pass rate", cut.Markup);
        Assert.Contains("Avg duration", cut.Markup);
        Assert.Contains("Flake rate", cut.Markup);
    }

    [Fact]
    public void MetricsPanel_renders_pass_rate_with_green_class_when_above_95()
    {
        var state = CreateState();
        SetPrivate(state, "CurrentMetrics", MakeMetrics(passRate: 0.98));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // 98% pass rate should use metrics-green
        Assert.Contains("metrics-green", cut.Markup);
    }

    [Fact]
    public void MetricsPanel_renders_sparkline_polyline_when_3_or_more_trend_points()
    {
        // Arrange: metrics with 5 trend points
        var state = CreateState();
        SetPrivate(state, "CurrentMetrics", MakeMetrics(trendPoints: 5));

        // Act
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // Assert: sparkline section and polyline are rendered
        Assert.Contains("metrics-sparkline", cut.Markup);
        // A polyline element with points attribute should be present
        var polylines = cut.FindAll("polyline");
        Assert.NotEmpty(polylines);
        var pts = polylines[0].GetAttribute("points");
        Assert.NotNull(pts);
        Assert.NotEmpty(pts);
    }

    [Fact]
    public void MetricsPanel_does_not_render_sparkline_with_fewer_than_3_trend_points()
    {
        var state = CreateState();
        SetPrivate(state, "CurrentMetrics", MakeMetrics(trendPoints: 2));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // No sparkline section when < 3 points
        Assert.DoesNotContain("metrics-sparkline-section", cut.Markup);
    }

    [Fact]
    public void MetricsPanel_renders_top_flows_table_with_correct_column_headers()
    {
        var state = CreateState();
        SetPrivate(state, "CurrentMetrics", MakeMetrics(flowCount: 3));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // Table with metrics-flows-table class should be present
        var tables = cut.FindAll(".metrics-flows-table");
        Assert.NotEmpty(tables);

        // Column headers
        Assert.Contains("Flow", cut.Markup);
        Assert.Contains("P95", cut.Markup);
        Assert.Contains("P99", cut.Markup);
        Assert.Contains("Flake%", cut.Markup);
    }

    [Fact]
    public void MetricsPanel_renders_correct_number_of_flow_rows()
    {
        var state = CreateState();
        // 3 flows in metrics
        SetPrivate(state, "CurrentMetrics", MakeMetrics(flowCount: 3));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // 3 tbody rows for the flows (limited to 10 max)
        var rows = cut.FindAll(".metrics-flows-table tbody tr");
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void MetricsPanel_renders_heatmap_grid_with_correct_cell_count()
    {
        // Arrange: 3 flows, 20 cols — no runs in State.Runs so all cells are "not-run"
        var state = CreateState();
        SetPrivate(state, "CurrentMetrics", MakeMetrics(flowCount: 3));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // Heatmap section should be present
        Assert.Contains("metrics-heatmap", cut.Markup);

        // 3 flows × 20 cols = 60 cells
        var cells = cut.FindAll(".metrics-heatmap [role='cell']");
        Assert.Equal(3 * 20, cells.Count);
    }

    [Fact]
    public void MetricsPanel_renders_heatmap_legend_and_summary_text()
    {
        var state = CreateState();
        SetPrivate(state, "CurrentMetrics", MakeMetrics(flowCount: 2));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        var summary = cut.Find("[data-testid='metrics-heatmap-summary']").TextContent;
        Assert.Contains("heatmap does not rely on color alone", summary);
        Assert.Contains("Passed", summary);
        Assert.Contains("Failed", summary);
        Assert.Contains("Not run", summary);
    }

    [Fact]
    public void MetricsPanel_renders_step_p95_bars_inside_details_for_flows_with_steps()
    {
        var state = CreateState();
        // flowCount=2 generates steps for each flow
        SetPrivate(state, "CurrentMetrics", MakeMetrics(flowCount: 2, trendPoints: 5));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        // The step details elements should be present for flows that have steps
        Assert.Contains("metrics-step-details", cut.Markup);
        Assert.Contains("metrics-step-bars", cut.Markup);

        // Bar tracks and fills should be present
        Assert.Contains("metrics-step-bar-track", cut.Markup);
        Assert.Contains("metrics-step-bar-fill", cut.Markup);
    }

    [Fact]
    public void MetricsPanel_renders_amber_and_red_status_classes_for_lower_rates()
    {
        var state = CreateState();
        var suite = new SuiteMetrics(
            TotalRuns: 10,
            PassedRuns: 7,
            FailedRuns: 3,
            PassRate: 0.85,
            TotalDuration: TimeSpan.FromMinutes(8),
            AvgDuration: TimeSpan.FromSeconds(5));
        var flows = new List<FlowMetrics>
        {
            new(
                FlowId: "flow-amber",
                Runs: 10,
                Passed: 8,
                Failed: 2,
                PassRate: 0.85,
                FlakeRate: 0.05,
                AvgDuration: TimeSpan.FromMilliseconds(450),
                P50: TimeSpan.FromMilliseconds(400),
                P95: TimeSpan.FromMilliseconds(800),
                P99: TimeSpan.FromMilliseconds(900),
                MTTR: null),
            new(
                FlowId: "flow-red",
                Runs: 10,
                Passed: 6,
                Failed: 4,
                PassRate: 0.6,
                FlakeRate: 0.2,
                AvgDuration: TimeSpan.FromMinutes(2),
                P50: TimeSpan.FromSeconds(70),
                P95: TimeSpan.FromMinutes(2.5),
                P99: TimeSpan.FromMinutes(3),
                MTTR: null)
        };

        SetPrivate(state, "CurrentMetrics", new RunMetrics(suite, flows, [], []));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        Assert.Contains("metrics-amber", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("metrics-red", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("2.5m", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricsPanel_renders_empty_messages_for_missing_flow_and_heatmap_data()
    {
        var state = CreateState();
        var suite = new SuiteMetrics(
            TotalRuns: 4,
            PassedRuns: 4,
            FailedRuns: 0,
            PassRate: 1.0,
            TotalDuration: TimeSpan.FromSeconds(4),
            AvgDuration: TimeSpan.FromMilliseconds(500));

        SetPrivate(state, "CurrentMetrics", new RunMetrics(suite, [], [], []));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        Assert.Contains("No flow metrics yet.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No heatmap data yet.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricsPanel_renders_mixed_heatmap_outcomes_and_selected_row_state()
    {
        var state = CreateState();
        SetPrivate(state, nameof(StudioWorkspaceState.SelectedFlow), new Cress.Studio.ViewModels.FlowDocumentViewModel
        {
            Id = "flow-01",
            Name = "Flow 01",
            FilePath = @"C:\workspace\flows\flow-01.flow.yaml"
        });
        SetPrivate(state, "CurrentMetrics", MakeMetrics(flowCount: 1, trendPoints: 3));

        state.Runs.Add(new StoredRunResult
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata { RunId = "run-1" },
                Flows =
                [
                    new FlowRunResult { FlowId = "flow-01", Outcome = RunOutcome.Passed }
                ]
            }
        });
        state.Runs.Add(new StoredRunResult
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata { RunId = "run-2" },
                Flows =
                [
                    new FlowRunResult { FlowId = "flow-01", Outcome = RunOutcome.Errored }
                ]
            }
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();

        Assert.Contains("metrics-row-selected", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Passed (1)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Failed (1)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Not run (18)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("flow-01 • run-1 • Passed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("flow-01 • run-2 • Errored", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricsPanel_flow_row_click_selects_flow_and_private_helpers_cover_zero_rate_and_warning_color()
    {
        var state = CreateState();
        var projectRoot = Path.Combine(Path.GetTempPath(), "cress-metrics-tests", Guid.NewGuid().ToString("N"));
        var flowPath = Path.Combine(projectRoot, "flows", "flow-01.flow.yaml");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(flowPath)!);
            File.WriteAllText(flowPath, """
            version: 1
            id: flow-01
            name: Flow 01
            when:
              - step: http.get
                with:
                  url: https://example.test
            then:
              - expect: http.assert-status
                with:
                  status: "200"
            """);

            SetPrivate(state, nameof(StudioWorkspaceState.Snapshot), new StudioProjectSnapshot
            {
                Catalog = new ProjectCatalog
                {
                    NormalizedFlows =
                    [
                        new NormalizedFlow
                        {
                            FlowId = "flow-01",
                            Name = "Flow 01",
                            SourceFile = flowPath
                        }
                    ]
                }
            });
            SetPrivate(state, "CurrentMetrics", MakeMetrics(flowCount: 1, trendPoints: 3));

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.MetricsPanel>();
            cut.Find(".metrics-flows-table tbody tr").Click();

            Assert.Equal("flow-01", state.SelectedFlow?.Id);

            var panelType = typeof(Cress.Studio.Web.Components.Studio.MetricsPanel);
            var trendPassRate = panelType.GetMethod("TrendPassRate", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("TrendPassRate was not found.");
            var sparklineColor = panelType.GetMethod("SparklineColor", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("SparklineColor was not found.");

            Assert.Equal(0d, Assert.IsType<double>(trendPassRate.Invoke(null, [new TrendPoint(DateTimeOffset.UtcNow, 0, 0, TimeSpan.Zero)])));
            Assert.Equal("var(--color-warning)", Assert.IsType<string>(sparklineColor.Invoke(null, [new TrendPoint(DateTimeOffset.UtcNow, 8, 2, TimeSpan.FromSeconds(1))])));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, true);
            }
        }
    }
}
