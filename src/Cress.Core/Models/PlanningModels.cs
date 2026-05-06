namespace Cress.Core.Models;

public record PlanCollection
{
    public List<ExecutionPlan> Plans { get; init; } = [];
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error) && Plans.Count > 0;
}

public record ExecutionPlan
{
    public string FlowId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? CapabilityId { get; init; }
    public string? SourceFile { get; init; }
    public TraceabilityInfo? Traceability { get; init; }
    public List<string> RequiredDrivers { get; init; } = [];
    public List<PlanAction> Actions { get; init; } = [];
}

public record PlanAction
{
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Step { get; init; }
    public string? Driver { get; init; }
    public string? Plugin { get; init; }
    public string? Operation { get; init; }
    public string? Fixture { get; init; }
    public string? Owner { get; init; }
    public bool RetrySafe { get; init; }
    public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
