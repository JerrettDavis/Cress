namespace Cress.Core.Models;

public record NormalizedFlow
{
    public int Version { get; init; } = 1;
    public string FlowId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? CapabilityId { get; init; }
    public string? Summary { get; init; }
    public List<string> Tags { get; init; } = [];
    public TraceabilityInfo? Traceability { get; init; }
    public Dictionary<string, string> Personas { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<NormalizedFixture> Fixtures { get; init; } = [];
    public List<NormalizedExecutable> Actions { get; init; } = [];
    public List<NormalizedExecutable> Expectations { get; init; } = [];
    public string? Status { get; init; }
    public string? SourceFile { get; init; }
}

public record NormalizedFixture
{
    public string Name { get; init; } = string.Empty;
    public string? Use { get; init; }
    public string? Source { get; init; }
    public Dictionary<string, string> Bindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public record NormalizedExecutable
{
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public SourceReference Source { get; init; } = new();
}

public record SourceReference
{
    public string Section { get; init; } = string.Empty;
    public int Index { get; init; }
    public string? SourceFile { get; init; }
}
