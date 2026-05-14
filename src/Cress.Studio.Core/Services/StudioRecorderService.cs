using System.Diagnostics;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Recorder;
using Cress.Recorder.Inference;
using ComponentModel = System.ComponentModel;

namespace Cress.Studio.Services;

internal interface IDesktopRecordingSession : IDisposable
{
    event Action<RecordedEvent>? EventCaptured;
    void Start();
    IReadOnlyList<RecordedEvent> Stop();
}

internal interface IDesktopRecordingSessionFactory
{
    IDesktopRecordingSession Create(int processId);
}

internal interface IWebRecordingClient : IDisposable
{
    event Action<RecordedEvent>? EventCaptured;
    Task StartAsync(string url, string browserType, CancellationToken ct);
    Task<IReadOnlyList<RecordedEvent>> StopAsync();
}

internal interface IProcessInfo : IDisposable
{
    IntPtr MainWindowHandle { get; }
    string MainWindowTitle { get; }
    int Id { get; }
    string ProcessName { get; }
    string? MainModuleFileName { get; }
}

internal delegate IWebRecordingClient WebRecordingClientFactory();
internal delegate RecordingTargetInfo ResolveRecordingTarget(int processId);
internal delegate IEnumerable<IProcessInfo> ProcessInfoEnumerator();

internal sealed class ProcessInfoAdapter(Process process) : IProcessInfo
{
    public IntPtr MainWindowHandle => process.MainWindowHandle;

    public string MainWindowTitle => process.MainWindowTitle;

    public int Id => process.Id;

    public string ProcessName => process.ProcessName;

    public string? MainModuleFileName => process.MainModule?.FileName;

    public void Dispose() => process.Dispose();
}


internal sealed class DesktopRecordingSessionAdapter(RecordingSession session) : IDesktopRecordingSession
{
    public event Action<RecordedEvent>? EventCaptured
    {
        add => session.EventCaptured += value;
        remove => session.EventCaptured -= value;
    }

    public void Start() => session.Start();

    public IReadOnlyList<RecordedEvent> Stop() => session.Stop();

    public void Dispose() => session.Dispose();
}

internal sealed class DesktopRecordingSessionFactory : IDesktopRecordingSessionFactory
{
    public IDesktopRecordingSession Create(int processId)
        => new DesktopRecordingSessionAdapter(RecordingSession.FromProcessId(processId));
}

internal sealed class WebRecordingClientAdapter(WebRecorderClient client) : IWebRecordingClient
{
    public event Action<RecordedEvent>? EventCaptured
    {
        add => client.EventCaptured += value;
        remove => client.EventCaptured -= value;
    }

    public Task StartAsync(string url, string browserType, CancellationToken ct)
        => client.StartAsync(url, browserType, ct);

    public Task<IReadOnlyList<RecordedEvent>> StopAsync()
        => client.StopAsync();

    public void Dispose() => client.Dispose();
}

/// <summary>
/// Wraps <see cref="RecordingSession"/> (desktop) and <see cref="WebRecorderClient"/> (web)
/// and exposes recording state for the Blazor Studio.
/// Registered as <c>Scoped</c> — one instance per Blazor circuit (one per browser tab).
/// Thread-safe: UIA events fire on COM background threads; all state writes are guarded by
/// <see cref="Interlocked"/> / <see cref="volatile"/> / <see cref="System.Threading.Lock"/>.
/// </summary>
public sealed class StudioRecorderService : IStudioRecorderService, IDisposable
{
    private readonly RuntimeOrchestrator _orchestrator;
    private readonly IDesktopRecordingSessionFactory _sessionFactory;
    private readonly WebRecordingClientFactory _webClientFactory;
    private readonly ResolveRecordingTarget _resolveTarget;
    private readonly ProcessInfoEnumerator _enumerateProcesses;
    private readonly object _lock = new();

    private IDesktopRecordingSession? _session;
    private IWebRecordingClient? _webClient;

    /// <summary>Tracks which recording mode is currently active.</summary>
    private enum ActiveDomain { None, Desktop, Web }
    private ActiveDomain _activeRecordingDomain = ActiveDomain.None;

    private RecordingTargetInfo? _currentTarget;
    private int _capturedEventCount;
    private DateTimeOffset _recordingStartedAt;
    private bool _isRecording;
    private bool _disposed;

    // Accumulated live events (guarded by _lock for writes, snapshot for reads).
    private List<RecordedEvent> _liveEvents = [];
    // Cached inference result (recomputed on each debounce tick while recording).
    private IReadOnlyList<InferredStep> _liveSteps = [];

    // Shared inference engine instance — stateless, safe to reuse.
    private static readonly StepInferenceEngine _engine = new();

    // Debounce: suppress rapid StateChanged notifications to ~250 ms intervals.
    private readonly System.Threading.Timer _debounceTimer;
    private volatile bool _pendingNotification;

    public bool IsRecording => _isRecording;
    public RecordingTargetInfo? CurrentTarget => _currentTarget;
    public int CapturedEventCount => _capturedEventCount;
    public TimeSpan Elapsed => _isRecording
        ? DateTimeOffset.UtcNow - _recordingStartedAt
        : TimeSpan.Zero;

    public IReadOnlyList<RecordedEvent> CurrentEvents
    {
        get
        {
            lock (_lock)
            {
                return _liveEvents.Count == 0 ? [] : _liveEvents.ToList();
            }
        }
    }

    public IReadOnlyList<InferredStep> CurrentInferredSteps => _liveSteps;

    public event Action? StateChanged;

    public StudioRecorderService(RuntimeOrchestrator orchestrator)
        : this(
            orchestrator,
            new DesktopRecordingSessionFactory(),
            static () => new WebRecordingClientAdapter(new WebRecorderClient()),
            ResolveDesktopTarget,
            EnumerateProcesses)
    {
    }

    internal StudioRecorderService(
        RuntimeOrchestrator orchestrator,
        IDesktopRecordingSessionFactory sessionFactory,
        WebRecordingClientFactory webClientFactory,
        ResolveRecordingTarget resolveTarget,
        ProcessInfoEnumerator? enumerateProcesses = null)
    {
        _orchestrator = orchestrator;
        _sessionFactory = sessionFactory;
        _webClientFactory = webClientFactory;
        _resolveTarget = resolveTarget;
        _enumerateProcesses = enumerateProcesses ?? EnumerateProcesses;
        _debounceTimer = new System.Threading.Timer(
            _ => FirePendingNotification(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public Task<IReadOnlyList<RecordingTargetInfo>> ListAvailableTargetsAsync()
    {
        var results = new List<RecordingTargetInfo>();

        foreach (var processInfo in _enumerateProcesses())
        {
            using var process = processInfo;
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                var title = process.MainWindowTitle;
                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                string? moduleFileName = null;
                bool isAttachable = true;
                try
                {
                    moduleFileName = process.MainModuleFileName;
                }
                catch (ComponentModel.Win32Exception)
                {
                    // Access denied to elevated/system process — still list it but flag it.
                    isAttachable = false;
                }
                catch (InvalidOperationException)
                {
                    // Process has already exited — skip.
                    continue;
                }

                results.Add(new RecordingTargetInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    MainWindowTitle = title,
                    MainModuleFileName = moduleFileName,
                    IsAttachable = isAttachable
                });
            }
            catch (InvalidOperationException)
            {
                // Process exited between enumeration and property access — skip silently.
            }
            catch (ComponentModel.Win32Exception)
            {
                // Cannot read process info — skip silently.
            }
        }

        results.Sort((a, b) => string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<RecordingTargetInfo>>(results);
    }

    public Task StartRecordingAsync(int processId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock!)
        {
            if (_isRecording)
            {
                StopActiveSessionUnsafe();
            }

            _currentTarget = _resolveTarget(processId);

            _session = _sessionFactory.Create(processId);
            _session.EventCaptured += OnEventCaptured;
            _session.Start();

            _capturedEventCount = 0;
            _liveEvents = [];
            _liveSteps = [];
            _recordingStartedAt = DateTimeOffset.UtcNow;
            _isRecording = true;
            _activeRecordingDomain = ActiveDomain.Desktop;
        }

        ScheduleNotification();
        return Task.CompletedTask;
    }

    public async Task StartWebRecordingAsync(string url, string browserType, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock!)
        {
            if (_isRecording)
            {
                StopActiveSessionUnsafe();
            }

            _currentTarget = new RecordingTargetInfo
            {
                ProcessId = 0,
                ProcessName = "browser",
                MainWindowTitle = $"{browserType} — {url}",
                IsAttachable = true
            };

            _capturedEventCount = 0;
            _liveEvents = [];
            _liveSteps = [];
            _recordingStartedAt = DateTimeOffset.UtcNow;
            _isRecording = true;
            _activeRecordingDomain = ActiveDomain.Web;

            _webClient = _webClientFactory();
            _webClient.EventCaptured += OnEventCaptured;
        }

        await _webClient!.StartAsync(url, browserType, ct).ConfigureAwait(false);
        ScheduleNotification();
    }

    public async Task<RecordingResult> StopRecordingAsync()
    {
        IDesktopRecordingSession? session;
        IWebRecordingClient? webClient;
        RecordingTargetInfo? target;
        ActiveDomain domain;
        TimeSpan duration;

        lock (_lock!)
        {
            if (!_isRecording)
            {
                return new RecordingResult();
            }

            session = _session;
            webClient = _webClient;
            target = _currentTarget;
            domain = _activeRecordingDomain;
            duration = Elapsed;

            _isRecording = false;
            _session = null;
            _webClient = null;
            _activeRecordingDomain = ActiveDomain.None;
        }

        IReadOnlyList<RecordedEvent> rawEvents;

        if (domain == ActiveDomain.Web && webClient is not null)
        {
            rawEvents = await webClient.StopAsync().ConfigureAwait(false);
            webClient.Dispose();
        }
        else if (session is not null)
        {
            rawEvents = session.Stop();
            session.Dispose();
        }
        else
        {
            rawEvents = [];
        }

        // Clear live state now that recording is stopped.
        lock (_lock)
        {
            _liveEvents = [];
        }
        _liveSteps = [];

        var options = new InferenceOptions
        {
            Domain = domain == ActiveDomain.Web
                ? Cress.Recorder.Inference.InferenceDomain.Web
                : Cress.Recorder.Inference.InferenceDomain.Desktop,
            DebounceWindow = TimeSpan.FromMilliseconds(50),
            IgnoreFocusEvents = true,
            TargetProcessId = domain == ActiveDomain.Desktop ? target?.ProcessId : null,
        };
        var steps = _engine.Infer(rawEvents, options);

        ScheduleNotification();

        return new RecordingResult
        {
            Events = rawEvents,
            Steps = steps,
            Duration = duration,
            ProcessName = target?.ProcessName
        };
    }

    public async Task<RecordingReplayResult> ReplayRecordedFlowAsync(string flowFilePath, string projectPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var options = new RunOptions
            {
                FlowPaths = [flowFilePath],
                Profile = "local",
                Trigger = "replay-recorded"
            };

            var runResult = await _orchestrator.ExecuteAsync(projectPath, options);

            sw.Stop();
            var flow = runResult.Flows.FirstOrDefault();
            var stepResults = flow?.Steps
                .Select(s => $"{s.Name}: {s.Outcome}{(string.IsNullOrWhiteSpace(s.Message) ? string.Empty : " — " + s.Message)}")
                .ToList() ?? [];

            var passed = runResult.Passed;
            var stepCount = flow?.Steps.Count ?? 0;
            var durationSec = (int)sw.Elapsed.TotalSeconds;
            var summary = passed
                ? $"Passed ({stepCount} steps in {durationSec}s)"
                : $"Failed at step {flow?.Steps.FirstOrDefault(s => s.Outcome != RunOutcome.Passed && s.Outcome != RunOutcome.Skipped)?.Name ?? "?"}: {flow?.FailureMessage ?? runResult.Diagnostics.FirstOrDefault()?.Message ?? "unknown error"}";

            return new RecordingReplayResult
            {
                Passed = passed,
                Summary = summary,
                StepResults = stepResults,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new RecordingReplayResult
            {
                Passed = false,
                Summary = $"Replay error: {ex.Message}",
                StepResults = [],
                Duration = sw.Elapsed
            };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debounceTimer.Dispose();

        lock (_lock!)
        {
            _session?.Dispose();
            _session = null;
            _webClient?.Dispose();
            _webClient = null;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Tears down any active session without acquiring <see cref="_lock"/>.
    /// Must only be called while the caller already holds <see cref="_lock"/>.
    /// </summary>
    private void StopActiveSessionUnsafe()
    {
        _session?.Dispose();
        _session = null;
        _webClient?.Dispose();
        _webClient = null;
        _activeRecordingDomain = ActiveDomain.None;
        _isRecording = false;
    }

    private void OnEventCaptured(RecordedEvent evt)
    {
        lock (_lock)
        {
            _liveEvents.Add(evt);
        }
        Interlocked.Increment(ref _capturedEventCount);
        ScheduleNotification();
    }

    private void ScheduleNotification()
    {
        _pendingNotification = true;
        // Reset the debounce timer to fire 250 ms from now.
        _debounceTimer.Change(250, Timeout.Infinite);
    }

    private void FirePendingNotification()
    {
        if (!_pendingNotification) return;
        _pendingNotification = false;

        // Recompute live inference while recording.
        if (_isRecording)
        {
            IReadOnlyList<RecordedEvent> snapshot;
            int? targetPid;
            lock (_lock)
            {
                snapshot = _liveEvents.ToList();
                targetPid = _currentTarget?.ProcessId;
            }
            var options = new InferenceOptions
            {
                DebounceWindow = TimeSpan.FromMilliseconds(50),
                IgnoreFocusEvents = true,
                TargetProcessId = targetPid
            };
            _liveSteps = _engine.Infer(snapshot, options);
        }

        StateChanged?.Invoke();
    }

    private static IEnumerable<IProcessInfo> EnumerateProcesses()
        => Process.GetProcesses().Select(static process => (IProcessInfo)new ProcessInfoAdapter(process));

    private static RecordingTargetInfo ResolveDesktopTarget(int processId)
    {
        using var target = Process.GetProcessById(processId);
        return new RecordingTargetInfo
        {
            ProcessId = processId,
            ProcessName = target.ProcessName,
            MainWindowTitle = target.MainWindowTitle,
            IsAttachable = true
        };
    }
}
