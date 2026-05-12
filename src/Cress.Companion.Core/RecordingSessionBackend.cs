using Cress.Recorder;

namespace Cress.Companion;

public sealed class RecordingSessionBackendFactory : ICompanionSessionBackendFactory
{
    public ICompanionSessionBackend Create(int processId)
        => new RecordingSessionBackend(RecordingSession.FromProcessId(processId));
}

internal sealed class RecordingSessionBackend(RecordingSession session) : ICompanionSessionBackend
{
    public event Action<RecordedEvent>? EventCaptured;

    public void Start()
    {
        session.EventCaptured += HandleEventCaptured;
        session.Start();
    }

    public IReadOnlyList<RecordedEvent> Stop()
    {
        session.EventCaptured -= HandleEventCaptured;
        return session.Stop();
    }

    public void Dispose()
    {
        session.EventCaptured -= HandleEventCaptured;
        session.Dispose();
    }

    private void HandleEventCaptured(RecordedEvent recordedEvent)
        => EventCaptured?.Invoke(recordedEvent);
}
