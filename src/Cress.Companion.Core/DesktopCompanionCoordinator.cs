using Cress.Recorder;
using Cress.Recorder.Inference;

namespace Cress.Companion;

public sealed class DesktopCompanionCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, ManagedSession> _sessions = new();
    private readonly ICompanionSessionBackendFactory _sessionFactory;
    private readonly ICompanionTargetCatalog _targetCatalog;
    private readonly ICompanionWindowInspector _windowInspector;
    private readonly ICompanionPreviewProvider _previewProvider;
    private readonly ICompanionClock _clock;
    private readonly DesktopCompanionOptions _options;
    private readonly StepInferenceEngine _engine = new();
    private bool _disposed;

    public DesktopCompanionCoordinator(
        ICompanionSessionBackendFactory sessionFactory,
        ICompanionTargetCatalog targetCatalog,
        ICompanionWindowInspector windowInspector,
        ICompanionPreviewProvider previewProvider,
        ICompanionClock clock,
        DesktopCompanionOptions? options = null)
    {
        _sessionFactory = sessionFactory;
        _targetCatalog = targetCatalog;
        _windowInspector = windowInspector;
        _previewProvider = previewProvider;
        _clock = clock;
        _options = options ?? new DesktopCompanionOptions();
    }

    public event Action? Changed;

    public Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync()
        => _targetCatalog.ListTargetsAsync();

    public Task<CompanionSessionSnapshot> StartRecordingAsync(int processId, bool overlayEnabled = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CompanionSessionSnapshot? existingSnapshot;
        lock (_gate)
        {
            if (_sessions.TryGetValue(processId, out var existing) && existing.Status is CompanionSessionStatus.Recording or CompanionSessionStatus.Paused)
            {
                existing.OverlayEnabled = overlayEnabled;
                existingSnapshot = BuildSnapshot(existing, includePreview: false);
            }
            else
            {
                existingSnapshot = null;
            }
        }

        if (existingSnapshot is not null)
        {
            RaiseChanged();
            return Task.FromResult(existingSnapshot);
        }

        var target = ResolveTarget(processId);
        var backend = _sessionFactory.Create(processId);
        var session = new ManagedSession(processId, target.ProcessName, target.WindowTitle, _clock.UtcNow, backend)
        {
            OverlayEnabled = overlayEnabled,
            WindowState = _windowInspector.Inspect(processId)
        };

        backend.EventCaptured += evt => OnEventCaptured(session, evt);

        try
        {
            backend.Start();
        }
        catch (Exception ex)
        {
            backend.Dispose();
            session.Status = CompanionSessionStatus.Faulted;
            session.ErrorMessage = ex.Message;
            lock (_gate)
            {
                _sessions[processId] = session;
            }

            RaiseChanged();
            return Task.FromResult(BuildSnapshot(session, includePreview: false));
        }

        lock (_gate)
        {
            _sessions[processId] = session;
        }

        RaiseChanged();
        return Task.FromResult(BuildSnapshot(session, includePreview: false));
    }

    public Task<CompanionSessionSnapshot> PauseRecordingAsync(int processId)
    {
        var session = GetSession(processId);
        lock (_gate)
        {
            session.Status = session.Status == CompanionSessionStatus.Faulted
                ? CompanionSessionStatus.Faulted
                : CompanionSessionStatus.Paused;
        }

        RaiseChanged();
        return Task.FromResult(BuildSnapshot(session, includePreview: false));
    }

    public Task<CompanionSessionSnapshot> ResumeRecordingAsync(int processId)
    {
        var session = GetSession(processId);
        lock (_gate)
        {
            if (session.Status != CompanionSessionStatus.Faulted)
            {
                session.Status = CompanionSessionStatus.Recording;
            }
        }

        RaiseChanged();
        return Task.FromResult(BuildSnapshot(session, includePreview: false));
    }

    public Task<CompanionSessionSnapshot> StopRecordingAsync(int processId)
    {
        var session = GetSession(processId);

        IReadOnlyList<RecordedEvent> stoppedEvents;
        lock (_gate)
        {
            if (session.Status == CompanionSessionStatus.Stopped)
            {
                return Task.FromResult(BuildSnapshot(session, includePreview: false));
            }

            session.Status = session.Status == CompanionSessionStatus.Faulted
                ? CompanionSessionStatus.Faulted
                : CompanionSessionStatus.Stopped;
            session.EndedAtUtc = _clock.UtcNow;
        }

        stoppedEvents = session.Backend.Stop();
        lock (_gate)
        {
            if (session.Events.Count == 0 && stoppedEvents.Count > 0)
            {
                session.Events.AddRange(stoppedEvents.Take(_options.MaxRetainedEvents));
                session.InferredSteps = InferSteps(session.Events, session.ProcessId);
            }
        }
        session.Backend.Dispose();

        RaiseChanged();
        return Task.FromResult(BuildSnapshot(session, includePreview: false));
    }

    public Task<CompanionSessionSnapshot> SetOverlayEnabledAsync(int processId, bool overlayEnabled)
    {
        var session = GetSession(processId);
        lock (_gate)
        {
            session.OverlayEnabled = overlayEnabled;
        }

        RaiseChanged();
        return Task.FromResult(BuildSnapshot(session, includePreview: false));
    }

    public CompanionServiceSnapshot GetSnapshot(bool includePreview = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<CompanionSessionSnapshot> snapshots;
        lock (_gate)
        {
            snapshots = _sessions.Values
                .OrderByDescending(session => session.Status == CompanionSessionStatus.Recording)
                .ThenByDescending(session => session.Status == CompanionSessionStatus.Paused)
                .ThenBy(session => session.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(session => BuildSnapshot(session, includePreview))
                .ToList();
        }

        return new CompanionServiceSnapshot
        {
            GeneratedAtUtc = _clock.UtcNow,
            Sessions = snapshots
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            foreach (var session in _sessions.Values)
            {
                session.Backend.Dispose();
            }

            _sessions.Clear();
        }
    }

    private CompanionSessionSnapshot BuildSnapshot(ManagedSession session, bool includePreview)
    {
        RefreshWindowState(session, includePreview);

        var now = _clock.UtcNow;
        var endedAt = session.EndedAtUtc;
        var elapsed = (endedAt ?? now) - session.StartedAtUtc;

        return new CompanionSessionSnapshot
        {
            ProcessId = session.ProcessId,
            ProcessName = session.ProcessName,
            WindowTitle = string.IsNullOrWhiteSpace(session.WindowState.WindowTitle) ? session.WindowTitle : session.WindowState.WindowTitle,
            Status = session.Status,
            StartedAtUtc = session.StartedAtUtc,
            EndedAtUtc = endedAt,
            Elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed,
            CapturedEventCount = session.Events.Count,
            InferredStepCount = session.InferredSteps.Count,
            SuppressedEventCount = session.SuppressedEventCount,
            LastEventSummary = session.LastEventSummary,
            LastStepSummary = session.LastStepSummary,
            ErrorMessage = session.ErrorMessage,
            WindowBounds = session.WindowState.Bounds,
            IsWindowVisible = session.WindowState.IsVisible,
            OverlayEnabled = session.OverlayEnabled,
            PreviewImageDataUrl = includePreview ? session.PreviewImageDataUrl : null,
            InferredSteps = session.InferredSteps.ToList()
        };
    }

    private void RefreshWindowState(ManagedSession session, bool includePreview)
    {
        session.WindowState = _windowInspector.Inspect(session.ProcessId);
        if (includePreview && session.WindowState.Bounds is { Width: > 0, Height: > 0 })
        {
            session.PreviewImageDataUrl = _previewProvider.CapturePreview(session.WindowState.Bounds);
        }
        else if (!includePreview)
        {
            session.PreviewImageDataUrl = null;
        }
    }

    private ManagedSession GetSession(int processId)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(processId, out var session))
            {
                return session;
            }
        }

        throw new InvalidOperationException($"Desktop companion session for PID {processId} was not found.");
    }

    private CompanionTargetInfo ResolveTarget(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return new CompanionTargetInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                WindowTitle = process.MainWindowTitle,
                IsAttachable = true
            };
        }
        catch (Exception)
        {
            var catalogTarget = _targetCatalog.ListTargetsAsync().GetAwaiter().GetResult()
                .FirstOrDefault(target => target.ProcessId == processId);
            if (catalogTarget is not null)
            {
                return catalogTarget;
            }

            return new CompanionTargetInfo
            {
                ProcessId = processId,
                ProcessName = $"pid-{processId}",
                WindowTitle = $"PID {processId}",
                IsAttachable = false,
                MainModuleFileName = null
            };
        }
    }

    private void OnEventCaptured(ManagedSession session, RecordedEvent recordedEvent)
    {
        lock (_gate)
        {
            if (session.Status == CompanionSessionStatus.Paused)
            {
                session.SuppressedEventCount++;
                return;
            }

            if (session.Status is CompanionSessionStatus.Stopped or CompanionSessionStatus.Faulted)
            {
                return;
            }

            session.WindowState = _windowInspector.Inspect(session.ProcessId);
            session.Events.Add(recordedEvent);
            if (session.Events.Count > _options.MaxRetainedEvents)
            {
                session.Events.RemoveAt(0);
            }

            session.InferredSteps = InferSteps(session.Events, session.ProcessId);
            session.LastEventSummary = DescribeEvent(recordedEvent);
            session.LastStepSummary = session.InferredSteps.LastOrDefault()?.ToString() ?? "No steps inferred yet";
        }

        RaiseChanged();
    }

    private IReadOnlyList<InferredStep> InferSteps(IReadOnlyList<RecordedEvent> events, int processId)
    {
        var options = new InferenceOptions
        {
            Domain = InferenceDomain.Desktop,
            DebounceWindow = TimeSpan.FromMilliseconds(50),
            IgnoreFocusEvents = true,
            AssertionTargetAutomationId = _options.AssertionTargetAutomationId,
            TargetProcessId = processId
        };

        return _engine.Infer(events, options);
    }

    private void RaiseChanged()
        => Changed?.Invoke();

    private static string DescribeEvent(RecordedEvent recordedEvent)
    {
        var target = recordedEvent.Element.AutomationId
                     ?? recordedEvent.Element.Name
                     ?? recordedEvent.Element.ControlType
                     ?? "target";

        return recordedEvent.Kind switch
        {
            EventKind.Invoke => $"Invoked {target}",
            EventKind.ValueChanged when !string.IsNullOrWhiteSpace(recordedEvent.Value) => $"Changed {target} to {recordedEvent.Value}",
            EventKind.KeyDown when !string.IsNullOrWhiteSpace(recordedEvent.Key) => $"Pressed {recordedEvent.Key}",
            EventKind.WindowOpened => $"Opened {recordedEvent.Element.Name}",
            _ => $"{recordedEvent.Kind} on {target}"
        };
    }

    private sealed class ManagedSession
    {
        public ManagedSession(int processId, string processName, string windowTitle, DateTimeOffset startedAtUtc, ICompanionSessionBackend backend)
        {
            ProcessId = processId;
            ProcessName = processName;
            WindowTitle = windowTitle;
            StartedAtUtc = startedAtUtc;
            Backend = backend;
        }

        public int ProcessId { get; }
        public string ProcessName { get; }
        public string WindowTitle { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? EndedAtUtc { get; set; }
        public ICompanionSessionBackend Backend { get; }
        public List<RecordedEvent> Events { get; } = [];
        public IReadOnlyList<InferredStep> InferredSteps { get; set; } = [];
        public CompanionSessionStatus Status { get; set; } = CompanionSessionStatus.Recording;
        public int SuppressedEventCount { get; set; }
        public string LastEventSummary { get; set; } = "No events yet";
        public string LastStepSummary { get; set; } = "No steps inferred yet";
        public string? ErrorMessage { get; set; }
        public CompanionWindowState WindowState { get; set; } = new();
        public bool OverlayEnabled { get; set; } = true;
        public string? PreviewImageDataUrl { get; set; }
    }
}
