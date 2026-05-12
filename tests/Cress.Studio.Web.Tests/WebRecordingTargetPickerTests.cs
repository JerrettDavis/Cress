using Bunit;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

/// <summary>
/// bUnit tests for the Web tab added to <see cref="RecordingTargetPicker"/>.
/// </summary>
public sealed class WebRecordingTargetPickerTests : TestContext
{
    private (StudioWorkspaceState state, FakeStudioRecorderService recorder) CreateState(
        FakeStudioRecorderService? recorder = null)
    {
        Services.AddCressStudioBackend();
        var fake = recorder ?? new FakeStudioRecorderService();
        Services.AddSingleton<IStudioRecorderService>(fake);
        Services.AddSingleton<IStudioCompanionClient>(new FakeStudioCompanionClient());
        Services.AddSingleton<StudioWorkspaceState>();
        var state = Services.GetRequiredService<StudioWorkspaceState>();
        return (state, fake);
    }

    [Fact]
    public void Picker_shows_Desktop_and_Web_tabs_when_open()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        Assert.Contains("picker-tab", cut.Markup);
        Assert.Contains("Desktop", cut.Markup);
        Assert.Contains("Web", cut.Markup);
    }

    [Fact]
    public void Picker_defaults_to_Desktop_tab()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        // Desktop tab should be active (has picker-tab--active class).
        var desktopTab = cut.Find("#picker-tab-desktop");
        Assert.Contains("picker-tab--active", desktopTab.GetAttribute("class") ?? string.Empty);

        var webTab = cut.Find("#picker-tab-web");
        Assert.DoesNotContain("picker-tab--active", webTab.GetAttribute("class") ?? string.Empty);
    }

    [Fact]
    public void Switching_to_Web_tab_shows_URL_input_and_browser_dropdown()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        // Click the Web tab.
        cut.Find("#picker-tab-web").Click();

        // URL input and browser select should now be visible.
        Assert.NotNull(cut.Find("#web-url"));
        Assert.NotNull(cut.Find("#web-browser"));
    }

    [Fact]
    public void Start_recording_button_is_disabled_when_URL_is_empty()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-web").Click();

        // URL input is empty by default — Start recording should be disabled.
        var startBtn = cut.Find("button[title*='Enter a valid URL']");
        Assert.NotNull(startBtn);
        Assert.True(startBtn.HasAttribute("disabled"), "Start button should be disabled when URL is empty");
    }

    [Fact]
    public void Start_recording_button_is_disabled_for_invalid_URL()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-web").Click();

        // Enter a non-URL value.
        cut.Find("#web-url").Input("not-a-url");

        var startBtn = cut.Find("button[title*='Enter a valid URL']");
        Assert.True(startBtn.HasAttribute("disabled"), "Start button should be disabled for an invalid URL");
    }

    [Fact]
    public void Web_tab_shows_visible_validation_message_for_invalid_URL()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-web").Click();
        cut.Find("#web-url").Input("not-a-url");

        Assert.Contains("Use a full http:// or https:// URL", cut.Find("[data-testid='recording-picker-web-status']").TextContent);
    }

    [Fact]
    public void Start_recording_button_is_enabled_for_valid_https_URL()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-web").Click();

        cut.Find("#web-url").Input("https://example.com");

        // The button should no longer carry the disabled attribute.
        var startBtn = cut.Find("button[title*='Start recording in browser']");
        Assert.False(startBtn.HasAttribute("disabled"), "Start button should be enabled for a valid URL");
    }

    [Fact]
    public void Clicking_a_web_preset_populates_url_and_enables_start()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-web").Click();
        cut.Find("[data-testid='recording-picker-web-preset-example']").Click();

        Assert.Equal("https://example.com", cut.Find("#web-url").GetAttribute("value"));
        Assert.Contains("Recording will open https://example.com in Chromium.", cut.Find("[data-testid='recording-picker-web-status']").TextContent);
        Assert.False(cut.Find("button[title*='Start recording in browser']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Clicking_Start_recording_calls_BeginWebRecordingAsync_with_correct_args()
    {
        var recorder = new FakeStudioRecorderService();
        var (state, _) = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-web").Click();

        cut.Find("#web-url").Input("https://example.com");

        // Select Firefox in the browser dropdown.
        cut.Find("#web-browser").Change("firefox");

        var startBtn = cut.Find("button[title*='Start recording in browser']");
        await startBtn.ClickAsync(new());

        Assert.Equal("https://example.com", recorder.LastWebUrl);
        Assert.Equal("firefox", recorder.LastWebBrowserType);
        Assert.Equal(1, recorder.WebRecordingStartCount);
    }

    [Fact]
    public async Task Clicking_Start_recording_closes_picker_and_starts_recording()
    {
        var recorder = new FakeStudioRecorderService();
        var (state, _) = CreateState(recorder);
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-web").Click();
        cut.Find("#web-url").Input("https://example.com");

        var startBtn = cut.Find("button[title*='Start recording in browser']");
        await startBtn.ClickAsync(new());

        // After starting, the picker should be closed and recording active.
        Assert.False(state.IsRecorderPickerOpen);
        Assert.True(state.IsRecording);
    }

    [Fact]
    public void Helper_text_is_shown_on_Web_tab()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();
        cut.Find("#picker-tab-web").Click();

        Assert.Contains("A browser window will open", cut.Markup);
    }

    [Fact]
    public void Refresh_button_is_only_visible_on_Desktop_tab()
    {
        var (state, _) = CreateState();
        state.OpenRecorderPicker();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingTargetPicker>();

        // On Desktop tab — Refresh should appear.
        Assert.Contains("Refresh", cut.Markup);

        // Switch to Web tab.
        cut.Find("#picker-tab-web").Click();

        // Refresh should disappear on the Web tab.
        Assert.DoesNotContain("Refresh", cut.Markup);
    }
}
