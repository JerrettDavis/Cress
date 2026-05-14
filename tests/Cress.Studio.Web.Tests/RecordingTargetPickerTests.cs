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
    public void RecordingTargetPicker_desktop_filter_matches_pid_and_clear_restores_targets()
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
        cut.WaitForAssertion(() => Assert.Contains("notepad", cut.Markup));

        cut.Find("[data-testid='recording-picker-desktop-filter']").Input("4567");

        Assert.DoesNotContain("notepad", cut.Markup);
        Assert.Contains("calc", cut.Markup);
        Assert.Contains("Filter: 4567", cut.Markup);

        cut.Find("[aria-label='Clear process filter']").Click();

        Assert.Contains("notepad", cut.Markup);
        Assert.Contains("calc", cut.Markup);
        Assert.DoesNotContain("Filter: 4567", cut.Markup);
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
    public async Task RecordingTargetPicker_attach_starts_desktop_recording_and_closes_picker()
    {
        var recorder = new FakeStudioRecorderService
        {
            Targets =
            [
                new RecordingTargetInfo { ProcessId = 1234, ProcessName = "notepad", MainWindowTitle = "Notes", IsAttachable = true }
            ]
        };

        var state = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.WaitForAssertion(() => Assert.Contains("notepad", cut.Markup));

        await cut.InvokeAsync(() => cut.Find("tbody button.action-button.success").Click());

        Assert.True(state.IsRecording);
        Assert.False(state.IsRecorderPickerOpen);
        Assert.Equal(1234, recorder.CurrentTarget?.ProcessId);
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
    public void RecordingTargetPicker_shows_empty_filter_message_when_desktop_filters_match_nothing()
    {
        var recorder = new FakeStudioRecorderService
        {
            Targets =
            [
                new RecordingTargetInfo { ProcessId = 1234, ProcessName = "notepad", MainWindowTitle = "Notes", IsAttachable = true }
            ]
        };

        var state = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.WaitForAssertion(() => Assert.Contains("notepad", cut.Markup));

        cut.Find("[data-testid='recording-picker-desktop-filter']").Input("missing");

        Assert.NotNull(cut.Find("[data-testid='recording-picker-desktop-empty-filter']"));
        Assert.Contains("0 shown", cut.Markup);
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

    [Fact]
    public async Task RecordingTargetPicker_companion_session_actions_invoke_client_operations()
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
                        StartedAtUtc = DateTimeOffset.UtcNow
                    },
                    new Cress.Companion.CompanionSessionSnapshot
                    {
                        ProcessId = 4300,
                        ProcessName = "calc",
                        WindowTitle = "Calculator",
                        Status = Cress.Companion.CompanionSessionStatus.Paused,
                        StartedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            }
        };

        var state = CreateState(companionClient: companion);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-companion").Click();

        cut.WaitForAssertion(() => Assert.Contains("Resume", cut.Markup));

        await cut.InvokeAsync(() => cut.FindAll("button.action-button").First(button => button.TextContent.Contains("Pause", StringComparison.Ordinal)).Click());
        await cut.InvokeAsync(() =>
        {
            var resumeRow = cut.FindAll("tbody tr").Single(row => row.TextContent.Contains("Calculator", StringComparison.Ordinal));
            resumeRow.QuerySelector("button.action-button.success")!.Click();
        });
        await cut.InvokeAsync(() => cut.FindAll("button.action-button.danger").First(button => button.TextContent.Contains("Stop", StringComparison.Ordinal)).Click());

        Assert.Equal(4200, companion.LastPausedProcessId);
        Assert.Equal(4300, companion.LastResumedProcessId);
        Assert.Equal(4200, companion.LastStoppedProcessId);
    }

    [Fact]
    public async Task RecordingTargetPicker_companion_start_button_invokes_client_and_closes_picker()
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
                Sessions = []
            }
        };

        var state = CreateState(companionClient: companion);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-companion").Click();
        cut.WaitForAssertion(() => Assert.Contains("Start in companion", cut.Markup));

        await cut.InvokeAsync(() => cut.Find("[data-testid='recording-picker-companion-start']").Click());

        Assert.Equal(4200, companion.LastStartedProcessId);
        Assert.False(state.IsRecorderPickerOpen);
    }

    [Fact]
    public void RecordingTargetPicker_companion_empty_and_disabled_target_render_expected_state()
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
                    IsAttachable = false
                }
            ],
            Snapshot = new Cress.Companion.CompanionServiceSnapshot
            {
                IsAvailable = true,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Sessions = []
            }
        };

        var state = CreateState(companionClient: companion);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-companion").Click();
        cut.WaitForAssertion(() => Assert.Contains("Draft", cut.Markup));

        Assert.NotNull(cut.Find("[data-testid='recording-picker-companion-start']").GetAttribute("disabled"));

        companion.Targets = [];
        companion.Snapshot = companion.Snapshot with { Sessions = [] };
        cut.FindAll("button.action-button").Single(button => button.TextContent.Contains("Refresh", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("No attachable windows were reported", cut.Markup));
    }

    [Fact]
    public void RecordingTargetPicker_web_tab_presets_drive_status_and_enable_start()
    {
        var state = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("[data-testid='recording-picker-tab-web']").Click();

        Assert.Contains("Enter a full http:// or https:// URL", cut.Find("[data-testid='recording-picker-web-status']").TextContent);

        cut.Find("[data-testid='recording-picker-web-preset-localhost']").Click();
        Assert.Equal("http://localhost:5000", cut.Find("[data-testid='recording-picker-web-url']").GetAttribute("value"));
        Assert.Null(cut.Find("[data-testid='recording-picker-web-start']").GetAttribute("disabled"));

        cut.Find("[data-testid='recording-picker-web-preset-example']").Click();
        Assert.Contains("https://example.com", cut.Find("[data-testid='recording-picker-web-status']").TextContent);
    }

    [Fact]
    public async Task RecordingTargetPicker_web_tab_start_uses_selected_browser_and_closes_picker()
    {
        var recorder = new FakeStudioRecorderService();
        var state = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("[data-testid='recording-picker-tab-web']").Click();
        cut.Find("[data-testid='recording-picker-web-url']").Input("https://contoso.test");
        cut.Find("#web-browser").Change("firefox");

        Assert.Contains("Firefox", cut.Find("[data-testid='recording-picker-web-status']").TextContent);

        await cut.InvokeAsync(() => cut.Find("[data-testid='recording-picker-web-start']").Click());

        Assert.Equal("https://contoso.test", recorder.LastWebUrl);
        Assert.Equal("firefox", recorder.LastWebBrowserType);
        Assert.Equal(1, recorder.WebRecordingStartCount);
        Assert.False(state.IsRecorderPickerOpen);
    }

    [Fact]
    public void RecordingTargetPicker_web_tab_invalid_url_shows_validation_message()
    {
        var state = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("[data-testid='recording-picker-tab-web']").Click();
        cut.Find("[data-testid='recording-picker-web-url']").Input("ftp://example.com");

        Assert.Contains("Use a full http:// or https:// URL", cut.Find("[data-testid='recording-picker-web-status']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='recording-picker-web-start']").GetAttribute("disabled"));
    }

    [Fact]
    public void RecordingTargetPicker_web_tab_supports_webkit_status_label()
    {
        var state = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("[data-testid='recording-picker-tab-web']").Click();
        cut.Find("[data-testid='recording-picker-web-url']").Input("https://example.com");
        cut.Find("#web-browser").Change("webkit");

        Assert.Contains("WebKit", cut.Find("[data-testid='recording-picker-web-status']").TextContent);
    }

    [Fact]
    public void RecordingTargetPicker_cancel_resets_tab_and_filters()
    {
        var recorder = new FakeStudioRecorderService
        {
            Targets =
            [
                new RecordingTargetInfo { ProcessId = 1234, ProcessName = "notepad", MainWindowTitle = "Notes", IsAttachable = true }
            ]
        };

        var state = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.WaitForAssertion(() => Assert.Contains("notepad", cut.Markup));
        cut.Find("[data-testid='recording-picker-tab-web']").Click();
        cut.Find("[data-testid='recording-picker-web-preset-localhost']").Click();
        cut.Find("[data-testid='recording-picker-cancel']").Click();

        Assert.False(state.IsRecorderPickerOpen);

        state.OpenRecorderPicker();
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='recording-picker-panel-desktop']"));
            Assert.Contains("notepad", cut.Markup);
            Assert.DoesNotContain("http://localhost:5000", cut.Markup);
        });
    }
}
