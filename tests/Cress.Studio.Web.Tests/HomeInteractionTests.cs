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
    [InlineData("", "workspace-section")]
    [InlineData("designer", "designer-section")]
    public void Home_highlights_section_from_route(string route, string focusedTestId)
    {
        CreateState();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"http://localhost/{route}".TrimEnd('/'));

        var cut = RenderComponent<Home>();

        Assert.Contains("panel-focus", cut.Find($"[data-testid='{focusedTestId}']").GetAttribute("class"));
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

        var cut = RenderComponent<Home>();

        await InvokePrivateAsync(cut.Instance, "CreateFlowAsync");
        cut.Render();
        Assert.Contains(cut.FindAll(".tab-button.active"), button => button.TextContent.Contains("Flow editor", StringComparison.Ordinal));

        await InvokePrivateAsync(cut.Instance, "CreateSuiteAsync");
        cut.Render();
        Assert.Contains(cut.FindAll(".tab-button.active"), button => button.TextContent.Contains("Suite editor", StringComparison.Ordinal));
    }

    [Fact]
    public void Home_hero_load_demo_button_loads_first_demo_workspace()
    {
        var state = CreateState();

        var cut = RenderComponent<Home>();
        cut.Find("[data-testid='hero-load-demo']").Click();

        Assert.True(state.HasLoadedProject);
        Assert.DoesNotContain("hero-panel", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_use_demo_path_button_sets_workspace_without_loading_project()
    {
        var state = CreateState();
        var demo = Assert.Single(state.DemoWorkspaces, item => item.Id == "calc-smoke");

        var cut = RenderComponent<Home>();
        cut.Find("[data-testid='use-demo-path-calc-smoke']").Click();

        Assert.False(state.HasLoadedProject);
        Assert.Equal(demo.ProjectPath, state.ProjectPathInput);
    }

    [Fact]
    public void Home_browse_workspaces_button_opens_workspace_picker()
    {
        var state = CreateState();

        var cut = RenderComponent<Home>();
        cut.Find("[data-testid='hero-browse-workspaces']").Click();

        Assert.True(state.IsWorkspacePickerOpen);
        Assert.Contains("Browse for a workspace", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Home_demo_filter_narrows_visible_demo_cards()
    {
        CreateState();

        var cut = RenderComponent<Home>();

        cut.Find("[data-testid='demo-filter']").Input("browser");

        Assert.Contains("Browser search-style demo", cut.Markup);
        Assert.DoesNotContain("HTTP smoke demo", cut.Markup);
        Assert.DoesNotContain("Calculator desktop demo", cut.Markup);
        Assert.Contains("1 shown", cut.Markup);
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
    public void Home_runner_node_filter_narrows_visible_nodes()
    {
        CreateState(runnerService: new FakeRunnerService(
        [
            CreateNode("local", "Embedded local", "Local embedded runner", StudioRunnerTransportKind.Embedded, "This machine", ["web", "http"], StudioRunnerNodeStatus.Healthy),
            CreateNode("remote-browser", "Browser lab", "Remote browser node", StudioRunnerTransportKind.RemoteHttp, "Lab rack", ["browser"], StudioRunnerNodeStatus.Busy, activeRunId: "run-42"),
            CreateNode("remote-desktop", "Desktop lab", "Remote desktop node", StudioRunnerTransportKind.RemoteHttp, "QA floor", ["desktop"], StudioRunnerNodeStatus.Degraded, lastError: "Recorder not responding")
        ]));

        var cut = RenderComponent<Home>();

        cut.Find("[data-testid='runner-node-filter']").Input("desktop");

        var renderedNodes = cut.FindAll("[data-testid^='runner-node-']")
            .Where(element => element.TagName.Equals("DIV", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.TextContent)
            .ToList();

        Assert.Contains(renderedNodes, content => content.Contains("Desktop lab", StringComparison.Ordinal));
        Assert.DoesNotContain(renderedNodes, content => content.Contains("Embedded local", StringComparison.Ordinal));
        Assert.DoesNotContain(renderedNodes, content => content.Contains("Browser lab", StringComparison.Ordinal));
        Assert.Contains("1 shown", cut.Markup);
    }

    [Fact]
    public void Home_runner_node_issue_toggle_limits_list_to_attention_needed_nodes()
    {
        CreateState(runnerService: new FakeRunnerService(
        [
            CreateNode("local", "Embedded local", "Local embedded runner", StudioRunnerTransportKind.Embedded, "This machine", ["web"], StudioRunnerNodeStatus.Healthy),
            CreateNode("remote-offline", "Remote offline", "Remote fallback node", StudioRunnerTransportKind.RemoteHttp, "West", ["browser"], StudioRunnerNodeStatus.Offline, lastError: "Heartbeat expired"),
            CreateNode("remote-degraded", "Remote degraded", "Remote desktop node", StudioRunnerTransportKind.RemoteHttp, "East", ["desktop"], StudioRunnerNodeStatus.Degraded, lastError: "Queue backlog")
        ]));

        var cut = RenderComponent<Home>();

        cut.Find("[data-testid='runner-node-issues-only']").Change(true);

        var renderedNodes = cut.FindAll("[data-testid^='runner-node-']")
            .Where(element => element.TagName.Equals("DIV", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.TextContent)
            .ToList();

        Assert.DoesNotContain(renderedNodes, content => content.Contains("Embedded local", StringComparison.Ordinal));
        Assert.Contains(renderedNodes, content => content.Contains("Remote offline", StringComparison.Ordinal));
        Assert.Contains(renderedNodes, content => content.Contains("Remote degraded", StringComparison.Ordinal));
        Assert.Contains("Needs attention: 2", cut.Markup);
        Assert.Contains("Issues only", cut.Markup);
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
