using YamlDotNet.Serialization;

namespace Cress.Core.Models;

public record CressFlow
{
    public int Version { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "capability")]
    public string? CapabilityId { get; init; }

    public string? Summary { get; init; }
    public List<string> Tags { get; init; } = [];
    public TraceabilityInfo? Traceability { get; init; }
    public Dictionary<string, string>? Personas { get; init; }
    public Dictionary<string, FlowFixtureRef>? Fixtures { get; init; }
    public List<string>? Given { get; init; }
    public List<FlowAction> When { get; init; } = [];
    public List<FlowExpectation> Then { get; init; } = [];
    public string? Status { get; init; }

    [YamlIgnore]
    public string? SourceFile { get; init; }
}

public record TraceabilityInfo
{
    public string? Requirement { get; init; }
    public List<string>? AcceptanceCriteria { get; init; }
    public string? Owner { get; init; }
    public string? Risk { get; init; }
}

public record FlowFixtureRef
{
    public string? Use { get; init; }
    public string? Source { get; init; }

    [YamlMember(Alias = "for")]
    public string? For { get; init; }
}

public record FlowAction
{
    [YamlMember(Alias = "step")]
    public string Step { get; init; } = string.Empty;

    public Dictionary<string, string>? With { get; init; }
}

public record FlowExpectation
{
    [YamlMember(Alias = "expect")]
    public string Expect { get; init; } = string.Empty;

    public Dictionary<string, string>? With { get; init; }
}

public record CressCapability
{
    public int Version { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Owner { get; init; }
    public string? Risk { get; init; }
    public List<string> Tags { get; init; } = [];

    [YamlIgnore]
    public string? SourceFile { get; init; }

    public List<string>? Rules { get; init; }
    public List<AcceptanceCriterion>? AcceptanceCriteria { get; init; }
}

public record AcceptanceCriterion
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
