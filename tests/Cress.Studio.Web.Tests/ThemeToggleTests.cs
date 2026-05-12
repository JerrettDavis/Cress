using Bunit;
using Microsoft.JSInterop;

namespace Cress.Studio.Web.Tests;

public sealed class ThemeToggleTests : TestContext
{
    public ThemeToggleTests()
    {
        // getTheme is called in OnAfterRenderAsync; return "system" as the default stored value.
        JSInterop.Setup<string>("getTheme").SetResult("system");
        JSInterop.Setup<string>("getEffectiveTheme").SetResult("dark");
        // setTheme is called on button clicks.
        JSInterop.SetupVoid("setTheme", _ => true);
    }

    [Fact]
    public void ThemeToggle_renders_three_buttons_with_correct_aria_labels()
    {
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ThemeToggle>();

        var buttons = cut.FindAll("button");

        Assert.Equal(3, buttons.Count);
        Assert.Contains(buttons, b => b.GetAttribute("aria-label") == "System theme");
        Assert.Contains(buttons, b => b.GetAttribute("aria-label") == "Light theme");
        Assert.Contains(buttons, b => b.GetAttribute("aria-label") == "Dark theme");
    }

    [Fact]
    public void ThemeToggle_system_button_is_active_by_default()
    {
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ThemeToggle>();

        var activeButtons = cut.FindAll(".tab-button.active");

        Assert.Single(activeButtons);
        Assert.Equal("System theme", activeButtons[0].GetAttribute("aria-label"));
        Assert.Contains("Theme: System", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
        Assert.Contains("Following OS - Dark now.", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
    }

    [Fact]
    public void ThemeToggle_switches_active_button_on_click()
    {
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ThemeToggle>();

        var lightButton = cut.Find("[aria-label='Light theme']");
        lightButton.Click();

        var activeButtons = cut.FindAll(".tab-button.active");
        Assert.Single(activeButtons);
        Assert.Equal("Light theme", activeButtons[0].GetAttribute("aria-label"));
        Assert.Contains("Theme: Light", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
        Assert.Contains("Pinned to light mode.", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
    }

    [Fact]
    public void ThemeToggle_dark_button_updates_summary()
    {
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ThemeToggle>();

        cut.Find("[aria-label='Dark theme']").Click();

        var activeButtons = cut.FindAll(".tab-button.active");
        Assert.Single(activeButtons);
        Assert.Equal("Dark theme", activeButtons[0].GetAttribute("aria-label"));
        Assert.Contains("Theme: Dark", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
        Assert.Contains("Pinned to dark mode.", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
    }

    [Fact]
    public void ThemeToggle_keeps_defaults_when_initial_js_lookup_is_unavailable()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.Setup<string>("getTheme").SetException(new JSException("Unavailable"));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ThemeToggle>();

        var activeButtons = cut.FindAll(".tab-button.active");
        Assert.Single(activeButtons);
        Assert.Equal("System theme", activeButtons[0].GetAttribute("aria-label"));
        Assert.Contains("Theme: System", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
        Assert.Contains("Following OS - Dark now.", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
    }

    [Fact]
    public void ThemeToggle_updates_selection_even_when_set_theme_js_call_fails()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.Setup<string>("getTheme").SetResult("system");
        JSInterop.Setup<string>("getEffectiveTheme").SetResult("dark");
        JSInterop.SetupVoid("setTheme", _ => true).SetException(new JSException("Unavailable"));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ThemeToggle>();

        cut.Find("[aria-label='Light theme']").Click();

        var activeButtons = cut.FindAll(".tab-button.active");
        Assert.Single(activeButtons);
        Assert.Equal("Light theme", activeButtons[0].GetAttribute("aria-label"));
        Assert.Contains("Theme: Light", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
        Assert.Contains("Pinned to light mode.", cut.Find("[data-testid='theme-toggle-summary']").TextContent);
    }
}
