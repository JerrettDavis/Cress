using Cress.Core.Models;
using Cress.Sdk;

namespace Cress.Execution;

public interface IRuntimeDriver
{
    string Name { get; }
    IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog);
    Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken);
}

public interface IDriverSession : IAsyncDisposable
{
    string Name { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
    Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken);
}

public sealed class DriverSessionStartContext
{
    public string ProjectRoot { get; init; } = string.Empty;
    public string FlowId { get; init; } = string.Empty;
    public string ArtifactRoot { get; init; } = string.Empty;
    public EvidenceStore EvidenceStore { get; init; } = null!;
    public EffectiveConfig EffectiveConfig { get; init; } = new();
}

public sealed class FlowExecutionContext
{
    public string FlowId { get; init; } = string.Empty;
    public string FlowName { get; init; } = string.Empty;
    public string ArtifactRoot { get; init; } = string.Empty;
    public EffectiveConfig EffectiveConfig { get; init; } = new();
    public EvidenceStore EvidenceStore { get; init; } = null!;
    public IDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, string> Fixtures { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public ICressLogger Logger { get; init; } = NullCressLogger.Instance;
    public IDriverAccessor Drivers { get; init; } = NullDriverAccessor.Instance;
}
