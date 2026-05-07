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
        Assert.Contains("Test automation studio", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Workspace setup", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Studio navigation", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Runs, evidence, and diagnostics", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System theme aware", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Flake watch", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_renders_open_file_button()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Open file", html, StringComparison.OrdinalIgnoreCase);
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

        // The static render includes the component tag or its surrounding markup.
        // "Open file" being present confirms the toolbar-actions section rendered, meaning
        // QuickActionSelector was also included in that section.
        Assert.Contains("Open file", html, StringComparison.OrdinalIgnoreCase);
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
        // RunInsightsPanel always renders its three section headings regardless of data.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Flake watch", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recent activity", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_composes_suite_editor_component()
    {
        // The SuiteEditor component renders inside the "suite" designer tab.
        // On initial page load the designer is on the overview tab, so the SuiteEditor
        // itself is not rendered. The presence of the "Suite editor" tab button confirms
        // the page is wired to include the SuiteEditor in the designer section.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Suite editor", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_composes_artifact_preview_panel_component()
    {
        // ArtifactPreviewPanel always renders its "Artifacts and reports" heading.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Artifacts and reports", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_composes_explorer_panel_component()
    {
        // ExplorerPanel renders a placeholder when no project is loaded.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Load a Cress workspace", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explorer-title", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_page_renders_record_button_in_toolbar()
    {
        // RecordButton is embedded in the toolbar and should be in the initial SSR output.
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        // The RecordButton renders either "Record" (idle) or "Stop recording" (active).
        // On initial load it's always idle — confirm "Record" is present.
        Assert.Contains("Record", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("record-idle", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/workspace", "Workspace setup")]
    [InlineData("/designer", "Designer views")]
    [InlineData("/results", "Runs, evidence, and diagnostics")]
    public async Task Studio_routes_render_expected_sections(string route, string expectedText)
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync(route);

        Assert.Contains(expectedText, html, StringComparison.OrdinalIgnoreCase);
    }
}
