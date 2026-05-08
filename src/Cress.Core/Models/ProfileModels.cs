using YamlDotNet.Serialization;

namespace Cress.Core.Models;

public record CressProfile
{
    public string Profile { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
    public TimeoutsConfig? Timeouts { get; init; }
    public EvidenceProfileConfig? Evidence { get; init; }
    public SecretsConfig? Secrets { get; init; }
    public AuthenticationConfig? Authentication { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public PlaywrightProfileConfig? Playwright { get; init; }
    [YamlMember(Alias = "flawright")]
    public FlawrightProfileConfig? Flawright { get; init; }
    public Dictionary<string, string>? Variables { get; init; }
    public FlakeProfileConfig? Flake { get; init; }
}

public record TimeoutsConfig
{
    public int? Step { get; init; }
    public int? Expectation { get; init; }
    public int? Driver { get; init; }
}

public record EvidenceProfileConfig
{
    public string? Mode { get; init; }
    public bool? Screenshots { get; init; }
    public string? ScreenshotPolicy { get; init; }
}

public record SecretsConfig
{
    public List<string>? Required { get; init; }
}

public record AuthenticationConfig
{
    public string? Header { get; init; }
    public string? Scheme { get; init; }
    public string? Token { get; init; }
    public string? TokenEnvironmentVariable { get; init; }
}

public record PlaywrightProfileConfig
{
    public bool? Simulated { get; init; }
    public bool? Headless { get; init; }
    public string? Browser { get; init; }
}

public record FlawrightProfileConfig
{
    public string? ApplicationPath { get; init; }
    public string? Arguments { get; init; }
    public string? WindowTitle { get; init; }
    public int? LaunchTimeoutMs { get; init; }
}

/// <summary>Profile-level overrides for flake detection parameters. Any non-null value overrides the project-level FlakeConfig.</summary>
public record FlakeProfileConfig
{
    public int? Window { get; init; }
    public int? MinPasses { get; init; }
    public int? MinFails { get; init; }
    public double? Threshold { get; init; }
}
