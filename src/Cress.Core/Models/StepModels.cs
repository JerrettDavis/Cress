using YamlDotNet.Serialization;

namespace Cress.Core.Models;

public record StepManifest
{
    public int Version { get; init; }
    public List<StepDefinition> Steps { get; init; } = [];

    [YamlIgnore]
    public string? SourceFile { get; init; }
}

public record StepDefinition
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public List<string> Aliases { get; init; } = [];
    public Dictionary<string, StepContractField>? Inputs { get; init; }
    public Dictionary<string, StepContractField>? Outputs { get; init; }
    public List<string>? Preconditions { get; init; }
    public List<string>? Effects { get; init; }
    public List<string> Drivers { get; init; } = [];
    public string? Idempotency { get; init; }
    public bool RetrySafe { get; init; }

    [YamlMember(Alias = "timeout")]
    public int? TimeoutMs { get; init; }

    public string? Owner { get; init; }
    public int? Version { get; init; }
    public StepImplementationBinding? Implementation { get; init; }

    [YamlIgnore]
    public string? SourceFile { get; init; }
}

public record StepContractField
{
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
    public string? Description { get; init; }
}

public record StepImplementationBinding
{
    public string? Plugin { get; init; }
    public string? Operation { get; init; }
}
