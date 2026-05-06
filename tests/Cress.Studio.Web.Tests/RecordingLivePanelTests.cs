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
}
