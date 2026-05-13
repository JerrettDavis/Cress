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

    [Fact]
    public async Task StartRecordingAsync_reuses_active_session_and_updates_overlay_state()
    {
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(backendFactory);

        var first = await coordinator.StartRecordingAsync(101, overlayEnabled: false);
        var second = await coordinator.StartRecordingAsync(101, overlayEnabled: true);

        var backend = backendFactory.GetBackend(101);
        Assert.Equal(first.StartedAtUtc, second.StartedAtUtc);
        Assert.True(second.OverlayEnabled);
        Assert.Equal(1, backendFactory.CreateCount(101));
        Assert.Equal(1, backend.StartCalls);
    }

    [Fact]
    public async Task StartRecordingAsync_when_backend_start_fails_returns_faulted_snapshot()
    {
        var backendFactory = new FakeBackendFactory();
        backendFactory.Configure(101, backend => backend.StartException = new InvalidOperationException("Failed to hook recorder."));
        using var coordinator = CreateCoordinator(backendFactory);

        var snapshot = await coordinator.StartRecordingAsync(101);

        var backend = backendFactory.GetBackend(101);
        Assert.Equal(CompanionSessionStatus.Faulted, snapshot.Status);
        Assert.Equal("Failed to hook recorder.", snapshot.ErrorMessage);
        Assert.Equal(1, backend.DisposeCalls);
    }

    [Fact]
    public async Task StopRecordingAsync_uses_backend_stop_events_when_live_events_were_not_captured()
    {
        var backendFactory = new FakeBackendFactory();
        backendFactory.Configure(101, backend => backend.StoppedEvents =
        [
            CreateRecordedEvent(101, EventKind.Invoke, automationId: "clearButton")
        ]);
        using var coordinator = CreateCoordinator(backendFactory);

        await coordinator.StartRecordingAsync(101);

        var stopped = await coordinator.StopRecordingAsync(101);
        var stoppedAgain = await coordinator.StopRecordingAsync(101);

        var backend = backendFactory.GetBackend(101);
        Assert.Equal(CompanionSessionStatus.Stopped, stopped.Status);
        Assert.Equal(1, stopped.CapturedEventCount);
        Assert.Equal(1, stopped.InferredStepCount);
        Assert.Equal(1, backend.StopCalls);
        Assert.Equal(stopped.CapturedEventCount, stoppedAgain.CapturedEventCount);
    }

    [Fact]
    public async Task GetSnapshot_without_preview_clears_any_previous_preview()
    {
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(backendFactory, previewProvider: new FakePreviewProvider());

        await coordinator.StartRecordingAsync(101);

        Assert.NotNull(coordinator.GetSnapshot(includePreview: true).Sessions.Single().PreviewImageDataUrl);
        Assert.Null(coordinator.GetSnapshot(includePreview: false).Sessions.Single().PreviewImageDataUrl);
    }

    [Fact]
    public async Task SetOverlayEnabledAsync_and_list_targets_reflect_current_state()
    {
        var backendFactory = new FakeBackendFactory();
        var targetCatalog = new FakeTargetCatalog(
            new CompanionTargetInfo { ProcessId = 101, ProcessName = "calc", WindowTitle = "Calculator", IsAttachable = true },
            new CompanionTargetInfo { ProcessId = 202, ProcessName = "notepad", WindowTitle = "Notes", IsAttachable = false });
        using var coordinator = CreateCoordinator(backendFactory, targetCatalog: targetCatalog);

        var targets = await coordinator.ListTargetsAsync();
        await coordinator.StartRecordingAsync(101);
        var updated = await coordinator.SetOverlayEnabledAsync(101, overlayEnabled: false);

        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, target => target.ProcessId == 202 && !target.IsAttachable);
        Assert.False(updated.OverlayEnabled);
    }

    [Fact]
    public async Task Faulted_sessions_preserve_faulted_status_for_pause_resume_and_stop()
    {
        var backendFactory = new FakeBackendFactory();
        backendFactory.Configure(101, backend => backend.StartException = new InvalidOperationException("boom"));
        using var coordinator = CreateCoordinator(backendFactory);

        await coordinator.StartRecordingAsync(101);

        Assert.Equal(CompanionSessionStatus.Faulted, (await coordinator.PauseRecordingAsync(101)).Status);
        Assert.Equal(CompanionSessionStatus.Faulted, (await coordinator.ResumeRecordingAsync(101)).Status);
        Assert.Equal(CompanionSessionStatus.Faulted, (await coordinator.StopRecordingAsync(101)).Status);
    }

    [Fact]
    public async Task StartRecordingAsync_uses_catalog_fallback_when_process_cannot_be_resolved()
    {
        const int missingProcessId = 999999;
        var backendFactory = new FakeBackendFactory();
        var targetCatalog = new FakeTargetCatalog(
            new CompanionTargetInfo
            {
                ProcessId = missingProcessId,
                ProcessName = "demo-app",
                WindowTitle = "Demo window",
                IsAttachable = false
            });
        using var coordinator = CreateCoordinator(
            backendFactory,
            targetCatalog: targetCatalog,
            windowInspector: new EmptyWindowInspector());

        var snapshot = await coordinator.StartRecordingAsync(missingProcessId);

        Assert.Equal("demo-app", snapshot.ProcessName);
        Assert.Equal("Demo window", snapshot.WindowTitle);
    }

    [Fact]
    public async Task StartRecordingAsync_uses_default_fallback_when_catalog_does_not_include_process()
    {
        const int missingProcessId = 999998;
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(
            backendFactory,
            targetCatalog: new FakeTargetCatalog(),
            windowInspector: new EmptyWindowInspector());

        var snapshot = await coordinator.StartRecordingAsync(missingProcessId);

        Assert.Equal($"pid-{missingProcessId}", snapshot.ProcessName);
        Assert.Equal($"PID {missingProcessId}", snapshot.WindowTitle);
    }

    [Fact]
    public async Task Event_capture_trims_retained_events_and_updates_humanized_descriptions()
    {
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(
            backendFactory,
            options: new DesktopCompanionOptions { MaxRetainedEvents = 2 });

        await coordinator.StartRecordingAsync(101);

        backendFactory.Emit(101, CreateRecordedEvent(101, EventKind.Invoke, automationId: "clearButton"));
        backendFactory.Emit(101, CreateRecordedEvent(101, EventKind.KeyDown, key: "Enter"));
        backendFactory.Emit(101, CreateRecordedEvent(101, EventKind.WindowOpened, name: "History"));

        var session = coordinator.GetSnapshot().Sessions.Single();
        Assert.Equal(2, session.CapturedEventCount);
        Assert.Equal("Opened History", session.LastEventSummary);
    }

    [Fact]
    public async Task Value_changed_events_use_readable_descriptions()
    {
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(backendFactory);

        await coordinator.StartRecordingAsync(101);
        backendFactory.Emit(101, CreateRecordedEvent(101, EventKind.ValueChanged, automationId: "display", value: "42"));

        var session = coordinator.GetSnapshot().Sessions.Single();
        Assert.Equal("Changed display to 42", session.LastEventSummary);
    }

    [Fact]
    public async Task Dispose_prevents_new_sessions_and_unknown_sessions_throw()
    {
        var backendFactory = new FakeBackendFactory();
        using var coordinator = CreateCoordinator(backendFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PauseRecordingAsync(404));

        coordinator.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.StartRecordingAsync(101));
        Assert.Throws<ObjectDisposedException>(() => coordinator.GetSnapshot());
    }

    private static DesktopCompanionCoordinator CreateCoordinator(
        FakeBackendFactory backendFactory,
        ICompanionTargetCatalog? targetCatalog = null,
        ICompanionWindowInspector? windowInspector = null,
        ICompanionPreviewProvider? previewProvider = null,
        DesktopCompanionOptions? options = null)
        => new(
            backendFactory,
            targetCatalog ?? new FakeTargetCatalog(),
            windowInspector ?? new FakeWindowInspector(),
            previewProvider ?? new FakePreviewProvider(),
            new FakeClock(),
            options);

    private static RecordedEvent CreateRecordedEvent(
        int processId,
        EventKind kind,
        string? automationId = null,
        string? key = null,
        string? name = null,
        string? value = null)
        => new()
        {
            Kind = kind,
            Timestamp = DateTimeOffset.UtcNow,
            Key = key,
            Value = value,
            Element = new ElementInfo
            {
                ProcessId = processId,
                AutomationId = automationId ?? string.Empty,
                Name = name ?? automationId ?? "target",
                ControlType = "button"
            }
        };

    private sealed class FakeBackendFactory : ICompanionSessionBackendFactory
    {
        private readonly Dictionary<int, FakeBackend> _backends = new();
        private readonly Dictionary<int, Action<FakeBackend>> _configurations = new();
        private readonly Dictionary<int, int> _createCounts = new();

        public ICompanionSessionBackend Create(int processId)
        {
            var backend = new FakeBackend(processId);
            if (_configurations.TryGetValue(processId, out var configure))
            {
                configure(backend);
            }

            _backends[processId] = backend;
            _createCounts[processId] = _createCounts.TryGetValue(processId, out var count) ? count + 1 : 1;
            return backend;
        }

        public int CreateCount(int processId)
            => _createCounts.TryGetValue(processId, out var count) ? count : 0;

        public FakeBackend GetBackend(int processId)
            => _backends[processId];

        public void Configure(int processId, Action<FakeBackend> configure)
            => _configurations[processId] = configure;

        public void EmitInvoke(int processId, string automationId)
            => Emit(processId, CreateRecordedEvent(processId, EventKind.Invoke, automationId: automationId));

        public void Emit(int processId, RecordedEvent recordedEvent)
        {
            _backends[processId].Emit(recordedEvent);
        }
    }

    private sealed class FakeBackend(int processId) : ICompanionSessionBackend
    {
        private readonly List<RecordedEvent> _events = [];

        public event Action<RecordedEvent>? EventCaptured;

        public Exception? StartException { get; set; }
        public IReadOnlyList<RecordedEvent> StoppedEvents { get; set; } = [];
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public void Start()
        {
            StartCalls++;
            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public IReadOnlyList<RecordedEvent> Stop()
        {
            StopCalls++;
            return StoppedEvents.Count > 0 ? StoppedEvents : _events.ToList();
        }

        public void Emit(RecordedEvent recordedEvent)
        {
            Assert.Equal(processId, recordedEvent.Element.ProcessId);
            _events.Add(recordedEvent);
            EventCaptured?.Invoke(recordedEvent);
        }

        public void Dispose()
        {
            DisposeCalls++;
        }
    }

    private sealed class FakeTargetCatalog(params CompanionTargetInfo[] targets) : ICompanionTargetCatalog
    {
        private readonly IReadOnlyList<CompanionTargetInfo> _targets = targets.Length == 0
            ? [
                new CompanionTargetInfo { ProcessId = 101, ProcessName = "calc", WindowTitle = "Calculator", IsAttachable = true },
                new CompanionTargetInfo { ProcessId = 202, ProcessName = "notepad", WindowTitle = "Notes", IsAttachable = true }
            ]
            : targets;

        public Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync()
            => Task.FromResult(_targets);
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

    private sealed class EmptyWindowInspector : ICompanionWindowInspector
    {
        public CompanionWindowState Inspect(int processId) => new();
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
