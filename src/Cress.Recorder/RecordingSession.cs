using System.Collections.Concurrent;
using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Cress.Recorder;

/// <summary>
/// Attaches to a running process and records UIA events until <see cref="Stop"/> is called.
/// Thread-safe: event handlers fire on the UIA COM background thread; all state is
/// protected by <see cref="ConcurrentQueue{T}"/> and <see cref="Interlocked"/>.
/// </summary>
public sealed class RecordingSession : IDisposable
{
    private readonly int _processId;
    private readonly UIA3Automation _automation;
    private readonly ConcurrentQueue<RecordedEvent> _events = new();
    private UiaEventDispatcher? _dispatcher;
    private bool _disposed;

    /// <summary>Raised on the UIA background thread for each captured event.</summary>
    public event Action<RecordedEvent>? EventCaptured;

    private RecordingSession(int processId)
    {
        _processId = processId;
        _automation = new UIA3Automation();
    }

    /// <summary>Creates a session attached to an already-running process by PID.</summary>
    public static RecordingSession FromProcessId(int processId)
        => new(processId);

    /// <summary>Creates a session attached to the first running process matching <paramref name="processName"/>.</summary>
    public static RecordingSession FromProcessName(string processName)
    {
        var process = Process.GetProcessesByName(processName).FirstOrDefault(p => !p.HasExited)
            ?? throw new InvalidOperationException($"No running process named '{processName}' was found.");
        return new RecordingSession(process.Id);
    }

    /// <summary>
    /// Attaches FlaUI to the target process and registers UIA event handlers.
    /// Must be called before events will be captured.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var app = FlaUI.Core.Application.Attach(_processId);
        var window = app.GetMainWindow(_automation, TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException($"Could not locate main window for PID {_processId}.");

        _dispatcher = new UiaEventDispatcher(
            _automation,
            window,
            _processId,
            _events,
            evt => EventCaptured?.Invoke(evt));

        _dispatcher.RegisterAll();
    }

    /// <summary>
    /// Unregisters all UIA event handlers and returns the captured events.
    /// </summary>
    public IReadOnlyList<RecordedEvent> Stop()
    {
        _dispatcher?.Dispose();
        _dispatcher = null;
        return _events.ToArray();
    }

    /// <summary>Live snapshot of events captured so far (thread-safe).</summary>
    public IReadOnlyList<RecordedEvent> Events => _events.ToArray();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dispatcher?.Dispose();
        _automation.Dispose();
    }
}
