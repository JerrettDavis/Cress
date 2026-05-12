using Bunit;
using Cress.Execution;
using Cress.Studio.Services;
using Cress.Studio;
using Cress.Studio.Web.Components.Pages;
using Cress.Studio.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class HomeInteractionTests : TestContext, IDisposable
{
    private readonly List<string> _temporaryRoots = [];

    [Theory]
    [InlineData("workspace", "workspace-section", "workspace")]
    [InlineData("designer", "designer-section", "designer")]
    public void Home_highlights_section_from_route(string route, string focusedTestId, string expectedScrollTarget)
    {
        CreateState();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"http://localhost/{route}");

        var cut = RenderComponent<Home>();

        Assert.Contains("panel-focus", cut.Find($"[data-testid='{focusedTestId}']").GetAttribute("class"));
        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "cressStudio.scrollSectionIntoView"
            && string.Equals(invocation.Arguments.SingleOrDefault()?.ToString(), expectedScrollTarget, StringComparison.Ordinal));
    }

    [Fact]
    public void Home_root_route_keeps_landing_view_in_place()
    {
        CreateState();
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/");

        var cut = RenderComponent<Home>();

        Assert.NotNull(cut.Find("[data-testid='onboarding-panel']"));
        Assert.NotNull(cut.Find("[data-testid='startup-wizard-nav']"));
        Assert.NotNull(cut.Find("[data-testid='workflow-progress']"));
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "cressStudio.scrollSectionIntoView");
    }

    [Fact]
    public void Home_before_loading_project_shows_progressive_previews_instead_of_full_authoring_stack()
    {
        CreateState();

        var cut = RenderComponent<Home>();

        Assert.NotNull(cut.Find("[data-testid='designer-section']"));
        Assert.NotNull(cut.Find("[data-testid='results-panel']"));
        Assert.NotNull(cut.Find("[data-testid='workspace-next-step-callout']"));
        Assert.Empty(cut.FindAll("[data-testid='designer-tab-overview']"));
        Assert.Empty(cut.FindAll("[data-testid='more-actions']"));
        Assert.Empty(cut.FindAll("[data-testid='open-selected-file']"));
    }

    [Fact]
    public void Home_shows_busy_banner_when_workspace_is_busy()
    {
        var state = CreateState();
        SetPrivate(state, nameof(StudioWorkspaceState.IsBusy), true);
        SetPrivate(state, nameof(StudioWorkspaceState.LiveRunHeadline), "Running checkout flow");
        SetPrivate(state, nameof(StudioWorkspaceState.LiveCurrentStepMessage), "Waiting for the next response.");

        var cut = RenderComponent<Home>();

        Assert.Contains("studio-shell--busy", cut.Find("[data-testid='studio-shell']").GetAttribute("class"));
        Assert.Contains("Running checkout flow", cut.Find("[data-testid='studio-busy-banner']").TextContent);
        Assert.Contains("Waiting for the next response.", cut.Find("[data-testid='studio-busy-banner']").TextContent);
    }

    [Fact]
    public void Home_loaded_workspace_route_hides_designer_and_results_pages()
    {
        var state = CreateState();
        var projectRoot = CreateProject("workspace-only");
        state.SetProjectPath(projectRoot);
        state.LoadProject();
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/workspace");

        var cut = RenderComponent<Home>();

        Assert.NotNull(cut.Find("[data-testid='workspace-section']"));
        Assert.Empty(cut.FindAll("[data-testid='designer-section']"));
        Assert.Empty(cut.FindAll("[data-testid='results-panel']"));
    }

    [Fact]
    public void Home_loaded_designer_route_hides_workspace_and_results_pages()
    {
        var state = CreateState();
        var projectRoot = CreateProject("designer-only");
        state.SetProjectPath(projectRoot);
        state.LoadProject();
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/designer");

        var cut = RenderComponent<Home>();

        Assert.NotNull(cut.Find("[data-testid='designer-section']"));
        Assert.NotNull(cut.Find("[data-testid='explorer-panel']"));
        Assert.Empty(cut.FindAll("[data-testid='workspace-section']"));
        Assert.Empty(cut.FindAll("[data-testid='results-panel']"));
    }

    [Fact]
    public void Home_loaded_results_route_hides_workspace_and_designer_pages()
    {
        var state = CreateState();
        var projectRoot = CreateProject("results-only");
        state.SetProjectPath(projectRoot);
        state.LoadProject();
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/results");

        var cut = RenderComponent<Home>();

        Assert.NotNull(cut.Find("[data-testid='results-panel']"));
        Assert.Empty(cut.FindAll("[data-testid='workspace-section']"));
        Assert.Empty(cut.FindAll("[data-testid='designer-section']"));
    }

    [Fact]
    public void Home_updates_visible_page_when_navigation_changes_after_initial_render()
    {
        var state = CreateState();
        var projectRoot = CreateProject("route-change");
        state.SetProjectPath(projectRoot);
        state.LoadProject();

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("http://localhost/workspace");

        var cut = RenderComponent<Home>();
        Assert.NotNull(cut.Find("[data-testid='workspace-section']"));

        navigation.NavigateTo("http://localhost/designer");
        cut.Render();
        Assert.NotNull(cut.Find("[data-testid='designer-section']"));
        Assert.Empty(cut.FindAll("[data-testid='workspace-section']"));

        navigation.NavigateTo("http://localhost/results");
        cut.Render();
        Assert.NotNull(cut.Find("[data-testid='results-panel']"));
        Assert.Empty(cut.FindAll("[data-testid='designer-section']"));
    }

    [Fact]
    public void Home_open_file_copies_selected_flow_path()
    {
        var state = CreateState();
        var projectRoot = CreateProject("home-open-file");
        state.SetProjectPath(projectRoot);
        state.LoadProject();

        var cut = RenderComponent<Home>();
        cut.Find("[data-testid='open-selected-file']").Click();

        var invocation = JSInterop.Invocations.Single(entry => entry.Identifier == "navigator.clipboard.writeText");
        Assert.Equal(state.SelectedFlow!.FilePath, invocation.Arguments[0]?.ToString());
    }

    [Fact]
    public async Task Home_more_actions_switch_between_flow_and_suite_tabs()
    {
        var state = CreateState();
        var projectRoot = CreateProject("home-actions");
        state.SetProjectPath(projectRoot);
        state.LoadProject();
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/designer");

        var cut = RenderComponent<Home>();

        await InvokePrivateAsync(cut.Instance, "CreateFlowAsync");
        cut.Render();
        Assert.Contains(cut.FindAll(".tab-button.active"), button => button.TextContent.Contains("Flow editor", StringComparison.Ordinal));

        await InvokePrivateAsync(cut.Instance, "CreateSuiteAsync");
        cut.Render();
        Assert.Contains(cut.FindAll(".tab-button.active"), button => button.TextContent.Contains("Suite editor", StringComparison.Ordinal));
    }

    [Fact]
    public void Home_samples_mode_shows_demo_guidance()
    {
        CreateState();

        var cut = RenderComponent<Home>();
        cut.Find("[data-testid='startup-mode-samples']").Click();

        Assert.NotNull(cut.Find("[data-testid='startup-samples-panel']"));
        Assert.NotNull(cut.Find("[data-testid='demo-filter']"));
    }

    [Fact]
    public void Home_open_mode_surfaces_recent_and_suggested_sections()
    {
        CreateState();
        var cut = RenderComponent<Home>();

        Assert.NotNull(cut.Find("[data-testid='suggested-workspace-panel']"));
        Assert.NotNull(cut.Find("[data-testid='recent-workspaces-panel']"));
    }

    [Fact]
    public void Home_open_mode_keeps_workspace_picker_accessible()
    {
        var state = CreateState();

        var cut = RenderComponent<Home>();
        var browseButton = cut.FindAll("[data-testid='open-workspace-picker-from-suggested']").FirstOrDefault()
            ?? cut.FindAll("button").First(button => button.TextContent.Contains("Browse workspaces", StringComparison.Ordinal));
        browseButton.Click();

        Assert.True(state.IsWorkspacePickerOpen);
        Assert.Contains("Browse for a workspace", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_wizard_can_switch_to_samples_mode()
    {
        CreateState();

        var cut = RenderComponent<Home>();
        cut.Find("[data-testid='startup-mode-samples']").Click();

        Assert.NotNull(cut.Find("[data-testid='startup-samples-panel']"));
        Assert.NotNull(cut.Find("[data-testid='demo-filter']"));
        Assert.Contains("Start from a sample or demo", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_recent_workspace_filter_narrows_visible_recent_cards()
    {
        var first = CreateDirectory("recent-httpbin-smoke");
        var second = CreateDirectory("recent-web-smoke");
        CreateState(recentWorkspaces: [first, second]);

        var cut = RenderComponent<Home>();

        cut.Find("[data-testid='recent-workspace-filter']").Input("web");

        var renderedRecentCards = cut.FindAll("[data-testid^='recent-workspace-card-']")
            .Select(element => element.TextContent)
            .ToList();

        Assert.Single(renderedRecentCards);
        Assert.Contains("recent-web-smoke", renderedRecentCards[0], StringComparison.Ordinal);
        Assert.Contains("1 shown", cut.Markup);
    }

    [Fact]
    public void Home_recent_workspace_actions_can_stage_remove_and_clear_entries()
    {
        var first = CreateDirectory("recent-action-a");
        var second = CreateDirectory("recent-action-b");
        var state = CreateState(recentWorkspaces: [first, second]);

        var cut = RenderComponent<Home>();

        var secondCard = cut.FindAll("[data-testid^='recent-workspace-card-']")
            .Single(element => element.TextContent.Contains("recent-action-b", StringComparison.Ordinal));
        secondCard.QuerySelector("[data-testid^='use-recent-workspace-']")!.Click();
        Assert.Equal(second, state.ProjectPathInput);

        var firstCard = cut.FindAll("[data-testid^='recent-workspace-card-']")
            .Single(element => element.TextContent.Contains("recent-action-a", StringComparison.Ordinal));
        firstCard.QuerySelector("[data-testid^='remove-recent-workspace-']")!.Click();
        Assert.Single(state.RecentWorkspaces);
        Assert.DoesNotContain(first, state.RecentWorkspaces);

        cut.Find("[data-testid='clear-recent-workspaces']").Click();
        Assert.Empty(state.RecentWorkspaces);
        Assert.Contains("No recent workspaces yet", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_workspace_setup_summary_reflects_current_configuration_and_invalid_retry()
    {
        var state = CreateState(runnerService: new FakeRunnerService(
        [
            CreateNode("local", "Embedded local", "Local embedded runner", StudioRunnerTransportKind.Embedded, "This machine", ["web"], StudioRunnerNodeStatus.Healthy),
            CreateNode("remote-browser", "Browser lab", "Remote browser node", StudioRunnerTransportKind.RemoteHttp, "Lab rack", ["browser"], StudioRunnerNodeStatus.Busy, activeRunId: "run-42")
        ]));
        var workspace = CreateProject("workspace-summary");
        state.SetProjectPath(workspace);
        state.LoadProject();
        state.SelectedProfile = "local";
        state.RetryCountOverrideText = "oops";
        state.ScreenshotPolicy = "every-step";
        state.SelectedRunnerNodeId = "remote-browser";

        var cut = RenderComponent<Home>();
        var summary = cut.Find("[data-testid='workspace-setup-summary']").TextContent;

        Assert.Contains("Workspace: workspace-summary", summary, StringComparison.Ordinal);
        Assert.Contains("Profile: local", summary, StringComparison.Ordinal);
        Assert.Contains("Retries: Invalid", summary, StringComparison.Ordinal);
        Assert.Contains("Screenshots: Every step", summary, StringComparison.Ordinal);
        Assert.Contains("Node: Browser lab (Busy)", summary, StringComparison.Ordinal);
        Assert.Contains("Retry override must be a non-negative integer.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_workspace_advanced_controls_are_grouped_under_a_collapsed_advanced_panel()
    {
        CreateState();

        var cut = RenderComponent<Home>();
        var advancedPanel = cut.Find("[data-testid='workspace-advanced-panel']");

        Assert.False(advancedPanel.HasAttribute("open"));
        Assert.Contains("Retry override", advancedPanel.TextContent, StringComparison.Ordinal);
        Assert.Contains("Screenshot policy", advancedPanel.TextContent, StringComparison.Ordinal);
        Assert.Contains("Execution node", advancedPanel.TextContent, StringComparison.Ordinal);
        Assert.Contains("Show advanced controls", advancedPanel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_workspace_path_readiness_reports_detected_cress_workspace()
    {
        var state = CreateState();
        var workspace = CreateProject("path-readiness");
        state.SetProjectPath(workspace);

        var cut = RenderComponent<Home>();
        var readiness = cut.Find("[data-testid='workspace-path-readiness']").TextContent;

        Assert.Contains("Cress workspace detected", readiness, StringComparison.Ordinal);
        Assert.Contains("ready to load", readiness, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_workspace_path_readiness_reports_missing_folder()
    {
        var state = CreateState();
        var missingPath = Path.Combine(Path.GetTempPath(), "cress-home-tests", Guid.NewGuid().ToString("N"), "missing-workspace");
        state.SetProjectPath(missingPath);

        var cut = RenderComponent<Home>();
        var readiness = cut.Find("[data-testid='workspace-path-readiness']").TextContent;

        Assert.Contains("Folder not found", readiness, StringComparison.Ordinal);
        Assert.Contains("Pick an existing folder", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_wizard_switches_between_open_and_new_modes()
    {
        CreateState();
        var cut = RenderComponent<Home>();
        Assert.NotNull(cut.Find("[data-testid='startup-open-panel']"));

        cut.Find("[data-testid='startup-mode-new']").Click();

        Assert.NotNull(cut.Find("[data-testid='startup-new-panel']"));
        Assert.Empty(cut.FindAll("[data-testid='startup-open-panel']"));
    }

    [Fact]
    public void Home_new_mode_prioritizes_folder_selection()
    {
        var state = CreateState();
        var cut = RenderComponent<Home>();
        cut.Find("[data-testid='startup-mode-new']").Click();
        cut.Find("[data-testid='startup-new-browse']").Click();

        Assert.True(state.IsWorkspacePickerOpen);
    }

    [Fact]
    public void Home_onboarding_defaults_to_single_open_mode_panel()
    {
        CreateState();

        var cut = RenderComponent<Home>();

        Assert.NotNull(cut.Find("[data-testid='startup-open-panel']"));
        Assert.Empty(cut.FindAll("[data-testid='startup-new-panel']"));
        Assert.Empty(cut.FindAll("[data-testid='startup-samples-panel']"));
    }

    private StudioWorkspaceState CreateState(string[]? recentWorkspaces = null, IStudioRunnerService? runnerService = null)
    {
        Services.AddCressStudioBackend();
        if (runnerService is not null)
        {
            Services.AddSingleton(runnerService);
        }

        Services.AddSingleton<StudioWorkspaceState>();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        JSInterop.Setup<string[]>("cressStudio.getRecentWorkspaces").SetResult(recentWorkspaces ?? []);
        JSInterop.SetupVoid("cressStudio.setRecentWorkspaces", _ => true);
        JSInterop.SetupVoid("cressStudio.scrollSectionIntoView", _ => true);
        JSInterop.Setup<string>("getTheme").SetResult("system");
        JSInterop.SetupVoid("setTheme", _ => true);
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    private static StudioRunnerNodeSnapshot CreateNode(
        string id,
        string name,
        string description,
        StudioRunnerTransportKind transport,
        string location,
        IReadOnlyList<string> capabilities,
        StudioRunnerNodeStatus status,
        string? activeRunId = null,
        string? lastError = null)
        => new(
            id,
            name,
            name,
            description,
            transport,
            location,
            capabilities,
            status,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddMinutes(-5),
            null,
            activeRunId,
            activeRunId,
            0,
            lastError);

    private static async Task InvokePrivateAsync(object target, string methodName, params object[]? arguments)
    {
        var method = target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");
        var result = method.Invoke(target, arguments);
        if (result is Task task)
        {
            await task;
        }
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found.");
        property.SetValue(target, value);
    }

    private string CreateProject(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "cress-home-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        _temporaryRoots.Add(Path.GetDirectoryName(root)!);

        WriteFile(root, Path.Combine(".cress", "config.yaml"), """
        version: 1
        project:
          name: Home test sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        defaults:
          retries: 0
        """);
        WriteFile(root, Path.Combine(".cress", "profiles", "local.yaml"), """
        baseUrl: https://example.test
        evidence:
          screenshotPolicy: on-failure
        """);
        WriteFile(root, Path.Combine("capabilities", "search.md"), """
        ---
        version: 1
        id: capability.search
        owner: qa
        risk: medium
        ---

        # Search capability
        """);
        WriteFile(root, Path.Combine("flows", "search.flow.yaml"), """
        version: 1
        id: flow.search
        name: Search flow
        when:
          - step: http.get
            with:
              url: https://example.test/search
        then:
          - expect: http.assert-status
            with:
              status: "200"
        """);
        WriteFile(root, Path.Combine("steps", "http.yaml"), """
        version: 1
        steps:
          - name: http.get
            implementation:
              plugin: builtin.http
              operation: get
          - name: http.assert-status
            implementation:
              plugin: builtin.http
              operation: assert-status
        """);

        return root;
    }

    private string CreateDirectory(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "cress-home-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        _temporaryRoots.Add(Path.GetDirectoryName(root)!);
        return root;
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    void IDisposable.Dispose()
    {
        foreach (var root in _temporaryRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        base.Dispose();
    }

    private sealed class FakeRunnerService(IReadOnlyList<StudioRunnerNodeSnapshot> nodes) : IStudioRunnerService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<StudioRunnerNodeSnapshot> ListNodes() => nodes;

        public Task<StudioRunnerDispatchResult> DispatchAsync(
            StudioRunnerDispatchRequest request,
            IProgress<RuntimeProgressUpdate>? progress,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
