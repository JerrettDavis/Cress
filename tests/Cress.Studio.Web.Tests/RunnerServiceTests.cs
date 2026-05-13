using System.Collections.Concurrent;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio.Services;

namespace Cress.Studio.Web.Tests;

public sealed class RunnerServiceTests
{
    [Fact]
    public async Task EmbeddedRunner_dispatches_runs_and_updates_snapshot()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FakeStudioRunnerExecutor(async (_, _, progress, cancellationToken) =>
        {
            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.RunStarted,
                RunId = "run-123",
                Message = "Running sample flow."
            });

            started.SetResult();
            await release.Task.WaitAsync(cancellationToken);

            progress?.Report(new RuntimeProgressUpdate
            {
                Kind = RuntimeProgressKind.RunCompleted,
                RunId = "run-123",
                Message = "Run passed."
            });

            return new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-123",
                    Profile = "local",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                    DurationMs = 10
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow-1",
                        Name = "Sample flow",
                        Outcome = RunOutcome.Passed
                    }
                ]
            };
        });

        var node = new StudioEmbeddedRunnerNode(executor);
        var service = new StudioRunnerService([node]);
        var request = StudioRunnerDispatchRequest.Create(
            StudioEmbeddedRunnerNode.LocalNodeId,
            projectRoot: "C:\\repo\\specs\\sample",
            options: new RunOptions { Profile = "local" },
            requestedBy: "test-user",
            requestedFrom: "test");

        var dispatchTask = service.DispatchAsync(request, progress: null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var runningSnapshot = await WaitForSnapshotAsync(
            service,
            snapshot => snapshot.ActiveRunId == "run-123",
            TimeSpan.FromSeconds(5));
        Assert.Equal(StudioRunnerNodeStatus.Busy, runningSnapshot.Status);
        Assert.Equal("run-123", runningSnapshot.ActiveRunId);
        Assert.Equal(request.DispatchId, runningSnapshot.ActiveDispatchId);

        release.SetResult();

        var result = await dispatchTask;
        var completedSnapshot = service.ListNodes().Single();

        Assert.Equal(StudioEmbeddedRunnerNode.LocalNodeId, result.NodeId);
        Assert.Equal("run-123", result.Result.Metadata.RunId);
        Assert.Equal(StudioRunnerNodeStatus.Healthy, completedSnapshot.Status);
        Assert.Null(completedSnapshot.ActiveRunId);
        Assert.Equal("run-123", completedSnapshot.LastRunId);
        Assert.Null(completedSnapshot.LastError);
    }

    [Fact]
    public void EmbeddedRunner_registers_local_capabilities()
    {
        var node = new StudioEmbeddedRunnerNode(new FakeStudioRunnerExecutor((_, _, _, _) => Task.FromResult(new RunResult())));
        var snapshot = node.Snapshot;

        Assert.Equal(StudioEmbeddedRunnerNode.LocalNodeId, snapshot.Id);
        Assert.Equal(StudioRunnerTransportKind.Embedded, snapshot.Transport);
        Assert.Contains("http", snapshot.Capabilities);
        Assert.Contains("playwright", snapshot.Capabilities);
        Assert.Contains("plugins", snapshot.Capabilities);

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("flawright", snapshot.Capabilities);
        }
    }

    [Fact]
    public void RunnerService_lists_nodes_in_name_order_and_replays_change_notifications()
    {
        var first = new FakeStudioRunnerNode(new StudioRunnerNodeSnapshot(
            Id: "zeta",
            Name: "Zeta node",
            DisplayName: "Zeta",
            Description: "Later node",
            Transport: StudioRunnerTransportKind.RemoteHttp,
            Location: "lab-z",
            Capabilities: ["http"],
            Status: StudioRunnerNodeStatus.Healthy,
            LastHeartbeatUtc: DateTimeOffset.UtcNow,
            LastCompletedUtc: null,
            ActiveDispatchId: null,
            ActiveRunId: null,
            LastRunId: null,
            QueueDepth: 0,
            LastError: null));
        var second = new FakeStudioRunnerNode(new StudioRunnerNodeSnapshot(
            Id: "alpha",
            Name: "Alpha node",
            DisplayName: "Alpha",
            Description: "Earlier node",
            Transport: StudioRunnerTransportKind.Embedded,
            Location: "lab-a",
            Capabilities: ["http"],
            Status: StudioRunnerNodeStatus.Healthy,
            LastHeartbeatUtc: DateTimeOffset.UtcNow,
            LastCompletedUtc: null,
            ActiveDispatchId: null,
            ActiveRunId: null,
            LastRunId: null,
            QueueDepth: 0,
            LastError: null));
        var service = new StudioRunnerService([first, second]);
        var changedCount = 0;
        service.Changed += () => changedCount++;

        var snapshots = service.ListNodes();

        Assert.Collection(
            snapshots,
            snapshot => Assert.Equal("Alpha node", snapshot.Name),
            snapshot => Assert.Equal("Zeta node", snapshot.Name));

        second.RaiseChanged();

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public async Task RunnerService_throws_when_node_is_not_registered()
    {
        var service = new StudioRunnerService([]);
        var request = StudioRunnerDispatchRequest.Create(
            nodeId: "missing-node",
            projectRoot: "C:\\repo\\specs\\sample",
            options: new RunOptions(),
            requestedBy: "test-user",
            requestedFrom: "test");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DispatchAsync(request, progress: null));

        Assert.Contains("missing-node", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddedRunner_marks_snapshot_degraded_when_execution_fails()
    {
        var node = new StudioEmbeddedRunnerNode(new FakeStudioRunnerExecutor((_, _, _, _) => throw new InvalidOperationException("boom")));
        var request = StudioRunnerDispatchRequest.Create(
            StudioEmbeddedRunnerNode.LocalNodeId,
            projectRoot: "C:\\repo\\specs\\sample",
            options: new RunOptions(),
            requestedBy: "test-user",
            requestedFrom: "test");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => node.DispatchAsync(request, progress: null));
        var snapshot = node.Snapshot;

        Assert.Equal("boom", error.Message);
        Assert.Equal(StudioRunnerNodeStatus.Degraded, snapshot.Status);
        Assert.Null(snapshot.ActiveDispatchId);
        Assert.Null(snapshot.ActiveRunId);
        Assert.Equal("boom", snapshot.LastError);
        Assert.NotNull(snapshot.LastCompletedUtc);
    }

    [Fact]
    public async Task EmbeddedRunner_resets_snapshot_when_execution_is_cancelled()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = new StudioEmbeddedRunnerNode(new FakeStudioRunnerExecutor(async (_, _, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new RunResult();
        }));
        var request = StudioRunnerDispatchRequest.Create(
            StudioEmbeddedRunnerNode.LocalNodeId,
            projectRoot: "C:\\repo\\specs\\sample",
            options: new RunOptions(),
            requestedBy: "test-user",
            requestedFrom: "test");
        using var cts = new CancellationTokenSource();
        var dispatchTask = node.DispatchAsync(request, progress: null, cts.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatchTask);

        var snapshot = node.Snapshot;
        Assert.Equal(StudioRunnerNodeStatus.Healthy, snapshot.Status);
        Assert.Null(snapshot.ActiveDispatchId);
        Assert.Null(snapshot.ActiveRunId);
        Assert.Null(snapshot.LastError);
        Assert.NotNull(snapshot.LastCompletedUtc);
    }

    [Fact]
    public void DispatchRequest_create_populates_request_metadata()
    {
        var options = new RunOptions { Profile = "ci" };

        var request = StudioRunnerDispatchRequest.Create(
            StudioEmbeddedRunnerNode.LocalNodeId,
            projectRoot: "C:\\repo\\specs\\sample",
            options: options,
            requestedBy: "test-user",
            requestedFrom: "runner-tests");

        Assert.Equal(StudioEmbeddedRunnerNode.LocalNodeId, request.NodeId);
        Assert.Equal("C:\\repo\\specs\\sample", request.ProjectRoot);
        Assert.Same(options, request.Options);
        Assert.Equal("test-user", request.RequestedBy);
        Assert.Equal("runner-tests", request.RequestedFrom);
        Assert.StartsWith("dispatch-", request.DispatchId, StringComparison.Ordinal);
    }

    private static async Task<StudioRunnerNodeSnapshot> WaitForSnapshotAsync(
        StudioRunnerService service,
        Func<StudioRunnerNodeSnapshot, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = service.ListNodes().Single();
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(25);
        }

        return service.ListNodes().Single();
    }

    private sealed class FakeStudioRunnerExecutor(Func<string, RunOptions, IProgress<RuntimeProgressUpdate>?, CancellationToken, Task<RunResult>> executeAsync)
        : IStudioRunnerExecutor
    {
        public Task<RunResult> ExecuteAsync(
            string projectRoot,
            RunOptions options,
            IProgress<RuntimeProgressUpdate>? progress,
            CancellationToken cancellationToken)
            => executeAsync(projectRoot, options, progress, cancellationToken);
    }

    private sealed class FakeStudioRunnerNode(StudioRunnerNodeSnapshot snapshot) : IStudioRunnerNode
    {
        public event Action? Changed;

        public StudioRunnerNodeSnapshot Snapshot { get; } = snapshot;

        public Task<StudioRunnerDispatchResult> DispatchAsync(
            StudioRunnerDispatchRequest request,
            IProgress<RuntimeProgressUpdate>? progress,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new StudioRunnerDispatchResult(
                request.NodeId,
                request.DispatchId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                new RunResult()));

        public void RaiseChanged() => Changed?.Invoke();
    }
}
