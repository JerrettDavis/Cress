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

        var runningSnapshot = service.ListNodes().Single();
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
}
