using Bunit;
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

    private StudioWorkspaceState CreateState(string[]? recentWorkspaces = null)
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        JSInterop.Setup<string[]>("cressStudio.getRecentWorkspaces").SetResult(recentWorkspaces ?? []);
        JSInterop.SetupVoid("cressStudio.setRecentWorkspaces", _ => true);
        JSInterop.SetupVoid("cressStudio.scrollSectionIntoView", _ => true);
        JSInterop.Setup<string>("getTheme").SetResult("system");
        JSInterop.SetupVoid("setTheme", _ => true);
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

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
}
