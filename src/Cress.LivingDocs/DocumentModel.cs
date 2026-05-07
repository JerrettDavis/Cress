using Cress.Studio.Services;

namespace Cress.LivingDocs;

/// <summary>Top-level data shape passed to every living doc template.</summary>
public sealed record DocumentModel(
    DocumentMeta Meta,
    DocumentBranding Branding,
    SuiteSummary Suite,
    IReadOnlyList<FlowSummary> Flows,
    RunMetrics? Metrics,
    IReadOnlyList<RunHistoryEntry> RecentRuns,
    IReadOnlyList<ScreenshotEvidence> Screenshots,
    IReadOnlyList<DiagnosticEntry> Diagnostics);

public sealed record DocumentMeta(
    string ProjectName,
    string GeneratedAt,
    string Version,
    string GitSha);

public sealed record DocumentBranding(
    string Title,
    string? LogoUrl,
    string AccentColor);

public sealed record SuiteSummary(
    int TotalFlows,
    int PassedRuns,
    int FailedRuns,
    double PassRate,
    TimeSpan AvgDuration);

public sealed record FlowSummary(
    string Id,
    string Name,
    string? Description,
    string Status,
    double PassRate,
    TimeSpan AvgDuration);

public sealed record RunHistoryEntry(
    string RunId,
    DateTimeOffset Timestamp,
    int PassedCount,
    int FailedCount);

public sealed record ScreenshotEvidence(
    string FlowId,
    string StepName,
    string FilePath);

public sealed record DiagnosticEntry(
    string Severity,
    string FlowId,
    string Message);
