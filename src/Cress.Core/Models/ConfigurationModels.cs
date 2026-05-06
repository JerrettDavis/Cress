using YamlDotNet.Serialization;

namespace Cress.Core.Models;

public record CressConfig
{
    public int Version { get; init; }
    public ProjectConfig Project { get; init; } = new();
    public PathsConfig Paths { get; init; } = new();
    public DefaultsConfig Defaults { get; init; } = new();
    public FlakeConfig Flake { get; init; } = new();
    public PluginsConfig Plugins { get; init; } = new();
    public Dictionary<string, DriverConfig> Drivers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public record ProjectConfig
{
    public string Name { get; init; } = string.Empty;
    public string DefaultProfile { get; init; } = "local";
}

public record PathsConfig
{
    public string Capabilities { get; init; } = "capabilities";
    public string Flows { get; init; } = "flows";
    public string Models { get; init; } = "models";
    public string Fixtures { get; init; } = "fixtures";
    public string Steps { get; init; } = "steps";
    public string Artifacts { get; init; } = "artifacts/runs";
    public string Reports { get; init; } = "reports";
}

public record DefaultsConfig
{
    public int Timeout { get; init; } = 30000;
    public int Retries { get; init; }
    public string Evidence { get; init; } = "standard";
    public string Cleanup { get; init; } = "on-success";
}

public record PluginsConfig
{
    public List<string> Discover { get; init; } = [];
}

public record DriverConfig
{
    public bool Enabled { get; init; }
    public string? Config { get; init; }
}

public record FlakeConfig
{
    /// <summary>Number of most-recent runs to include in flake analysis. Default: 25.</summary>
    public int Window { get; init; } = 25;

    /// <summary>Minimum number of passing runs within the window required to classify a flow as flaky. Default: 1.</summary>
    public int MinPasses { get; init; } = 1;

    /// <summary>Minimum number of failing runs within the window required to classify a flow as flaky. Default: 1.</summary>
    public int MinFails { get; init; } = 1;

    /// <summary>Failure rate threshold (0.0–1.0). A flow is flagged as flaky only when failCount / windowCount >= threshold. Default: 0.10.</summary>
    public double Threshold { get; init; } = 0.10;
}

public record EffectiveConfig
{
    public CressConfig Config { get; init; } = new();
    public string ActiveProfile { get; init; } = "local";
    public CressProfile Profile { get; init; } = new();

    /// <summary>
    /// Returns the effective flake configuration, merging project-level defaults with any
    /// profile-level overrides (profile wins where non-null).
    /// </summary>
    public FlakeConfig ResolvedFlake
    {
        get
        {
            var profileFlake = Profile.Flake;
            if (profileFlake is null)
            {
                return Config.Flake;
            }

            return Config.Flake with
            {
                Window = profileFlake.Window ?? Config.Flake.Window,
                MinPasses = profileFlake.MinPasses ?? Config.Flake.MinPasses,
                MinFails = profileFlake.MinFails ?? Config.Flake.MinFails,
                Threshold = profileFlake.Threshold ?? Config.Flake.Threshold
            };
        }
    }
}
