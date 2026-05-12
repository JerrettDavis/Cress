using Cress.Companion;

namespace Cress.Studio.Services;

public interface IStudioCompanionClient
{
    Task<CompanionServiceSnapshot> GetSnapshotAsync(bool includePreview = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync(CancellationToken cancellationToken = default);

    Task<CompanionSessionSnapshot> StartRecordingAsync(int processId, bool overlayEnabled = true, CancellationToken cancellationToken = default);

    Task<CompanionSessionSnapshot> PauseRecordingAsync(int processId, CancellationToken cancellationToken = default);

    Task<CompanionSessionSnapshot> ResumeRecordingAsync(int processId, CancellationToken cancellationToken = default);

    Task<CompanionSessionSnapshot> StopRecordingAsync(int processId, CancellationToken cancellationToken = default);
}
