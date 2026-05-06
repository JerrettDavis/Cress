namespace Cress.Core.Models;

public enum RunOutcome
{
    Passed,
    Failed,
    Skipped,
    Blocked,
    Errored
}

public record RunOptions
{
    public string? FlowPath { get; init; }
    public IReadOnlyList<string> FlowPaths { get; init; } = [];
    public string? Tag { get; init; }
    public string? Profile { get; init; }
    public int? Parallel { get; init; }
    public bool ContinueOnFailure { get; init; }
    public bool DryRun { get; init; }
    public string? EvidenceModeOverride { get; init; }
    public string? ScreenshotPolicyOverride { get; init; }
    public string? StartFromStep { get; init; }
    public int? RetryCountOverride { get; init; }
    public string? Trigger { get; init; }
    public IReadOnlyList<string> ReportFormats { get; init; } = [];
}

public record RunResult
{
    public int Version { get; init; } = 1;
    public RunMetadata Metadata { get; init; } = new();
    public RunInvocation Invocation { get; init; } = new();
    public IReadOnlyList<FlowRunResult> Flows { get; init; } = [];
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyDictionary<string, string> Reports { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public ArtifactIndex ArtifactIndex { get; init; } = new();
    public bool Passed => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error) && Flows.All(flow => flow.Outcome == RunOutcome.Passed);
}

public record RunMetadata
{
    public string RunId { get; init; } = string.Empty;
    public string ArtifactRoot { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string Profile { get; init; } = string.Empty;
    public string? Environment { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public double DurationMs { get; init; }
}

public record RunInvocation
{
    public string Trigger { get; init; } = "manual";
    public IReadOnlyList<string> RequestedFlows { get; init; } = [];
    public string? Tag { get; init; }
    public string? StartFromStep { get; init; }
    public int RetryCount { get; init; }
    public string EvidenceMode { get; init; } = "standard";
    public string ScreenshotPolicy { get; init; } = "on-failure";
}

public record FlowRunResult
{
    public string FlowId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? CapabilityId { get; init; }
    public string? SourceFile { get; init; }
    public RunOutcome Outcome { get; init; }
    public string? FailureMessage { get; init; }
    public string? FailureClassification { get; init; }
    public bool CleanupFailed { get; init; }
    public bool PassedWithRetry { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public double DurationMs { get; init; }
    public IReadOnlyList<string> Drivers { get; init; } = [];
    public TraceabilityInfo? Traceability { get; init; }
    public IReadOnlyList<StepRunResult> Steps { get; init; } = [];
}

public record StepRunResult
{
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Driver { get; init; }
    public string? Owner { get; init; }
    public RunOutcome Outcome { get; init; }
    public string? Message { get; init; }
    public string? FailureClassification { get; init; }
    public int Attempt { get; init; } = 1;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public double DurationMs { get; init; }
    public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<EvidenceArtifact> Artifacts { get; init; } = [];
}

public record DriverExecutionResult
{
    public RunOutcome Outcome { get; init; }
    public string? Message { get; init; }
    public string? FailureClassification { get; init; }
    public IReadOnlyDictionary<string, string> Outputs { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<EvidenceArtifact> Artifacts { get; init; } = [];
}

public record EvidenceArtifact
{
    public string Category { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? MediaType { get; init; }
    public long? SizeBytes { get; init; }
}

public record ArtifactIndex
{
    public Dictionary<string, List<string>> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
