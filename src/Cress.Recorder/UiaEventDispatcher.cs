using System.Collections.Concurrent;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;

namespace Cress.Recorder;

/// <summary>
/// Subscribes to FlaUI/UIA3 events on a target window (and its subtree) and
/// marshals them into <see cref="RecordedEvent"/> objects on a thread-safe queue.
///
/// Threading: all FlaUI event callbacks fire on a UIA COM background thread.
/// This class is safe to call from any thread for Dispose; event emission goes
/// to the queue and then to the caller's onEvent delegate.
/// </summary>
internal sealed class UiaEventDispatcher : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly AutomationElement _targetWindow;
    private readonly int _targetProcessId;
    private readonly ConcurrentQueue<RecordedEvent> _queue;
    private readonly Action<RecordedEvent> _onEvent;

    private int _sequence;
    private bool _disposed;

    // Returned handler objects — required for unregistration where available
    private FocusChangedEventHandlerBase? _focusHandler;

    public UiaEventDispatcher(
        UIA3Automation automation,
        AutomationElement targetWindow,
        int targetProcessId,
        ConcurrentQueue<RecordedEvent> queue,
        Action<RecordedEvent> onEvent)
    {
        _automation = automation;
        _targetWindow = targetWindow;
        _targetProcessId = targetProcessId;
        _queue = queue;
        _onEvent = onEvent;
    }

    /// <summary>Registers all UIA event handlers on the target window subtree.</summary>
    public void RegisterAll()
    {
        var invEventId = _automation.EventLibrary.Invoke.InvokedEvent;
        var valuePropId = _automation.PropertyLibrary.Value.Value;
        var namePropId = _automation.PropertyLibrary.Element.Name;
        var windowOpenedEventId = _automation.EventLibrary.Window.WindowOpenedEvent;

        // Invoke events on the target window subtree
        _targetWindow.RegisterAutomationEvent(
            invEventId,
            TreeScope.Subtree,
            (sender, _) => HandleInvoked(sender));

        // Value property changes on the target window subtree (e.g. text inputs)
        _targetWindow.RegisterPropertyChangedEvent(
            TreeScope.Subtree,
            (sender, prop, newVal) => HandlePropertyChanged(sender, prop, newVal),
            valuePropId);

        // Name property changes on the target window subtree.
        // Calculator's display element fires Name changes (not ValuePattern.Value changes),
        // so this second subscription is required for AssertText capture to work.
        _targetWindow.RegisterPropertyChangedEvent(
            TreeScope.Subtree,
            (sender, prop, newVal) => HandlePropertyChanged(sender, prop, newVal),
            namePropId);

        // Focus changed is automation-wide; filter by process ID in handler
        _focusHandler = _automation.RegisterFocusChangedEvent(
            sender => HandleFocusChanged(sender));

        // Window opened — desktop scope, filter by process ID in handler
        _automation.GetDesktop().RegisterAutomationEvent(
            windowOpenedEventId,
            TreeScope.Subtree,
            (sender, _) => HandleWindowOpened(sender));
    }

    private void HandleInvoked(AutomationElement sender)
    {
        if (_disposed) return;
        var info = SnapshotElement(sender);
        if (info is null) return;
        Emit(new RecordedEvent
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Timestamp = DateTimeOffset.Now,
            Kind = EventKind.Invoke,
            Element = info
        });
    }

    private void HandlePropertyChanged(AutomationElement sender, PropertyId prop, object newVal)
    {
        if (_disposed) return;
        var info = SnapshotElement(sender);
        if (info is null) return;
        if (info.ProcessId != _targetProcessId) return;

        // For Name property changes, the new value may be emitted before SnapshotElement
        // has refreshed the element's Name field (COM timing).  Use the event payload
        // directly so the engine's AssertionTargetAutomationId filter can act on it.
        var value = newVal?.ToString() ?? info.Name;

        Emit(new RecordedEvent
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Timestamp = DateTimeOffset.Now,
            Kind = EventKind.ValueChanged,
            Element = info,
            Value = value
        });
    }

    private void HandleFocusChanged(AutomationElement sender)
    {
        if (_disposed) return;
        ElementInfo? info;
        try { info = SnapshotElement(sender); }
        catch { return; }
        if (info is null || info.ProcessId != _targetProcessId) return;
        Emit(new RecordedEvent
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Timestamp = DateTimeOffset.Now,
            Kind = EventKind.FocusChanged,
            Element = info
        });
    }

    private void HandleWindowOpened(AutomationElement sender)
    {
        if (_disposed) return;
        ElementInfo? info;
        try { info = SnapshotElement(sender); }
        catch { return; }
        if (info is null || info.ProcessId != _targetProcessId) return;

        // Register Invoke + Value + Name events on newly opened child windows (best effort)
        try
        {
            var invEventId = _automation.EventLibrary.Invoke.InvokedEvent;
            var valuePropId = _automation.PropertyLibrary.Value.Value;
            var namePropId = _automation.PropertyLibrary.Element.Name;

            sender.RegisterAutomationEvent(
                invEventId,
                TreeScope.Subtree,
                (s, _) => HandleInvoked(s));

            sender.RegisterPropertyChangedEvent(
                TreeScope.Subtree,
                (s, p, v) => HandlePropertyChanged(s, p, v),
                valuePropId);

            sender.RegisterPropertyChangedEvent(
                TreeScope.Subtree,
                (s, p, v) => HandlePropertyChanged(s, p, v),
                namePropId);
        }
        catch { /* new window may vanish before registration completes */ }

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
        catch { /* subscriber errors must not crash the UIA thread */ }
    }

    private static ElementInfo? SnapshotElement(AutomationElement element)
    {
        try
        {
            return new ElementInfo
            {
                Name = element.Name ?? string.Empty,
                AutomationId = element.AutomationId ?? string.Empty,
                ControlType = element.ControlType.ToString(),
                ClassName = element.ClassName ?? string.Empty,
                FrameworkId = element.Properties.FrameworkId.ValueOrDefault ?? string.Empty,
                ProcessId = element.Properties.ProcessId.ValueOrDefault,
                RuntimeId = element.Properties.RuntimeId.ValueOrDefault ?? []
            };
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unregister focus changed (the only one FlaUI exposes an unregister for on AutomationBase)
        try
        {
            if (_focusHandler != null)
                _automation.UnregisterFocusChangedEvent(_focusHandler);
        }
        catch { }

        // FlaUI does not expose per-element unregister in 5.0; UnregisterAllEvents cleans everything
        try { _automation.UnregisterAllEvents(); }
        catch { }
    }
}
