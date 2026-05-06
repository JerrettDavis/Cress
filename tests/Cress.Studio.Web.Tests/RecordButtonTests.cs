using System.Reflection;
using Bunit;
using Cress.Recorder;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class RecordButtonTests : TestContext
{
    private StudioWorkspaceState CreateState(IStudioRecorderService? recorderService = null)
    {
        Services.AddCressStudioBackend();

        // Override the real recorder with a test double that never touches the OS.
        // Registering the instance last wins in ASP.NET Core DI (last registration is used).
        var fake = recorderService ?? new FakeStudioRecorderService();
        Services.AddSingleton<IStudioRecorderService>(fake);
        Services.AddSingleton<StudioWorkspaceState>();
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
    }

    [Fact]
    public void RecordButton_renders_record_label_when_not_recording()
    {
        CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordButton>();

        Assert.Contains("Record", cut.Markup);
        Assert.DoesNotContain("Stop recording", cut.Markup);
    }

    [Fact]
    public void RecordButton_renders_stop_label_when_recording_is_active()
    {
        var recorder = new FakeStudioRecorderService { IsRecording = true };
        CreateState(recorder);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordButton>();

        Assert.Contains("Stop recording", cut.Markup);
        Assert.DoesNotContain("class=\"action-button record-idle\"", cut.Markup);
    }

    [Fact]
    public void RecordButton_shows_event_count_when_recording_active()
    {
        var recorder = new FakeStudioRecorderService
        {
            IsRecording = true,
            CapturedEventCount = 7,
            CurrentTarget = new RecordingTargetInfo
            {
                ProcessId = 1234,
                ProcessName = "calc",
                MainWindowTitle = "Calculator"
            }
        };

        CreateState(recorder);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordButton>();

        Assert.Contains("7", cut.Markup);
        Assert.Contains("events", cut.Markup);
    }

    [Fact]
    public void RecordButton_clicking_idle_button_opens_picker()
    {
        var state = CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordButton>();

        var button = cut.Find("button.record-idle");
        button.Click();

        Assert.True(state.IsRecorderPickerOpen);
    }

    [Fact]
    public void RecordButton_shows_error_message_when_RecordingError_is_set()
    {
        var state = CreateState();
        // Set the error directly via reflection — same pattern used elsewhere in the test suite.
        SetPrivate(state, "RecordingError", "Access denied — the target process may be elevated.");
        state.GetType()
            .GetMethod("NotifyChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(state, null);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordButton>();

        Assert.Contains("Access denied", cut.Markup);
        Assert.Contains("record-error", cut.Markup);
    }
}
