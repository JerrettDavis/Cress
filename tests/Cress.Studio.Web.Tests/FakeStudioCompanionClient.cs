using Cress.Companion;
using Cress.Studio.Services;

namespace Cress.Studio.Web.Tests;

internal sealed class FakeStudioCompanionClient : IStudioCompanionClient
{
    public CompanionServiceSnapshot Snapshot { get; set; } = new()
    {
        IsAvailable = false,
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        Sessions = []
    };

    public List<CompanionTargetInfo> Targets { get; set; } = [];

    public int? LastStartedProcessId { get; private set; }
    public int? LastPausedProcessId { get; private set; }
    public int? LastResumedProcessId { get; private set; }
    public int? LastStoppedProcessId { get; private set; }

    public Task<CompanionServiceSnapshot> GetSnapshotAsync(bool includePreview = false, CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot);

    public Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CompanionTargetInfo>>(Targets);

    public Task<CompanionSessionSnapshot> StartRecordingAsync(int processId, bool overlayEnabled = true, CancellationToken cancellationToken = default)
    {
        LastStartedProcessId = processId;
        var session = new CompanionSessionSnapshot
        {
            ProcessId = processId,
            ProcessName = Targets.FirstOrDefault(target => target.ProcessId == processId)?.ProcessName ?? $"pid-{processId}",
            WindowTitle = Targets.FirstOrDefault(target => target.ProcessId == processId)?.WindowTitle ?? $"PID {processId}",
            Status = CompanionSessionStatus.Recording,
            StartedAtUtc = DateTimeOffset.UtcNow,
            OverlayEnabled = overlayEnabled
        };
        Snapshot = Snapshot with
        {
            IsAvailable = true,
            Sessions = Snapshot.Sessions.Concat([session]).ToList(),
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };

        return Task.FromResult(session);
    }

    public Task<CompanionSessionSnapshot> PauseRecordingAsync(int processId, CancellationToken cancellationToken = default)
    {
        LastPausedProcessId = processId;
        return UpdateStatus(processId, CompanionSessionStatus.Paused);
    }

    public Task<CompanionSessionSnapshot> ResumeRecordingAsync(int processId, CancellationToken cancellationToken = default)
    {
        LastResumedProcessId = processId;
        return UpdateStatus(processId, CompanionSessionStatus.Recording);
    }

    public Task<CompanionSessionSnapshot> StopRecordingAsync(int processId, CancellationToken cancellationToken = default)
    {
        LastStoppedProcessId = processId;
        return UpdateStatus(processId, CompanionSessionStatus.Stopped);
    }

    private Task<CompanionSessionSnapshot> UpdateStatus(int processId, CompanionSessionStatus status)
    {
        var sessions = Snapshot.Sessions.ToList();
        var index = sessions.FindIndex(session => session.ProcessId == processId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Session {processId} was not found.");
        }

        var updated = sessions[index] with
        {
            Status = status,
            EndedAtUtc = status == CompanionSessionStatus.Stopped ? DateTimeOffset.UtcNow : null
        };
        sessions[index] = updated;
        Snapshot = Snapshot with
        {
            Sessions = sessions,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
        return Task.FromResult(updated);
    }
}
