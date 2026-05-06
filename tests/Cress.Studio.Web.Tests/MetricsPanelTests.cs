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
        var cells = cut.FindAll(".metrics-heatmap-cell");
        Assert.Equal(3 * 20, cells.Count);
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
}
