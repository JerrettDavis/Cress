using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace Cress.Recorder.Tests;

public sealed class RecordingSessionTests
{
    [Fact]
    public void FromProcessId_EventsAndStopReturnQueuedSnapshots()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);
        var first = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero),
            Kind = EventKind.Invoke,
            Element = new ElementInfo { ControlType = "button", AutomationId = "save-button" }
        };
        var second = new RecordedEvent
        {
            Sequence = 2,
            Timestamp = new DateTimeOffset(2026, 5, 8, 12, 0, 1, TimeSpan.Zero),
            Kind = EventKind.Navigate,
            Element = new ElementInfo { ControlType = "document", Name = "Dashboard" },
            Url = "https://example.test/dashboard"
        };

        EnqueueEvent(session, first);
        EnqueueEvent(session, second);

        var liveSnapshot = session.Events;
        var stoppedSnapshot = session.Stop();

        Assert.Equal(2, liveSnapshot.Count);
        Assert.Equal(2, stoppedSnapshot.Count);
        Assert.Equal(first, stoppedSnapshot[0]);
        Assert.Equal(second, stoppedSnapshot[1]);
    }

    [Fact]
    public void FromProcessName_AttachesToCurrentProcess()
    {
        using var currentProcess = Process.GetCurrentProcess();

        var session = RecordingSession.FromProcessName(currentProcess.ProcessName);

        Assert.NotNull(session);
        Assert.Empty(session.Events);
    }

    [Fact]
    public void FromProcessName_ThrowsWhenProcessIsMissing()
    {
        var missingProcessName = $"missing-{Guid.NewGuid():N}";

        var exception = Assert.Throws<InvalidOperationException>(() => RecordingSession.FromProcessName(missingProcessName));

        Assert.Contains(missingProcessName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_ThrowsWhenSessionHasBeenDisposed()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Start());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);

        session.Dispose();
        session.Dispose();

        Assert.Empty(session.Events);
    }

    [Fact]
    public void ElementInfo_ToString_PrefersAutomationIdThenTestIdThenName()
    {
        Assert.Equal(
            "button[save-button]",
            new ElementInfo { ControlType = "button", AutomationId = "save-button", Name = "Save" }.ToString());
        Assert.Equal(
            "[data-testid=save-button]",
            new ElementInfo { ControlType = "button", TestId = "save-button", Name = "Save" }.ToString());
        Assert.Equal(
            "button[name='Save']",
            new ElementInfo { ControlType = "button", Name = "Save" }.ToString());
        Assert.Equal(
            "button",
            new ElementInfo { ControlType = "button" }.ToString());
    }

    [Fact]
    public void RecordedEvent_ToString_FormatsTimestampKindAndElement()
    {
        var evt = new RecordedEvent
        {
            Timestamp = new DateTimeOffset(2026, 5, 8, 12, 30, 45, 123, TimeSpan.Zero),
            Kind = EventKind.ValueChanged,
            Element = new ElementInfo { ControlType = "textbox", AutomationId = "search-box" }
        };

        Assert.Equal("[12:30:45.123] ValueChanged: textbox[search-box]", evt.ToString());
    }

    private static void EnqueueEvent(RecordingSession session, RecordedEvent recordedEvent)
    {
        var queueField = typeof(RecordingSession).GetField("_events", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queueField);

        var queue = Assert.IsType<ConcurrentQueue<RecordedEvent>>(queueField.GetValue(session));
        queue.Enqueue(recordedEvent);
    }
}
