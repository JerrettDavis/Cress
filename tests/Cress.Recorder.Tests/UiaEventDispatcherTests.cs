using System.Collections.Concurrent;
using System.Reflection;
using System.Windows.Automation;

namespace Cress.Recorder.Tests;

public sealed class UiaEventDispatcherTests
{
    [Fact]
    public void SnapshotElement_returns_current_root_metadata()
    {
        var root = GetRootElement();

        var info = InvokeStatic<ElementInfo?>("SnapshotElement", root);

        Assert.NotNull(info);
        Assert.True(info.ProcessId > 0);
        Assert.NotEmpty(info.ControlType);
        Assert.NotEmpty(info.RuntimeId);
    }

    [Fact]
    public void Handlers_emit_expected_events_for_matching_process()
    {
        var root = GetRootElement();
        ConcurrentQueue<RecordedEvent> queue = new();
        List<RecordedEvent> observed = [];
        using var dispatcher = new UiaEventDispatcher(root, root.Current.ProcessId, queue, observed.Add);

        InvokeInstance(
            dispatcher,
            "HandleInvoked",
            root,
            new AutomationEventArgs(InvokePatternIdentifiers.InvokedEvent));
        InvokeInstance(
            dispatcher,
            "HandlePropertyChanged",
            root,
            new AutomationPropertyChangedEventArgs(ValuePatternIdentifiers.ValueProperty, "old", "new"));
        InvokeInstance(
            dispatcher,
            "HandlePropertyChanged",
            root,
            new AutomationPropertyChangedEventArgs(ValuePatternIdentifiers.ValueProperty, "old", null));
        InvokeInstance(
            dispatcher,
            "HandleFocusChanged",
            root,
            new AutomationFocusChangedEventArgs(0, 0));
        InvokeInstance(
            dispatcher,
            "HandleWindowOpened",
            root,
            new AutomationEventArgs(WindowPattern.WindowOpenedEvent));

        var events = queue.ToArray();
        Assert.Equal(5, events.Length);
        Assert.Equal([EventKind.Invoke, EventKind.ValueChanged, EventKind.ValueChanged, EventKind.FocusChanged, EventKind.WindowOpened], events.Select(evt => evt.Kind).ToArray());
        Assert.Equal([1, 2, 3, 4, 5], events.Select(evt => evt.Sequence).ToArray());
        Assert.Equal("new", events[1].Value);
        Assert.Equal(events[2].Element.Name, events[2].Value);
        Assert.Equal(5, observed.Count);
    }

    [Fact]
    public void Emit_enqueues_even_when_callback_throws()
    {
        var root = GetRootElement();
        ConcurrentQueue<RecordedEvent> queue = new();
        using var dispatcher = new UiaEventDispatcher(root, root.Current.ProcessId, queue, _ => throw new InvalidOperationException("boom"));
        var evt = new RecordedEvent
        {
            Sequence = 7,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Invoke,
            Element = new ElementInfo { ControlType = "button", AutomationId = "save" }
        };

        InvokeInstance(dispatcher, "Emit", evt);

        var recorded = Assert.Single(queue);
        Assert.Equal(evt, recorded);
    }

    [Fact]
    public void Handlers_ignore_events_after_dispose()
    {
        var root = GetRootElement();
        ConcurrentQueue<RecordedEvent> queue = new();
        using var dispatcher = new UiaEventDispatcher(root, root.Current.ProcessId, queue, _ => { });
        dispatcher.Dispose();

        InvokeInstance(
            dispatcher,
            "HandleInvoked",
            root,
            new AutomationEventArgs(InvokePatternIdentifiers.InvokedEvent));
        InvokeInstance(
            dispatcher,
            "HandlePropertyChanged",
            root,
            new AutomationPropertyChangedEventArgs(ValuePatternIdentifiers.ValueProperty, "old", "new"));
        InvokeInstance(
            dispatcher,
            "HandleFocusChanged",
            root,
            new AutomationFocusChangedEventArgs(0, 0));
        InvokeInstance(
            dispatcher,
            "HandleWindowOpened",
            root,
            new AutomationEventArgs(WindowPattern.WindowOpenedEvent));

        Assert.Empty(queue);
    }

    [Fact]
    public void RegisterAll_and_dispose_are_safe_to_call()
    {
        var root = GetRootElement();
        using var dispatcher = new UiaEventDispatcher(root, root.Current.ProcessId, new ConcurrentQueue<RecordedEvent>(), _ => { });

        dispatcher.RegisterAll();
        dispatcher.Dispose();
        dispatcher.Dispose();
    }

    [Fact]
    public void Handlers_ignore_events_for_other_processes()
    {
        var root = GetRootElement();
        ConcurrentQueue<RecordedEvent> queue = new();
        using var dispatcher = new UiaEventDispatcher(root, root.Current.ProcessId + 1, queue, _ => { });

        InvokeInstance(
            dispatcher,
            "HandleInvoked",
            root,
            new AutomationEventArgs(InvokePatternIdentifiers.InvokedEvent));
        InvokeInstance(
            dispatcher,
            "HandlePropertyChanged",
            root,
            new AutomationPropertyChangedEventArgs(ValuePatternIdentifiers.ValueProperty, "old", "new"));
        InvokeInstance(
            dispatcher,
            "HandleFocusChanged",
            root,
            new AutomationFocusChangedEventArgs(0, 0));
        InvokeInstance(
            dispatcher,
            "HandleWindowOpened",
            root,
            new AutomationEventArgs(WindowPattern.WindowOpenedEvent));

        Assert.Empty(queue);
    }

    private static T InvokeStatic<T>(string methodName, params object?[] arguments)
    {
        var method = typeof(UiaEventDispatcher).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find static method '{methodName}'.");
        return (T)method.Invoke(null, arguments)!;
    }

    private static void InvokeInstance(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find instance method '{methodName}'.");
        method.Invoke(instance, arguments);
    }

    private static AutomationElement GetRootElement()
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return AutomationElement.RootElement;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100);
            }
        }
    }
}
