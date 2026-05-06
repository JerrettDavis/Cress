using Cress.Core.Models;
using Cress.ProjectSystem;
using Cress.Specs;

namespace Cress.Execution;

public sealed class ProjectCatalogService
{
    private readonly ProjectLocator _projectLocator;
    private readonly ConfigLoader _configLoader;
    private readonly ProfileLoader _profileLoader;
    private readonly FlowParser _flowParser;
    private readonly FlowNormalizer _flowNormalizer;
    private readonly CapabilityParser _capabilityParser;
    private readonly StepManifestParser _stepManifestParser;
    private readonly FixtureManifestParser _fixtureManifestParser;
    private readonly StepRegistry _stepRegistry;

    public ProjectCatalogService(
        ProjectLocator projectLocator,
        ConfigLoader configLoader,
        ProfileLoader profileLoader,
        FlowParser flowParser,
        FlowNormalizer flowNormalizer,
        CapabilityParser capabilityParser,
        StepManifestParser stepManifestParser,
        FixtureManifestParser fixtureManifestParser,
        StepRegistry stepRegistry)
    {
        _projectLocator = projectLocator;
        _configLoader = configLoader;
        _profileLoader = profileLoader;
        _flowParser = flowParser;
        _flowNormalizer = flowNormalizer;
        _capabilityParser = capabilityParser;
        _stepManifestParser = stepManifestParser;
        _fixtureManifestParser = fixtureManifestParser;
        _stepRegistry = stepRegistry;
    }

    public OperationResult<ProjectCatalog> Load(string startDirectory, string? profileName = null, bool strict = false)
    {
        var diagnostics = new List<Diagnostic>();
        var projectRoot = _projectLocator.FindProjectRoot(startDirectory);
        if (projectRoot is null)
        {
            return new OperationResult<ProjectCatalog>
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "PRJ001",
                        Message = "Could not locate a Cress project root.",
                        File = startDirectory
                    }
                ]
            };
        }

        var configResult = _configLoader.Load(projectRoot, strict);
        diagnostics.AddRange(configResult.Diagnostics);
        if (configResult.Value is null)
        {
            return new OperationResult<ProjectCatalog> { Diagnostics = diagnostics };
        }

        var profileResult = _profileLoader.LoadActive(projectRoot, configResult.Value, profileName, strict);
        diagnostics.AddRange(profileResult.Diagnostics);
        if (profileResult.Value is null)
        {
            return new OperationResult<ProjectCatalog> { Diagnostics = diagnostics };
        }

        var effectiveConfig = new EffectiveConfig
        {
            Config = configResult.Value,
            ActiveProfile = profileResult.Value.Profile,
            Profile = profileResult.Value
        };

        var profileResults = _profileLoader.LoadAll(projectRoot, strict);
        diagnostics.AddRange(profileResults.SelectMany(result => result.Diagnostics));

        var parsedFlows = Discover(projectRoot, configResult.Value.Paths.Flows, "*.flow.yaml")
            .Select(path => _flowParser.ParseFile(path, strict))
            .ToList();
        diagnostics.AddRange(parsedFlows.SelectMany(result => result.Diagnostics));

        var flows = parsedFlows.Where(result => result.Value is not null).Select(result => result.Value!).ToList();
        foreach (var duplicate in flows.GroupBy(flow => flow.Id, StringComparer.OrdinalIgnoreCase).Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "FLW011",
                Message = $"Duplicate flow id '{duplicate.Key}' was found.",
                File = duplicate.First().SourceFile
            });
        }

        var normalizedFlows = flows.Select(flow => _flowNormalizer.Normalize(flow)).ToList();
        diagnostics.AddRange(normalizedFlows.SelectMany(result => result.Diagnostics));

        var parsedCapabilities = Discover(projectRoot, configResult.Value.Paths.Capabilities, "*.md")
            .Select(path => _capabilityParser.ParseFile(path, strict))
            .ToList();
        diagnostics.AddRange(parsedCapabilities.SelectMany(result => result.Diagnostics));

        var capabilities = parsedCapabilities.Where(result => result.Value is not null).Select(result => result.Value!).ToList();
        foreach (var duplicate in capabilities.GroupBy(capability => capability.Id, StringComparer.OrdinalIgnoreCase).Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "CAP006",
                Message = $"Duplicate capability id '{duplicate.Key}' was found.",
                File = duplicate.First().SourceFile
            });
        }

        var stepManifests = Discover(projectRoot, configResult.Value.Paths.Steps, "*.yaml")
            .Select(path => _stepManifestParser.ParseFile(path, strict))
            .ToList();
        diagnostics.AddRange(stepManifests.SelectMany(result => result.Diagnostics));

        var stepRegistryResult = _stepRegistry.Build(stepManifests.Where(result => result.Value is not null).Select(result => result.Value!));
        diagnostics.AddRange(stepRegistryResult.Diagnostics);

        var fixtureManifests = Discover(projectRoot, configResult.Value.Paths.Fixtures, "*.yaml")
            .Select(path => _fixtureManifestParser.ParseFile(path, strict))
            .ToList();
        diagnostics.AddRange(fixtureManifests.SelectMany(result => result.Diagnostics));

        var fixtures = new Dictionary<string, FixtureDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in fixtureManifests.Where(result => result.Value is not null).Select(result => result.Value!))
        {
            foreach (var fixture in manifest.Fixtures)
            {
                if (!fixtures.TryAdd(fixture.Key, fixture.Value))
                {
                    diagnostics.Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "FIX005",
                        Message = $"Duplicate fixture '{fixture.Key}' was found.",
                        File = fixture.Value.SourceFile
                    });
                }
            }
        }

        return new OperationResult<ProjectCatalog>
        {
            Value = new ProjectCatalog
            {
                ProjectRoot = projectRoot,
                EffectiveConfig = effectiveConfig,
                Flows = flows,
                NormalizedFlows = normalizedFlows.Where(result => result.Value is not null).Select(result => result.Value!).ToList(),
                Capabilities = capabilities,
                Profiles = profileResults.Where(result => result.Value is not null).Select(result => result.Value!).ToList(),
                StepRegistry = stepRegistryResult.Value ?? StepRegistrySnapshot.Empty,
                FixtureDefinitions = fixtures
            },
            Diagnostics = diagnostics
        };
    }

    public IReadOnlyList<NormalizedFlow> SelectFlows(ProjectCatalog catalog, string? flowPath, string? tag, ICollection<Diagnostic>? diagnostics = null)
    {
        IEnumerable<NormalizedFlow> flows = catalog.NormalizedFlows;
        if (!string.IsNullOrWhiteSpace(flowPath))
        {
            var expectedPath = Path.IsPathRooted(flowPath)
                ? Path.GetFullPath(flowPath)
                : Path.GetFullPath(Path.Combine(catalog.ProjectRoot, flowPath));
            flows = flows.Where(flow => flow.SourceFile is not null && Path.GetFullPath(flow.SourceFile).Equals(expectedPath, StringComparison.OrdinalIgnoreCase));

            if (!flows.Any())
            {
                diagnostics?.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "SEL001",
                    Message = "No flow matched the requested path.",
                    File = flowPath
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            flows = flows.Where(flow => flow.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        }

        return flows.OrderBy(flow => flow.FlowId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> Discover(string projectRoot, string relativePath, string searchPattern)
    {
        var path = Path.Combine(projectRoot, relativePath);
        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories)
            : [];
    }
}

public sealed record ProjectCatalog
{
    public string ProjectRoot { get; init; } = string.Empty;
    public EffectiveConfig EffectiveConfig { get; init; } = new();
    public IReadOnlyList<CressFlow> Flows { get; init; } = [];
    public IReadOnlyList<NormalizedFlow> NormalizedFlows { get; init; } = [];
    public IReadOnlyList<CressCapability> Capabilities { get; init; } = [];
    public IReadOnlyList<CressProfile> Profiles { get; init; } = [];
    public StepRegistrySnapshot StepRegistry { get; init; } = StepRegistrySnapshot.Empty;
    public IReadOnlyDictionary<string, FixtureDefinition> FixtureDefinitions { get; init; } = new Dictionary<string, FixtureDefinition>(StringComparer.OrdinalIgnoreCase);
}
