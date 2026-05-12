using Cress.Recorder;

namespace Cress.Companion;

public interface ICompanionSessionBackend : IDisposable
{
    event Action<RecordedEvent>? EventCaptured;

    void Start();

    IReadOnlyList<RecordedEvent> Stop();
}

public interface ICompanionSessionBackendFactory
{
    ICompanionSessionBackend Create(int processId);
}

public interface ICompanionTargetCatalog
{
    Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync();
}

public interface ICompanionWindowInspector
{
    CompanionWindowState Inspect(int processId);
}

public interface ICompanionPreviewProvider
{
    string? CapturePreview(CompanionWindowBounds bounds);
}

public interface ICompanionClock
{
    DateTimeOffset UtcNow { get; }
}
