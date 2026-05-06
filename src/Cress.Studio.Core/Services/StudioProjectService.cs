using Cress.Core.Models;
using Cress.Execution;
using Cress.Validation;

namespace Cress.Studio.Services;

public sealed class StudioProjectService
{
    private readonly ProjectCatalogService _catalogService;
    private readonly ProjectValidator _validator;
    private readonly RunResultRepository _runResultRepository;
    private readonly StudioRunInsightsService _runInsightsService;

    public StudioProjectService(
        ProjectCatalogService catalogService,
        ProjectValidator validator,
        RunResultRepository runResultRepository,
        StudioRunInsightsService runInsightsService)
    {
        _catalogService = catalogService;
        _validator = validator;
        _runResultRepository = runResultRepository;
        _runInsightsService = runInsightsService;
    }

    public OperationResult<StudioProjectSnapshot> Load(string startDirectory, string? profileName = null)
    {
        var catalogResult = _catalogService.Load(startDirectory, profileName);
        var diagnostics = new List<Diagnostic>(catalogResult.Diagnostics);
        if (catalogResult.Value is null)
        {
            return new OperationResult<StudioProjectSnapshot>
            {
                Diagnostics = diagnostics
            };
        }

        var validation = _validator.Validate(catalogResult.Value.ProjectRoot);
        diagnostics.AddRange(validation.Diagnostics);

        var effectiveConfig = catalogResult.Value.EffectiveConfig;
        var flakeConfig = effectiveConfig.ResolvedFlake;

        var runs = _runResultRepository.ListRuns(
            catalogResult.Value.ProjectRoot,
            effectiveConfig.Config.Paths.Artifacts,
            flakeConfig.Window);

        return new OperationResult<StudioProjectSnapshot>
        {
            Value = new StudioProjectSnapshot
            {
                Catalog = catalogResult.Value,
                Runs = runs,
                CapabilityCoverage = BuildCoverage(catalogResult.Value, runs),
                RunInsights = _runInsightsService.Analyze(catalogResult.Value, runs, flakeConfig)
            },
            Diagnostics = diagnostics
        };
    }

    private static IReadOnlyList<StudioCapabilityCoverage> BuildCoverage(ProjectCatalog catalog, IReadOnlyList<StoredRunResult> runs)
    {
        var latestRun = runs.FirstOrDefault()?.Result;
        var flowLookup = latestRun?.Flows.ToDictionary(flow => flow.FlowId, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, FlowRunResult>(StringComparer.OrdinalIgnoreCase);

        return catalog.Capabilities
            .OrderBy(capability => capability.Name, StringComparer.OrdinalIgnoreCase)
            .Select(capability =>
            {
                var flows = catalog.NormalizedFlows.Where(flow => string.Equals(flow.CapabilityId, capability.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                var latestOutcomes = flows
                    .Select(flow => flowLookup.TryGetValue(flow.FlowId, out var result) ? result.Outcome.ToString() : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new StudioCapabilityCoverage
                {
                    CapabilityId = capability.Id,
                    CapabilityName = capability.Name,
                    FlowCount = flows.Count,
                    AcceptanceCriteriaCount = capability.AcceptanceCriteria?.Count ?? 0,
                    LatestOutcome = latestOutcomes.Count == 0 ? "Not run" : string.Join(", ", latestOutcomes)
                };
            })
            .ToList();
    }
}

public sealed record StudioProjectSnapshot
{
    public ProjectCatalog Catalog { get; init; } = new();
    public IReadOnlyList<StoredRunResult> Runs { get; init; } = [];
    public IReadOnlyList<StudioCapabilityCoverage> CapabilityCoverage { get; init; } = [];
    public StudioRunInsights RunInsights { get; init; } = new();
}

public sealed record StudioCapabilityCoverage
{
    public string CapabilityId { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public int FlowCount { get; init; }
    public int AcceptanceCriteriaCount { get; init; }
    public string LatestOutcome { get; init; } = "Not run";
}
