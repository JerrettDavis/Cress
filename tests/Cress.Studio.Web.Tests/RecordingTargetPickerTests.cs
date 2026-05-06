using System.Reflection;
using Bunit;
using Cress.Recorder;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class RecordingTargetPickerTests : TestContext
{
    private StudioWorkspaceState CreateState(IStudioRecorderService? recorderService = null)
    {
        Services.AddCressStudioBackend();
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
    public void RecordingTargetPicker_does_not_render_when_picker_is_closed()
    {
        var state = CreateState();
        Assert.False(state.IsRecorderPickerOpen);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        // Modal should not render its content when closed.
        Assert.DoesNotContain("modal-dialog", cut.Markup);
        Assert.DoesNotContain("Choose recording target", cut.Markup);
    }

    [Fact]
    public void RecordingTargetPicker_renders_modal_when_picker_is_open()
    {
        var recorder = new FakeStudioRecorderService();
        var state = CreateState(recorder);

        // Open the picker — this sets IsRecorderPickerOpen = true.
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        Assert.Contains("Choose recording target", cut.Markup);
    }

    [Fact]
    public void RecordingTargetPicker_renders_target_row_when_service_returns_one()
    {
        var target = new RecordingTargetInfo
        {
            ProcessId = 9876,
            ProcessName = "notepad",
            MainWindowTitle = "Untitled - Notepad",
            IsAttachable = true
        };

        var recorder = new FakeStudioRecorderService
        {
            Targets = [target]
        };

        var state = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        // Wait for async target loading.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("notepad", cut.Markup);
            Assert.Contains("Untitled - Notepad", cut.Markup);
            Assert.Contains("9876", cut.Markup);
        });
    }

    [Fact]
    public void RecordingTargetPicker_shows_empty_message_when_no_targets_found()
    {
        var recorder = new FakeStudioRecorderService { Targets = [] };
        var state = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No windows with visible titles", cut.Markup);
        });
    }
}
