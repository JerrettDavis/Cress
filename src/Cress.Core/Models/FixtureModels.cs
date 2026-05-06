using YamlDotNet.Serialization;

namespace Cress.Core.Models;

public record FixtureManifest
{
    public int Version { get; init; }
    public Dictionary<string, FixtureDefinition> Fixtures { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [YamlIgnore]
    public string? SourceFile { get; init; }
}

public record FixtureDefinition
{
    public string Type { get; init; } = string.Empty;
    public string Strategy { get; init; } = "static";
    public List<string> Traits { get; init; } = [];
    public string Cleanup { get; init; } = "on-success";
    public FixtureProviderBinding? Provider { get; init; }

    [YamlIgnore]
    public string Name { get; init; } = string.Empty;

    [YamlIgnore]
    public string? SourceFile { get; init; }
}

public record FixtureProviderBinding
{
    public string? Plugin { get; init; }
    public string? Operation { get; init; }
}

public record ResolvedFixture
{
    public string Alias { get; init; } = string.Empty;
    public string DefinitionName { get; init; } = string.Empty;
    public FixtureDefinition Definition { get; init; } = new();
    public Dictionary<string, string> Bindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
