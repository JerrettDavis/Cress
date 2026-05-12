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
    private StudioWorkspaceState CreateState(IStudioRecorderService? recorderService = null, FakeStudioCompanionClient? companionClient = null)
    {
        Services.AddCressStudioBackend();
        var fake = recorderService ?? new FakeStudioRecorderService();
        Services.AddSingleton<IStudioRecorderService>(fake);
        Services.AddSingleton<IStudioCompanionClient>(companionClient ?? new FakeStudioCompanionClient());
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
    public void RecordingTargetPicker_desktop_filter_reduces_visible_targets()
    {
        var recorder = new FakeStudioRecorderService
        {
            Targets =
            [
                new RecordingTargetInfo { ProcessId = 1234, ProcessName = "notepad", MainWindowTitle = "Notes", IsAttachable = true },
                new RecordingTargetInfo { ProcessId = 4567, ProcessName = "calc", MainWindowTitle = "Calculator", IsAttachable = true }
            ]
        };

        var state = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("notepad", cut.Markup);
            Assert.Contains("calc", cut.Markup);
        });

        cut.Find("[data-testid='recording-picker-desktop-filter']").Input("calc");

        Assert.DoesNotContain("notepad", cut.Markup);
        Assert.Contains("calc", cut.Markup);
        Assert.Contains("1 shown", cut.Markup);
    }

    [Fact]
    public void RecordingTargetPicker_attachable_only_toggle_hides_unattachable_targets()
    {
        var recorder = new FakeStudioRecorderService
        {
            Targets =
            [
                new RecordingTargetInfo { ProcessId = 1234, ProcessName = "notepad", MainWindowTitle = "Notes", IsAttachable = true },
                new RecordingTargetInfo { ProcessId = 4567, ProcessName = "admin-app", MainWindowTitle = "Elevated", IsAttachable = false }
            ]
        };

        var state = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        cut.WaitForAssertion(() => Assert.Contains("admin-app", cut.Markup));

        cut.Find("[data-testid='recording-picker-desktop-attachable-only']").Change(true);

        Assert.Contains("notepad", cut.Markup);
        Assert.DoesNotContain("admin-app", cut.Markup);
        Assert.Contains("Attachable-only filter on", cut.Markup);
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

    [Fact]
    public void RecordingTargetPicker_companion_tab_shows_targets_and_sessions()
    {
        var companion = new FakeStudioCompanionClient
        {
            Targets =
            [
                new Cress.Companion.CompanionTargetInfo
                {
                    ProcessId = 4200,
                    ProcessName = "wordpad",
                    WindowTitle = "Draft",
                    IsAttachable = true
                }
            ],
            Snapshot = new Cress.Companion.CompanionServiceSnapshot
            {
                IsAvailable = true,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Sessions =
                [
                    new Cress.Companion.CompanionSessionSnapshot
                    {
                        ProcessId = 4200,
                        ProcessName = "wordpad",
                        WindowTitle = "Draft",
                        Status = Cress.Companion.CompanionSessionStatus.Recording,
                        StartedAtUtc = DateTimeOffset.UtcNow,
                        CapturedEventCount = 8,
                        LastStepSummary = "Click(automationId=saveButton)"
                    }
                ]
            }
        };

        var state = CreateState(companionClient: companion);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-companion").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Desktop companion is tracking 1 app", cut.Markup);
            Assert.Contains("Draft", cut.Markup);
            Assert.Contains("Click(automationId=saveButton)", cut.Markup);
            Assert.Contains("Start in companion", cut.Markup);
        });
    }
}
