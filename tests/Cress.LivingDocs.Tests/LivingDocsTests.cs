using System.Text.RegularExpressions;
using Cress.Core.Models;
using Cress.Execution;
using Cress.LivingDocs;
using Cress.Studio.Services;

namespace Cress.LivingDocs.Tests;

// ──────────────────────────────────────────────────────────────────────────────
// Helpers shared across tests
// ──────────────────────────────────────────────────────────────────────────────

file static class Factories
{
    public static DocumentModel EmptyModel(string title = "Test", string accent = "#6366f1") =>
        new(
            Meta: new DocumentMeta("TestProject", "2026-01-01 00:00:00 UTC", "1.0", string.Empty),
            Branding: new DocumentBranding(title, null, accent),
            Suite: new SuiteSummary(0, 0, 0, 0.0, TimeSpan.Zero),
            Flows: [],
            Metrics: null,
            RecentRuns: [],
            Screenshots: [],
            Diagnostics: []);

    public static DocumentModel RichModel(
        int flowCount = 3,
        double passRate = 0.8,
        string accent = "#22c55e") =>
        new(
            Meta: new DocumentMeta("MyProject", "2026-01-01 00:00:00 UTC", "1.0", "abc1234567890"),
            Branding: new DocumentBranding("My Suite", "https://example.com/logo.png", accent),
            Suite: new SuiteSummary(
                TotalFlows: flowCount,
                PassedRuns: (int)(flowCount * passRate),
                FailedRuns: flowCount - (int)(flowCount * passRate),
                PassRate: passRate,
                AvgDuration: TimeSpan.FromSeconds(12)),
            Flows: Enumerable.Range(1, flowCount).Select(i => new FlowSummary(
                Id: $"flow-{i:000}",
                Name: $"Flow {i}",
                Description: null,
                Status: i % 3 == 0 ? "failing" : "passing",
                PassRate: i % 3 == 0 ? 0.2 : 1.0,
                AvgDuration: TimeSpan.FromMilliseconds(500 * i))).ToList(),
            Metrics: null,
            RecentRuns: Enumerable.Range(0, 5).Select(i => new RunHistoryEntry(
                RunId: $"run-{i:000}",
                Timestamp: DateTimeOffset.UtcNow.AddHours(-i),
                PassedCount: flowCount - (i % 2),
                FailedCount: i % 2)).ToList(),
            Screenshots: [],
            Diagnostics: []);
}

// ──────────────────────────────────────────────────────────────────────────────
// DocumentModel construction
// ──────────────────────────────────────────────────────────────────────────────

public sealed class DocumentModelTests
{
    [Fact]
    public void EmptyModel_HasZeroStats()
    {
        var model = Factories.EmptyModel();

        Assert.Equal(0, model.Suite.TotalFlows);
        Assert.Equal(0.0, model.Suite.PassRate);
        Assert.Empty(model.Flows);
        Assert.Empty(model.RecentRuns);
    }

    [Fact]
    public void RichModel_FlowCount_MatchesSuite()
    {
        var model = Factories.RichModel(flowCount: 5);

        Assert.Equal(5, model.Suite.TotalFlows);
        Assert.Equal(5, model.Flows.Count);
    }

    [Fact]
    public void RichModel_Branding_ReflectsInputs()
    {
        var model = Factories.RichModel(accent: "#ff0000");

        Assert.Equal("#ff0000", model.Branding.AccentColor);
        Assert.Equal("My Suite", model.Branding.Title);
        Assert.NotNull(model.Branding.LogoUrl);
    }

    [Fact]
    public void FlowSummary_StatusValues_AreValid()
    {
        var model = Factories.RichModel(flowCount: 9);
        var validStatuses = new[] { "passing", "failing", "flaky" };

        foreach (var flow in model.Flows)
        {
            Assert.Contains(flow.Status, validStatuses);
        }
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// TemplateRenderer — executive template
// ──────────────────────────────────────────────────────────────────────────────

public sealed class TemplateRendererExecutiveTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void Executive_EmptyModel_RendersWithoutError()
    {
        var html = _renderer.RenderEmbedded("executive", Factories.EmptyModel());

        Assert.False(string.IsNullOrWhiteSpace(html));
    }

    [Fact]
    public void Executive_Contains_TitleTag()
    {
        const string title = "My Status Page";
        var html = _renderer.RenderEmbedded("executive", Factories.EmptyModel(title: title));

        Assert.Contains($"<title>{title}</title>", html);
    }

    [Fact]
    public void Executive_Contains_AccentColor_InCss()
    {
        const string accent = "#22c55e";
        var html = _renderer.RenderEmbedded("executive", Factories.EmptyModel(accent: accent));

        // accent color appears in the CSS --accent variable declaration
        Assert.Contains(accent, html);
    }

    [Fact]
    public void Executive_RichModel_ContainsFlowNames()
    {
        var model = Factories.RichModel(flowCount: 3);
        var html = _renderer.RenderEmbedded("executive", model);

        Assert.Contains("Flow 1", html);
        Assert.Contains("Flow 2", html);
        Assert.Contains("Flow 3", html);
    }

    [Fact]
    public void Executive_EmptyModel_ShowsNoFlowMessage()
    {
        var html = _renderer.RenderEmbedded("executive", Factories.EmptyModel());

        Assert.Contains("cress run", html);
    }

    [Fact]
    public void Executive_RichModel_ContainsPassRateRing()
    {
        var model = Factories.RichModel(passRate: 0.75);
        var html = _renderer.RenderEmbedded("executive", model);

        // SVG circle element for the ring should be present
        Assert.Contains("<circle", html);
        Assert.Contains("pass-rate-ring", html);
    }

    [Fact]
    public void Executive_RichModel_RunHistoryBarsPresent()
    {
        var model = Factories.RichModel();
        var html = _renderer.RenderEmbedded("executive", model);

        Assert.Contains("run-bar", html);
    }

    [Fact]
    public void Executive_Branding_LogoUrlRendered()
    {
        var model = Factories.RichModel();
        var html = _renderer.RenderEmbedded("executive", model);

        Assert.Contains("https://example.com/logo.png", html);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// TemplateRenderer — other built-in templates (smoke tests)
// ──────────────────────────────────────────────────────────────────────────────

public sealed class TemplateRendererOtherTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void Technical_EmptyModel_RendersWithoutError()
    {
        var html = _renderer.RenderEmbedded("technical", Factories.EmptyModel());

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("<title>", html);
    }

    [Fact]
    public void Public_EmptyModel_RendersWithoutError()
    {
        var html = _renderer.RenderEmbedded("public", Factories.EmptyModel());

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("<title>", html);
    }

    [Fact]
    public void Public_AllPass_ShowsOperationalBanner()
    {
        // Suite with 0 failed runs → "All Systems Operational"
        var model = new DocumentModel(
            Meta: new DocumentMeta("P", "now", "1.0", ""),
            Branding: new DocumentBranding("T", null, "#6366f1"),
            Suite: new SuiteSummary(5, 5, 0, 1.0, TimeSpan.FromSeconds(3)),
            Flows: [],
            Metrics: null,
            RecentRuns: [],
            Screenshots: [],
            Diagnostics: []);

        var html = _renderer.RenderEmbedded("public", model);

        Assert.Contains("All Systems Operational", html);
    }

    [Fact]
    public void UnknownEmbeddedTemplate_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _renderer.RenderEmbedded("nonexistent", Factories.EmptyModel()));
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// TemplateRenderer — V17 Technical template (full implementation)
// ──────────────────────────────────────────────────────────────────────────────

file static class V17Factories
{
    /// <summary>RunMetrics with flow + step data for technical template tests.</summary>
    public static RunMetrics SampleMetrics(string flowId = "flow-001") =>
        new RunMetrics(
            Suite: new SuiteMetrics(10, 8, 2, 0.8, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(12)),
            Flows:
            [
                new FlowMetrics(
                    FlowId: flowId,
                    Runs: 10,
                    Passed: 8,
                    Failed: 2,
                    PassRate: 0.8,
                    FlakeRate: 0.1,
                    AvgDuration: TimeSpan.FromSeconds(1.2),
                    P50: TimeSpan.FromSeconds(1.0),
                    P95: TimeSpan.FromSeconds(2.5),
                    P99: TimeSpan.FromSeconds(3.8),
                    MTTR: null)
            ],
            Steps:
            [
                new StepMetrics(
                    FlowId: flowId,
                    StepIndex: 0,
                    StepName: "Navigate to home",
                    Runs: 10,
                    Passed: 10,
                    Failed: 0,
                    AvgDuration: TimeSpan.FromMilliseconds(400),
                    P50: TimeSpan.FromMilliseconds(380),
                    P95: TimeSpan.FromMilliseconds(800),
                    P99: TimeSpan.FromMilliseconds(950)),
                new StepMetrics(
                    FlowId: flowId,
                    StepIndex: 1,
                    StepName: "Click login",
                    Runs: 10,
                    Passed: 8,
                    Failed: 2,
                    AvgDuration: TimeSpan.FromMilliseconds(200),
                    P50: TimeSpan.FromMilliseconds(190),
                    P95: TimeSpan.FromMilliseconds(500),
                    P99: TimeSpan.FromMilliseconds(700))
            ],
            Trend: []);

    public static DocumentModel ModelWithMetrics(string flowId = "flow-001", string accent = "#6366f1") =>
        new DocumentModel(
            Meta: new DocumentMeta("TestProject", "2026-01-01 00:00:00 UTC", "1.0", "abc1234"),
            Branding: new DocumentBranding("My Suite", null, accent),
            Suite: new SuiteSummary(1, 8, 2, 0.8, TimeSpan.FromSeconds(12)),
            Flows:
            [
                new FlowSummary(
                    Id: flowId,
                    Name: "Login flow",
                    Description: null,
                    Status: "passing",
                    PassRate: 0.8,
                    AvgDuration: TimeSpan.FromSeconds(1.2))
            ],
            Metrics: SampleMetrics(flowId),
            RecentRuns: Enumerable.Range(0, 5).Select(i => new RunHistoryEntry(
                RunId: $"run-{i:000}",
                Timestamp: DateTimeOffset.UtcNow.AddHours(-i),
                PassedCount: 3 - (i % 2),
                FailedCount: i % 2)).ToList(),
            Screenshots:
            [
                new ScreenshotEvidence(flowId, "Navigate to home", "screenshots/flow-001-step0.png"),
                new ScreenshotEvidence(flowId, "Click login", "screenshots/flow-001-step1.png")
            ],
            Diagnostics:
            [
                new DiagnosticEntry("Error", flowId, "Step timed out after 5s"),
                new DiagnosticEntry("Warning", flowId, "Slow selector detected"),
                new DiagnosticEntry("Info", flowId, "Retry succeeded on attempt 2")
            ]);
}

public sealed class TechnicalTemplateTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void Technical_RichModel_RendersWithoutError()
    {
        var html = _renderer.RenderEmbedded("technical", V17Factories.ModelWithMetrics());

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
    }

    [Fact]
    public void Technical_EmptyModel_RendersCleanly()
    {
        // Must not throw even with no flows, no metrics, no runs
        var html = _renderer.RenderEmbedded("technical", Factories.EmptyModel());

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("cress run", html);
    }

    [Fact]
    public void Technical_StepBars_PresentWhenMetricsHasStepData()
    {
        var html = _renderer.RenderEmbedded("technical", V17Factories.ModelWithMetrics());

        // Per-step timing section renders expandable details with the bar elements
        Assert.Contains("step-bar-fill", html);
        Assert.Contains("step-bar-wrap", html);
    }

    [Fact]
    public void Technical_StepTable_ContainsStepNames()
    {
        var html = _renderer.RenderEmbedded("technical", V17Factories.ModelWithMetrics());

        Assert.Contains("Navigate to home", html);
        Assert.Contains("Click login", html);
    }

    [Fact]
    public void Technical_Screenshots_RenderedAsImages()
    {
        var html = _renderer.RenderEmbedded("technical", V17Factories.ModelWithMetrics());

        Assert.Contains("<img", html);
        Assert.Contains("screenshots/flow-001-step0.png", html);
        Assert.Contains("screenshots/flow-001-step1.png", html);
    }

    [Fact]
    public void Technical_Diagnostics_AllSeveritiesPresent()
    {
        var html = _renderer.RenderEmbedded("technical", V17Factories.ModelWithMetrics());

        Assert.Contains("sev-error", html);
        Assert.Contains("sev-warning", html);
        Assert.Contains("sev-info", html);
    }

    [Fact]
    public void Technical_AccentColor_AppearsInCss()
    {
        const string accent = "#ff6600";
        var html = _renderer.RenderEmbedded("technical", V17Factories.ModelWithMetrics(accent: accent));

        var styleBlock = System.Text.RegularExpressions.Regex.Match(
            html, @"<style>.*?</style>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.Contains(accent, styleBlock);
    }

    [Fact]
    public void Technical_RunHistory_ShowsUpTo20Rows()
    {
        // Model with 25 runs — table must cap at 20
        var model = new DocumentModel(
            Meta: new DocumentMeta("P", "now", "1.0", ""),
            Branding: new DocumentBranding("T", null, "#6366f1"),
            Suite: new SuiteSummary(1, 20, 5, 0.8, TimeSpan.FromSeconds(60)),
            Flows: [],
            Metrics: null,
            RecentRuns: Enumerable.Range(0, 25).Select(i => new RunHistoryEntry(
                $"run-{i:000}", DateTimeOffset.UtcNow.AddHours(-i), 3, i % 3 == 0 ? 1 : 0)).ToList(),
            Screenshots: [],
            Diagnostics: []);

        var html = _renderer.RenderEmbedded("technical", model);

        // Should contain run-000 through run-019 but not run-020+
        Assert.Contains("run-019", html);
        Assert.DoesNotContain("run-020", html);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// TemplateRenderer — V17 Public template (full implementation)
// ──────────────────────────────────────────────────────────────────────────────

public sealed class PublicTemplateTests
{
    private readonly TemplateRenderer _renderer = new();

    private static DocumentModel MakePublicModel(
        double passRate,
        IReadOnlyList<FlowSummary>? flows = null,
        int recentRunCount = 0,
        string accent = "#6366f1") =>
        new DocumentModel(
            Meta: new DocumentMeta("P", "2026-01-01", "1.0", ""),
            Branding: new DocumentBranding("MyApp Status", null, accent),
            Suite: new SuiteSummary(
                TotalFlows: flows?.Count ?? 0,
                PassedRuns: (int)(100 * passRate),
                FailedRuns: 100 - (int)(100 * passRate),
                PassRate: passRate,
                AvgDuration: TimeSpan.FromSeconds(3)),
            Flows: flows ?? [],
            Metrics: null,
            RecentRuns: Enumerable.Range(0, recentRunCount).Select(i => new RunHistoryEntry(
                $"run-{i}", DateTimeOffset.UtcNow.AddHours(-i),
                PassedCount: (int)(passRate * 5),
                FailedCount: 5 - (int)(passRate * 5))).ToList(),
            Screenshots: [],
            Diagnostics: []);

    [Fact]
    public void Public_Operational_Banner_When_PassRate_AtLeast_99()
    {
        var html = _renderer.RenderEmbedded("public", MakePublicModel(1.0));

        Assert.Contains("All Systems Operational", html);
    }

    [Fact]
    public void Public_MinorIssues_Banner_When_PassRate_Between_95_And_99()
    {
        var html = _renderer.RenderEmbedded("public", MakePublicModel(0.97));

        Assert.Contains("Minor Issues Detected", html);
        Assert.DoesNotContain("All Systems Operational", html);
    }

    [Fact]
    public void Public_Degraded_Banner_When_PassRate_Below_95()
    {
        var html = _renderer.RenderEmbedded("public", MakePublicModel(0.80));

        Assert.Contains("Service Degraded", html);
        Assert.DoesNotContain("All Systems Operational", html);
        Assert.DoesNotContain("Minor Issues Detected", html);
    }

    [Fact]
    public void Public_DraftFlows_AreHidden()
    {
        var flows = new List<FlowSummary>
        {
            new FlowSummary("flow-pub", "Public Login", null, "passing", 1.0, TimeSpan.FromSeconds(1)),
            new FlowSummary("flow-draft", "Internal Beta", null, "draft", 0.5, TimeSpan.FromSeconds(2))
        };
        var html = _renderer.RenderEmbedded("public", MakePublicModel(0.99, flows));

        Assert.Contains("Public Login", html);
        Assert.DoesNotContain("Internal Beta", html);
    }

    [Fact]
    public void Public_SparklineSvg_HasCorrectPointCount()
    {
        // 10 recent runs → polyline should have 10 coordinate pairs
        const int runCount = 10;
        var html = _renderer.RenderEmbedded("public", MakePublicModel(1.0, recentRunCount: runCount));

        // Each run contributes one x,y pair to the polyline's points attribute
        var polyMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<polyline[^>]*points=""([^""]+)""");
        Assert.True(polyMatch.Success, "polyline element expected in SVG sparkline");

        var pairs = polyMatch.Groups[1].Value.Trim().Split(' ',
            System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(runCount, pairs.Length);
    }

    [Fact]
    public void Public_AccentColor_AppearsInCss()
    {
        const string accent = "#ff6600";
        var html = _renderer.RenderEmbedded("public", MakePublicModel(1.0, accent: accent));

        var styleBlock = System.Text.RegularExpressions.Regex.Match(
            html, @"<style>.*?</style>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.Contains(accent, styleBlock);
    }

    [Fact]
    public void Public_NoInternals_NoMetrics_NoScreenshots()
    {
        var html = _renderer.RenderEmbedded("public", MakePublicModel(1.0));

        // Must not expose any internal/diagnostic information
        Assert.DoesNotContain("screenshot", html);
        Assert.DoesNotContain("diagnostic", html);
        Assert.DoesNotContain("P95", html);
        Assert.DoesNotContain("FlakeRate", html);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Round-trip: render → parse for known anchor strings
// ──────────────────────────────────────────────────────────────────────────────

public sealed class RoundTripTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void Executive_RoundTrip_TitleInHead()
    {
        const string title = "Httpbin smoke status";
        var model = Factories.EmptyModel(title: title);
        var html = _renderer.RenderEmbedded("executive", model);

        // <title> must be in <head>, not just anywhere
        var headBlock = Regex.Match(html, @"<head>.*?</head>", RegexOptions.Singleline).Value;
        Assert.Contains($"<title>{title}</title>", headBlock);
    }

    [Fact]
    public void Executive_RoundTrip_AccentColorInStyle()
    {
        const string accent = "#ab12cd";
        var html = _renderer.RenderEmbedded("executive", Factories.EmptyModel(accent: accent));

        // accent must appear inside a <style> block — it's written directly into the CSS --accent variable
        var styleBlock = Regex.Match(html, @"<style>.*?</style>", RegexOptions.Singleline).Value;
        Assert.Contains(accent, styleBlock);
    }

    [Fact]
    public void Executive_RoundTrip_ValidHtmlStructure()
    {
        var html = _renderer.RenderEmbedded("executive", Factories.RichModel());

        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
        Assert.Contains("<html", html);
        Assert.Contains("</html>", html);
        Assert.Contains("<head>", html);
        Assert.Contains("</head>", html);
        Assert.Contains("<body>", html);
        Assert.Contains("</body>", html);
    }
}
