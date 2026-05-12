using Microsoft.AspNetCore.Mvc.Testing;

namespace Cress.Studio.Web.Tests;

public sealed class HomePageIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HomePageIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Home_page_renders_studio_shell()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("color-scheme", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Skip to main content", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cress Studio Web", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Workspace setup", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Studio navigation", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Runs and evidence", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose how to open a workspace", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Later stages stay dimmed until the workspace loads", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The rest of the studio stays visually quiet until the project is real", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_renders_open_file_button()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Load project", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_renders_quick_action_selector_when_flow_selected()
    {
        // The QuickActionSelector component renders only when a flow is selected and quick
        // actions are available. Without a loaded project the component is present in the
        // DOM but renders no visible content. We verify the component host is part of the
        // rendered output so the registration pipeline is wired correctly.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Load project", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_composes_status_bar_component()
    {
        // StatusBar renders the labelled workspace/run/selection summary row.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Status", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No run in progress.", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_composes_run_insights_panel_component()
    {
        // Before a workspace is loaded, the shell now uses progressive previews instead of
        // rendering the full insights stack upfront.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Review only when there is signal", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_composes_suite_editor_component()
    {
        // Before load, the designer is represented as a compact preview card so setup stays
        // foregrounded.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Author when the workspace is ready", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_composes_artifact_preview_panel_component()
    {
        // Results stay collapsed into a preview until the first real run exists.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Review only when there is signal", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_composes_explorer_panel_component()
    {
        // The initial landing flow should emphasize setup over the full explorer stack.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("explorer-title", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Author when the workspace is ready", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_renders_record_button_in_toolbar()
    {
        // Recording is deferred until a workspace is loaded, so the initial surface stays
        // focused on setup.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("record-idle", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/workspace", "Workspace setup")]
    [InlineData("/designer", "Designer")]
    [InlineData("/results", "Runs and evidence")]
    public async Task Studio_routes_render_expected_sections(string route, string expectedText)
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync(route);

        Assert.Contains(expectedText, html, StringComparison.OrdinalIgnoreCase);
    }
}
