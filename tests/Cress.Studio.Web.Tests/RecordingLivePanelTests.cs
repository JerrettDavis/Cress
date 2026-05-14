using Bunit;
using Cress.Recorder;
using Cress.Recorder.Inference;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class RecordingLivePanelTests : TestContext
{
    private (StudioWorkspaceState state, FakeStudioRecorderService recorder) CreateState(
        FakeStudioRecorderService? recorder = null)
    {
        Services.AddCressStudioBackend();
        var fake = recorder ?? new FakeStudioRecorderService();
        Services.AddSingleton<IStudioRecorderService>(fake);
        Services.AddSingleton<StudioWorkspaceState>();
        var state = Services.GetRequiredService<StudioWorkspaceState>();
        return (state, fake);
    }

    [Fact]
    public void RecordingLivePanel_renders_nothing_when_not_recording()
    {
        CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingLivePanel>();

        // The drawer should have no --open modifier class, so no recording content is visible.
        Assert.DoesNotContain("RECORDING", cut.Markup);
        Assert.DoesNotContain("recording-drawer--open", cut.Markup);
    }

    [Fact]
    public void RecordingLivePanel_renders_recording_header_when_active()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            CurrentTarget = new RecordingTargetInfo
            {
                ProcessId = 99,
                ProcessName = "calc",
                MainWindowTitle = "Calculator"
            }
        };
        CreateState(recorder);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingLivePanel>();

        Assert.Contains("recording-drawer--open", cut.Markup);
        Assert.Contains("RECORDING", cut.Markup);
        Assert.Contains("Calculator", cut.Markup);
    }

    [Fact]
    public void RecordingLivePanel_renders_events_from_state()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            CurrentTarget = new RecordingTargetInfo { ProcessId = 1, ProcessName = "app", MainWindowTitle = "App" }
        };
        var now = DateTimeOffset.UtcNow;
        recorder.SimulateEvent(new RecordedEvent
        {
            Sequence = 1, Timestamp = now,
            Kind = EventKind.Invoke,
            Element = new ElementInfo { AutomationId = "btn1", ControlType = "Button" }
        });
        recorder.SimulateEvent(new RecordedEvent
        {
            Sequence = 2, Timestamp = now.AddMilliseconds(100),
            Kind = EventKind.ValueChanged,
            Element = new ElementInfo { AutomationId = "txt1", ControlType = "Edit" },
            Value = "hello"
        });
        recorder.SimulateEvent(new RecordedEvent
        {
            Sequence = 3, Timestamp = now.AddMilliseconds(200),
            Kind = EventKind.KeyDown,
            Element = new ElementInfo { ControlType = "Window" },
            Key = "Enter"
        });
        CreateState(recorder);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingLivePanel>();

        Assert.Contains("btn1", cut.Markup);
        Assert.Contains("txt1", cut.Markup);
        // Three events means the event rows section is non-empty.
        Assert.Contains("recording-event-row", cut.Markup);
        var rowCount = cut.FindAll(".recording-event-row").Count;
        Assert.Equal(3, rowCount);
    }

    [Fact]
    public async Task RecordingLivePanel_stop_button_calls_EndRecordingAsync()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            CurrentTarget = new RecordingTargetInfo { ProcessId = 42, ProcessName = "app", MainWindowTitle = "App" }
        };
        var (state, _) = CreateState(recorder);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingLivePanel>();

        var stopButton = cut.Find(".recording-stop-btn");
        await stopButton.ClickAsync(new());

        // After clicking Stop, IsRecording should be false (the fake flips it).
        Assert.False(state.IsRecording);
        // Save panel should open because EndRecordingAsync sets IsRecorderSavePanelOpen = true.
        Assert.True(state.IsRecorderSavePanelOpen);
    }

    [Fact]
    public void RecordingLivePanel_shows_empty_hint_when_no_events()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            CurrentTarget = new RecordingTargetInfo { ProcessId = 5, ProcessName = "x", MainWindowTitle = "X" }
        };
        CreateState(recorder);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingLivePanel>();

        Assert.Contains("Interact with the target application", cut.Markup);
    }

    [Fact]
    public void RecordingLivePanel_formats_event_labels_icons_and_elapsed_values()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            CurrentTarget = new RecordingTargetInfo { ProcessId = 5, ProcessName = "x", MainWindowTitle = "X" },
            Elapsed = TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(34))
        };
        var now = DateTimeOffset.UtcNow;
        recorder.SimulateEvent(new RecordedEvent
        {
            Sequence = 1,
            Timestamp = now,
            Kind = EventKind.FocusChanged,
            Element = new ElementInfo { Name = "Search", ControlType = "Edit" }
        });
        recorder.SimulateEvent(new RecordedEvent
        {
            Sequence = 2,
            Timestamp = now.AddMilliseconds(250),
            Kind = EventKind.WindowOpened,
            Element = new ElementInfo { ControlType = "Window" }
        });
        recorder.SimulateEvent(new RecordedEvent
        {
            Sequence = 3,
            Timestamp = now.AddMilliseconds(500),
            Kind = (EventKind)999,
            Element = new ElementInfo()
        });
        CreateState(recorder);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingLivePanel>();

        Assert.Contains("12:34", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("3 events", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Edit \"Search\"", cut.FindAll(".recording-event-label")[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Window", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("●", cut.FindAll(".recording-event-icon")[2].TextContent);
        Assert.Contains("event-kind--focus", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("event-kind--window", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("00:00.250", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingLivePanel_shows_placeholder_target_name_when_missing()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            CurrentTarget = null
        };
        CreateState(recorder);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingLivePanel>();

        Assert.Contains("…", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingLivePanel_limits_rendered_events_to_latest_hundred_and_requests_scroll()
    {
        JSInterop.SetupVoid("cressRecording.scrollToBottom", _ => true);

        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            CurrentTarget = new RecordingTargetInfo { ProcessId = 5, ProcessName = "x", MainWindowTitle = "X" }
        };
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 105; i++)
        {
            recorder.SimulateEvent(new RecordedEvent
            {
                Sequence = i + 1,
                Timestamp = now.AddMilliseconds(i),
                Kind = EventKind.Invoke,
                Element = new ElementInfo { AutomationId = $"btn-{i}", ControlType = "Button" }
            });
        }

        CreateState(recorder);
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingLivePanel>();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(100, cut.FindAll(".recording-event-row").Count);
            Assert.DoesNotContain("btn-0", cut.Markup);
            Assert.Contains("btn-104", cut.Markup);
        });

        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "cressRecording.scrollToBottom");
    }
}
