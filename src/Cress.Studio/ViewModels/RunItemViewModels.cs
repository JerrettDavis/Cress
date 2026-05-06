using System.Windows.Media;
using Cress.Core.Models;
using Cress.Execution;

namespace Cress.Studio.ViewModels;

public sealed class RunItemViewModel
{
    public required StoredRunResult StoredRun { get; init; }

    public string DisplayName => StoredRun.Result.Metadata.RunId;

    public string Summary
        => $"{StoredRun.Result.Metadata.Profile} • {StoredRun.Result.Flows.Count(flow => flow.Outcome == RunOutcome.Passed)}/{StoredRun.Result.Flows.Count} passed • {StoredRun.Result.Flows.Count(flow => flow.PassedWithRetry)} retried • {StoredRun.Result.Metadata.StartedAt.LocalDateTime:g}";
}

public sealed class RunFlowItemViewModel
{
    public required FlowRunResult Flow { get; init; }

    public string Name => Flow.Name;
    public string Outcome => Flow.Outcome.ToString();
}

public sealed class ArtifactItemViewModel
{
    public string DisplayName { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

public sealed class CapabilityCoverageItemViewModel
{
    public string CapabilityId { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public int FlowCount { get; init; }
    public string LatestOutcome { get; init; } = "Not run";
    public int AcceptanceCriteriaCount { get; init; }
}
