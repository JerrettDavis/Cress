using Cress.Studio.Services;

namespace Cress.Studio.Web.Tests;

public sealed class RunMetricsRecordTests
{
    [Fact]
    public void MetricsRecords_ExposeConfiguredValues()
    {
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
        var trend = new TrendPoint(new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero), 2, 1, TimeSpan.FromSeconds(30));
        var metrics = new RunMetrics(suite, [flow], [step], [trend]);

        Assert.Equal(3, metrics.Suite.TotalRuns);
        Assert.Equal("flow.search", metrics.Flows[0].FlowId);
        Assert.Equal("browser.open", metrics.Steps[0].StepName);
        Assert.Equal(2, metrics.Trend[0].PassedFlows);
        Assert.Null(MetricsOptions.Default.Window);
        Assert.Null(MetricsOptions.Default.MaxRuns);
    }
}
