using Cress.Core.Models;

namespace Cress.Sdk;

public interface ICressPluginModule
{
    IEnumerable<StepHandlerRegistration> GetStepHandlers();

    IEnumerable<FixtureProviderRegistration> GetFixtureProviders()
        => [];
}

public sealed record StepHandlerRegistration(
    string Operation,
    Func<StepExecutionContext, CancellationToken, Task<StepExecutionResult>> Execute);

public sealed record FixtureProviderRegistration(
    string Operation,
    Func<FixtureExecutionContext, CancellationToken, Task<FixtureExecutionResult>> Execute);

public sealed class StepExecutionContext
{
    public string FlowId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string ArtifactDirectory { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
    public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Fixtures { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public ICressLogger Logger { get; init; } = NullCressLogger.Instance;
    public IDriverAccessor Drivers { get; init; } = NullDriverAccessor.Instance;

    public string GetRequiredInput(string name)
        => Inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Required input '{name}' was not supplied for step '{StepName}'.");

    public string? GetInput(string name)
        => Inputs.TryGetValue(name, out var value) ? value : null;
}

public sealed class FixtureExecutionContext
{
    public string FlowId { get; init; } = string.Empty;
    public string FixtureAlias { get; init; } = string.Empty;
    public string FixtureName { get; init; } = string.Empty;
    public string ArtifactDirectory { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Bindings { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public ICressLogger Logger { get; init; } = NullCressLogger.Instance;
}

public sealed record StepExecutionResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? FailureClassification { get; init; }
    public IReadOnlyDictionary<string, string> Outputs { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<EvidenceArtifact> Artifacts { get; init; } = [];
}

public sealed record FixtureExecutionResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, string> Outputs { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<EvidenceArtifact> Artifacts { get; init; } = [];
}

public interface ICressLogger
{
    void Info(string message, IReadOnlyDictionary<string, string>? data = null);
    void Warning(string message, IReadOnlyDictionary<string, string>? data = null);
    void Error(string message, IReadOnlyDictionary<string, string>? data = null);
}

public sealed class NullCressLogger : ICressLogger
{
    public static NullCressLogger Instance { get; } = new();

    public void Info(string message, IReadOnlyDictionary<string, string>? data = null)
    {
    }

    public void Warning(string message, IReadOnlyDictionary<string, string>? data = null)
    {
    }

    public void Error(string message, IReadOnlyDictionary<string, string>? data = null)
    {
    }
}

public interface IDriverAccessor
{
    bool TryGetMetadata(string driverName, out IReadOnlyDictionary<string, string>? metadata);
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Snapshot();
}

public sealed class NullDriverAccessor : IDriverAccessor
{
    public static NullDriverAccessor Instance { get; } = new();

    public bool TryGetMetadata(string driverName, out IReadOnlyDictionary<string, string>? metadata)
    {
        metadata = null;
        return false;
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Snapshot()
        => new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
}
