using Cress.Studio.Services;

namespace Cress.Studio.Web.Tests;

public sealed class RunMetricsRecordTests
{
    [Fact]
    public void MetricsRecords_ExposeConfiguredValues()
    {
        var capturedTimestamp = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero);
        var suite = new SuiteMetrics(3, 2, 1, 66.7, TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(2));
        var flow = new FlowMetrics(
            "flow.search",
            3,
            2,
            1,
            66.7,
            33.3,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromSeconds(50),
            TimeSpan.FromMinutes(5));
        var step = new StepMetrics(
            "flow.search",
            0,
            "browser.open",
            3,
            2,
            1,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(9),
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(15));
        var trend = new TrendPoint(capturedTimestamp, 2, 1, TimeSpan.FromSeconds(30));
        var metrics = new RunMetrics(suite, [flow], [step], [trend]);

        Assert.Equal(3, metrics.Suite.TotalRuns);
        Assert.Equal(2, metrics.Suite.PassedRuns);
        Assert.Equal(1, metrics.Suite.FailedRuns);
        Assert.Equal(66.7, metrics.Suite.PassRate);
        Assert.Equal(TimeSpan.FromMinutes(6), metrics.Suite.TotalDuration);
        Assert.Equal(TimeSpan.FromMinutes(2), metrics.Suite.AvgDuration);
        Assert.Equal("flow.search", metrics.Flows[0].FlowId);
        Assert.Equal(3, metrics.Flows[0].Runs);
        Assert.Equal(2, metrics.Flows[0].Passed);
        Assert.Equal(1, metrics.Flows[0].Failed);
        Assert.Equal(66.7, metrics.Flows[0].PassRate);
        Assert.Equal(33.3, metrics.Flows[0].FlakeRate);
        Assert.Equal(TimeSpan.FromSeconds(30), metrics.Flows[0].AvgDuration);
        Assert.Equal(TimeSpan.FromSeconds(20), metrics.Flows[0].P50);
        Assert.Equal(TimeSpan.FromSeconds(40), metrics.Flows[0].P95);
        Assert.Equal(TimeSpan.FromSeconds(50), metrics.Flows[0].P99);
        Assert.Equal(TimeSpan.FromMinutes(5), metrics.Flows[0].MTTR);
        Assert.Equal("flow.search", metrics.Steps[0].FlowId);
        Assert.Equal(0, metrics.Steps[0].StepIndex);
        Assert.Equal("browser.open", metrics.Steps[0].StepName);
        Assert.Equal(3, metrics.Steps[0].Runs);
        Assert.Equal(2, metrics.Steps[0].Passed);
        Assert.Equal(1, metrics.Steps[0].Failed);
        Assert.Equal(TimeSpan.FromSeconds(10), metrics.Steps[0].AvgDuration);
        Assert.Equal(TimeSpan.FromSeconds(9), metrics.Steps[0].P50);
        Assert.Equal(TimeSpan.FromSeconds(12), metrics.Steps[0].P95);
        Assert.Equal(TimeSpan.FromSeconds(15), metrics.Steps[0].P99);
        Assert.Equal(capturedTimestamp, metrics.Trend[0].Timestamp);
        Assert.Equal(2, metrics.Trend[0].PassedFlows);
        Assert.Equal(1, metrics.Trend[0].FailedFlows);
        Assert.Equal(TimeSpan.FromSeconds(30), metrics.Trend[0].AvgFlowDuration);
        Assert.Null(MetricsOptions.Default.Window);
        Assert.Null(MetricsOptions.Default.MaxRuns);
    }
}
