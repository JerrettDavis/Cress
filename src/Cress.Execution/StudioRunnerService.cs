using System.Collections.Concurrent;
using Cress.Core.Models;
using Cress.Execution;

namespace Cress.Studio.Services;

public interface IStudioRunnerExecutor
{
    Task<RunResult> ExecuteAsync(
        string projectRoot,
        RunOptions options,
        IProgress<RuntimeProgressUpdate>? progress,
        CancellationToken cancellationToken);
}

public sealed class StudioRuntimeRunnerExecutor(RuntimeOrchestrator orchestrator) : IStudioRunnerExecutor
{
    public Task<RunResult> ExecuteAsync(
        string projectRoot,
        RunOptions options,
        IProgress<RuntimeProgressUpdate>? progress,
        CancellationToken cancellationToken)
        => orchestrator.ExecuteAsync(projectRoot, options, progress, cancellationToken);
}

public interface IStudioRunnerNode
{
    event Action? Changed;

    StudioRunnerNodeSnapshot Snapshot { get; }

    Task<StudioRunnerDispatchResult> DispatchAsync(
        StudioRunnerDispatchRequest request,
        IProgress<RuntimeProgressUpdate>? progress,
        CancellationToken cancellationToken = default);
}

public interface IStudioRunnerService
{
    event Action? Changed;

    IReadOnlyList<StudioRunnerNodeSnapshot> ListNodes();

    Task<StudioRunnerDispatchResult> DispatchAsync(
        StudioRunnerDispatchRequest request,
        IProgress<RuntimeProgressUpdate>? progress,
        CancellationToken cancellationToken = default);
}

public sealed class StudioRunnerService : IStudioRunnerService
{
    private readonly IReadOnlyDictionary<string, IStudioRunnerNode> _nodes;

    public StudioRunnerService(IEnumerable<IStudioRunnerNode> nodes)
    {
        _nodes = nodes.ToDictionary(node => node.Snapshot.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var node in _nodes.Values)
        {
            node.Changed += HandleNodeChanged;
        }
    }

    public event Action? Changed;

    public IReadOnlyList<StudioRunnerNodeSnapshot> ListNodes()
        => _nodes.Values
            .Select(node => node.Snapshot)
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public Task<StudioRunnerDispatchResult> DispatchAsync(
        StudioRunnerDispatchRequest request,
        IProgress<RuntimeProgressUpdate>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!_nodes.TryGetValue(request.NodeId, out var node))
        {
            throw new InvalidOperationException($"Runner node '{request.NodeId}' is not registered.");
        }

        return node.DispatchAsync(request, progress, cancellationToken);
    }

    private void HandleNodeChanged()
        => Changed?.Invoke();
}

public sealed class StudioEmbeddedRunnerNode(IStudioRunnerExecutor executor) : IStudioRunnerNode
{
    public const string LocalNodeId = "local-embedded";

    private readonly SemaphoreSlim _dispatchLock = new(1, 1);
    private readonly ConcurrentQueue<string> _queuedDispatches = new();
    private StudioRunnerNodeSnapshot _snapshot = CreateInitialSnapshot();

    public event Action? Changed;

    public StudioRunnerNodeSnapshot Snapshot => _snapshot;

    public async Task<StudioRunnerDispatchResult> DispatchAsync(
        StudioRunnerDispatchRequest request,
        IProgress<RuntimeProgressUpdate>? progress,
        CancellationToken cancellationToken = default)
    {
        var dispatchedAt = DateTimeOffset.UtcNow;
        _queuedDispatches.Enqueue(request.DispatchId);
        UpdateSnapshot(snapshot => snapshot with
        {
            QueueDepth = _queuedDispatches.Count,
            LastHeartbeatUtc = dispatchedAt
        });

        await _dispatchLock.WaitAsync(cancellationToken);
        try
        {
            _queuedDispatches.TryDequeue(out _);
            UpdateSnapshot(snapshot => snapshot with
            {
                Status = StudioRunnerNodeStatus.Busy,
                QueueDepth = _queuedDispatches.Count,
                ActiveDispatchId = request.DispatchId,
                LastHeartbeatUtc = DateTimeOffset.UtcNow,
                LastError = null
            });

            var nodeProgress = new Progress<RuntimeProgressUpdate>(update =>
            {
                progress?.Report(update);

                if (string.IsNullOrWhiteSpace(update.RunId))
                {
                    UpdateSnapshot(snapshot => snapshot with
                    {
                        LastHeartbeatUtc = DateTimeOffset.UtcNow
                    });
                    return;
                }

                UpdateSnapshot(snapshot => snapshot with
                {
                    ActiveDispatchId = request.DispatchId,
                    ActiveRunId = update.RunId,
                    LastHeartbeatUtc = DateTimeOffset.UtcNow
                });
            });

            var result = await executor.ExecuteAsync(request.ProjectRoot, request.Options, nodeProgress, cancellationToken);
            var completedAt = DateTimeOffset.UtcNow;

            UpdateSnapshot(snapshot => snapshot with
            {
                Status = StudioRunnerNodeStatus.Healthy,
                ActiveDispatchId = null,
                ActiveRunId = null,
                LastRunId = result.Metadata.RunId,
                LastCompletedUtc = completedAt,
                LastHeartbeatUtc = completedAt,
                LastError = null
            });

            return new StudioRunnerDispatchResult(request.NodeId, request.DispatchId, dispatchedAt, completedAt, result);
        }
        catch (OperationCanceledException)
        {
            var canceledAt = DateTimeOffset.UtcNow;
            UpdateSnapshot(snapshot => snapshot with
            {
                Status = StudioRunnerNodeStatus.Healthy,
                ActiveDispatchId = null,
                ActiveRunId = null,
                LastCompletedUtc = canceledAt,
                LastHeartbeatUtc = canceledAt,
                LastError = null
            });
            throw;
        }
        catch (Exception ex)
        {
            var failedAt = DateTimeOffset.UtcNow;
            UpdateSnapshot(snapshot => snapshot with
            {
                Status = StudioRunnerNodeStatus.Degraded,
                ActiveDispatchId = null,
                ActiveRunId = null,
                LastCompletedUtc = failedAt,
                LastHeartbeatUtc = failedAt,
                LastError = ex.Message
            });
            throw;
        }
        finally
        {
            _dispatchLock.Release();
        }
    }

    private void UpdateSnapshot(Func<StudioRunnerNodeSnapshot, StudioRunnerNodeSnapshot> update)
    {
        _snapshot = update(_snapshot);
        Changed?.Invoke();
    }

    private static StudioRunnerNodeSnapshot CreateInitialSnapshot()
    {
        var capabilities = new List<string> { "http", "playwright", "plugins" };
        if (OperatingSystem.IsWindows())
        {
            capabilities.Add("flawright");
        }

        return new StudioRunnerNodeSnapshot(
            LocalNodeId,
            "Local embedded node",
            $"{Environment.MachineName} • in-process",
            "Runs flows on the same machine as Studio Web. This is the development node that future remote runners will mirror.",
            StudioRunnerTransportKind.Embedded,
            "This machine",
            capabilities,
            StudioRunnerNodeStatus.Healthy,
            DateTimeOffset.UtcNow,
            LastCompletedUtc: null,
            ActiveDispatchId: null,
            ActiveRunId: null,
            LastRunId: null,
            QueueDepth: 0,
            LastError: null);
    }
}

public sealed record StudioRunnerDispatchRequest(
    string NodeId,
    string ProjectRoot,
    RunOptions Options,
    string DispatchId,
    string RequestedBy,
    string RequestedFrom)
{
    public static StudioRunnerDispatchRequest Create(string nodeId, string projectRoot, RunOptions options, string requestedBy, string requestedFrom)
        => new(
            NodeId: nodeId,
            ProjectRoot: projectRoot,
            Options: options,
            DispatchId: $"dispatch-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            RequestedBy: requestedBy,
            RequestedFrom: requestedFrom);
}

public sealed record StudioRunnerDispatchResult(
    string NodeId,
    string DispatchId,
    DateTimeOffset DispatchedAt,
    DateTimeOffset CompletedAt,
    RunResult Result);

public sealed record StudioRunnerNodeSnapshot(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    StudioRunnerTransportKind Transport,
    string Location,
    IReadOnlyList<string> Capabilities,
    StudioRunnerNodeStatus Status,
    DateTimeOffset LastHeartbeatUtc,
    DateTimeOffset? LastCompletedUtc,
    string? ActiveDispatchId,
    string? ActiveRunId,
    string? LastRunId,
    int QueueDepth,
    string? LastError);

public enum StudioRunnerNodeStatus
{
    Healthy,
    Busy,
    Degraded,
    Offline
}

public enum StudioRunnerTransportKind
{
    Embedded,
    RemoteHttp
}
