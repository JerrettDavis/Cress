using Cress.Companion;
using Cress.Recorder;

namespace Cress.Companion.Core.Tests;

public sealed class DesktopCompanionCoordinatorTests
{
    [Fact]
    public async Task StartRecordingAsync_creates_multi_app_sessions_and_infers_steps()
    {
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(backendFactory);

        await coordinator.StartRecordingAsync(101);
        await coordinator.StartRecordingAsync(202, overlayEnabled: false);

        backendFactory.EmitInvoke(101, "clearButton");
        backendFactory.EmitInvoke(202, "equalButton");

        var snapshot = coordinator.GetSnapshot();

        Assert.Equal(2, snapshot.Sessions.Count);
        Assert.Contains(snapshot.Sessions, session => session.ProcessId == 101 && session.Status == CompanionSessionStatus.Recording && session.InferredStepCount == 1 && session.OverlayEnabled);
        Assert.Contains(snapshot.Sessions, session => session.ProcessId == 202 && session.Status == CompanionSessionStatus.Recording && session.InferredStepCount == 1 && !session.OverlayEnabled);
    }

    [Fact]
    public async Task Pause_resume_and_stop_update_session_state_and_suppressed_counts()
    {
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(backendFactory);

        await coordinator.StartRecordingAsync(101);
        await coordinator.PauseRecordingAsync(101);
        backendFactory.EmitInvoke(101, "clearButton");

        var paused = coordinator.GetSnapshot().Sessions.Single();
        Assert.Equal(CompanionSessionStatus.Paused, paused.Status);
        Assert.Equal(1, paused.SuppressedEventCount);
        Assert.Equal(0, paused.CapturedEventCount);

        await coordinator.ResumeRecordingAsync(101);
        backendFactory.EmitInvoke(101, "plusButton");
        var resumed = coordinator.GetSnapshot().Sessions.Single();
        Assert.Equal(CompanionSessionStatus.Recording, resumed.Status);
        Assert.Equal(1, resumed.CapturedEventCount);

        var stopped = await coordinator.StopRecordingAsync(101);
        Assert.Equal(CompanionSessionStatus.Stopped, stopped.Status);
        Assert.NotNull(stopped.EndedAtUtc);
    }

    [Fact]
    public async Task GetSnapshot_with_preview_includes_current_window_preview()
    {
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(backendFactory, previewProvider: new FakePreviewProvider());

        await coordinator.StartRecordingAsync(101);

        var snapshot = coordinator.GetSnapshot(includePreview: true);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("data:image/png;base64,preview", session.PreviewImageDataUrl);
        Assert.True(session.IsWindowVisible);
        Assert.Equal("Calculator", session.WindowTitle);
    }

    private static DesktopCompanionCoordinator CreateCoordinator(
        FakeBackendFactory backendFactory,
        ICompanionPreviewProvider? previewProvider = null)
        => new(
            backendFactory,
            new FakeTargetCatalog(),
            new FakeWindowInspector(),
            previewProvider ?? new FakePreviewProvider(),
            new FakeClock());

    private sealed class FakeBackendFactory : ICompanionSessionBackendFactory
    {
        private readonly Dictionary<int, FakeBackend> _backends = new();

        public ICompanionSessionBackend Create(int processId)
        {
            var backend = new FakeBackend(processId);
            _backends[processId] = backend;
            return backend;
        }

        public void EmitInvoke(int processId, string automationId)
        {
            var recordedEvent = new RecordedEvent
            {
                Kind = EventKind.Invoke,
                Timestamp = DateTimeOffset.UtcNow,
                Element = new ElementInfo
                {
                    ProcessId = processId,
                    AutomationId = automationId,
                    Name = automationId,
                    ControlType = "button"
                }
            };

            _backends[processId].Emit(recordedEvent);
        }
    }

    private sealed class FakeBackend(int processId) : ICompanionSessionBackend
    {
        private readonly List<RecordedEvent> _events = [];

        public event Action<RecordedEvent>? EventCaptured;

        public void Start()
        {
        }

        public IReadOnlyList<RecordedEvent> Stop()
            => _events.ToList();

        public void Emit(RecordedEvent recordedEvent)
        {
            Assert.Equal(processId, recordedEvent.Element.ProcessId);
            _events.Add(recordedEvent);
            EventCaptured?.Invoke(recordedEvent);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeTargetCatalog : ICompanionTargetCatalog
    {
        public Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync()
            => Task.FromResult<IReadOnlyList<CompanionTargetInfo>>([
                new CompanionTargetInfo { ProcessId = 101, ProcessName = "calc", WindowTitle = "Calculator", IsAttachable = true },
                new CompanionTargetInfo { ProcessId = 202, ProcessName = "notepad", WindowTitle = "Notes", IsAttachable = true }
            ]);
    }

    private sealed class FakeWindowInspector : ICompanionWindowInspector
    {
        public CompanionWindowState Inspect(int processId)
            => processId == 101
                ? new CompanionWindowState
                {
                    WindowTitle = "Calculator",
                    IsVisible = true,
                    Bounds = new CompanionWindowBounds(10, 20, 800, 600)
                }
                : new CompanionWindowState
                {
                    WindowTitle = "Notes",
                    IsVisible = true,
                    Bounds = new CompanionWindowBounds(100, 40, 1024, 768)
                };
    }

    private sealed class FakePreviewProvider : ICompanionPreviewProvider
    {
        public string? CapturePreview(CompanionWindowBounds bounds)
            => "data:image/png;base64,preview";
    }

    private sealed class FakeClock : ICompanionClock
    {
        public DateTimeOffset UtcNow => new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    }
}
