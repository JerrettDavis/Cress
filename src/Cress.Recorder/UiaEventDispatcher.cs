using System.Collections.Concurrent;
using System.Windows.Automation;

namespace Cress.Recorder;

/// <summary>
/// Subscribes to Windows UI Automation events on a target window (and its subtree)
/// and marshals them into <see cref="RecordedEvent"/> objects on a thread-safe queue.
/// </summary>
internal sealed class UiaEventDispatcher : IDisposable
{
    private readonly AutomationElement _targetWindow;
    private readonly int _targetProcessId;
    private readonly ConcurrentQueue<RecordedEvent> _queue;
    private readonly Action<RecordedEvent> _onEvent;

    private readonly AutomationEventHandler _invokedHandler;
    private readonly AutomationPropertyChangedEventHandler _propertyChangedHandler;
    private readonly AutomationFocusChangedEventHandler _focusChangedHandler;
    private readonly AutomationEventHandler _windowOpenedHandler;

    private int _sequence;
    private bool _disposed;

    public UiaEventDispatcher(
        AutomationElement targetWindow,
        int targetProcessId,
        ConcurrentQueue<RecordedEvent> queue,
        Action<RecordedEvent> onEvent)
    {
        _targetWindow = targetWindow;
        _targetProcessId = targetProcessId;
        _queue = queue;
        _onEvent = onEvent;

        _invokedHandler = HandleInvoked;
        _propertyChangedHandler = HandlePropertyChanged;
        _focusChangedHandler = HandleFocusChanged;
        _windowOpenedHandler = HandleWindowOpened;
    }

    /// <summary>Registers all UIA event handlers on the target window subtree.</summary>
    public void RegisterAll()
    {
        Automation.AddAutomationEventHandler(
            InvokePatternIdentifiers.InvokedEvent,
            _targetWindow,
            TreeScope.Subtree,
            _invokedHandler);

        Automation.AddAutomationPropertyChangedEventHandler(
            _targetWindow,
            TreeScope.Subtree,
            _propertyChangedHandler,
            ValuePatternIdentifiers.ValueProperty,
            AutomationElement.NameProperty);

        Automation.AddAutomationFocusChangedEventHandler(_focusChangedHandler);

        Automation.AddAutomationEventHandler(
            WindowPattern.WindowOpenedEvent,
            AutomationElement.RootElement,
            TreeScope.Subtree,
            _windowOpenedHandler);
    }

    private void HandleInvoked(object sender, AutomationEventArgs e)
    {
        if (_disposed) return;
        if (sender is not AutomationElement element) return;

        var info = SnapshotElement(element);
        if (info is null || info.ProcessId != _targetProcessId) return;

        Emit(new RecordedEvent
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Timestamp = DateTimeOffset.Now,
            Kind = EventKind.Invoke,
            Element = info
        });
    }

    private void HandlePropertyChanged(object sender, AutomationPropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (sender is not AutomationElement element) return;

        var info = SnapshotElement(element);
        if (info is null || info.ProcessId != _targetProcessId) return;

        Emit(new RecordedEvent
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Timestamp = DateTimeOffset.Now,
            Kind = EventKind.ValueChanged,
            Element = info,
            Value = e.NewValue?.ToString() ?? info.Name
        });
    }

    private void HandleFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        if (_disposed) return;
        if (sender is not AutomationElement element) return;

        var info = SnapshotElement(element);
        if (info is null || info.ProcessId != _targetProcessId) return;

        Emit(new RecordedEvent
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Timestamp = DateTimeOffset.Now,
            Kind = EventKind.FocusChanged,
            Element = info
        });
    }

    private void HandleWindowOpened(object sender, AutomationEventArgs e)
    {
        if (_disposed) return;
        if (sender is not AutomationElement element) return;

        var info = SnapshotElement(element);
        if (info is null || info.ProcessId != _targetProcessId) return;

        Emit(new RecordedEvent
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Timestamp = DateTimeOffset.Now,
            Kind = EventKind.WindowOpened,
            Element = info
        });
    }

    private void Emit(RecordedEvent evt)
    {
        _queue.Enqueue(evt);
        try { _onEvent(evt); }
        catch { }
    }

    private static ElementInfo? SnapshotElement(AutomationElement element)
    {
        try
        {
            var current = element.Current;
            return new ElementInfo
            {
                Name = current.Name ?? string.Empty,
                AutomationId = current.AutomationId ?? string.Empty,
                ControlType = current.ControlType.ProgrammaticName ?? string.Empty,
                ClassName = current.ClassName ?? string.Empty,
                FrameworkId = current.FrameworkId ?? string.Empty,
                ProcessId = current.ProcessId,
                RuntimeId = element.GetRuntimeId() ?? []
            };
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Automation.RemoveAutomationEventHandler(
                InvokePatternIdentifiers.InvokedEvent,
                _targetWindow,
                _invokedHandler);
        }
        catch { }

        try
        {
            Automation.RemoveAutomationPropertyChangedEventHandler(
                _targetWindow,
                _propertyChangedHandler);
        }
        catch { }

        try
        {
            Automation.RemoveAutomationFocusChangedEventHandler(_focusChangedHandler);
        }
        catch { }

        try
        {
            Automation.RemoveAutomationEventHandler(
                WindowPattern.WindowOpenedEvent,
                AutomationElement.RootElement,
                _windowOpenedHandler);
        }
        catch { }
    }
}
