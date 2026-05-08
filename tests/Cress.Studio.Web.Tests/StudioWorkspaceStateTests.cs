using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class StudioWorkspaceStateTests : IDisposable
{
    private readonly List<string> _temporaryRoots = [];

    [Fact]
    public void SetRecentWorkspaces_normalizes_existing_paths_and_sets_default_input()
    {
        using var scope = CreateState();
        var first = CreateDirectory("recent-a");
        var second = CreateDirectory("recent-b");

        scope.State.SetProjectPath(null);
        scope.State.SetRecentWorkspaces([first, first, second, Path.Combine(first, "missing")]);

        Assert.Equal(2, scope.State.RecentWorkspaces.Count);
        Assert.Equal(first, scope.State.RecentWorkspaces[0]);
        Assert.Equal(second, scope.State.RecentWorkspaces[1]);
        Assert.Equal(first, scope.State.ProjectPathInput);
    }

    [Fact]
    public void WorkspacePicker_can_choose_a_folder_without_loading()
    {
        using var scope = CreateState();
        var root = CreateDirectory("picker-root");
        var child = Path.Combine(root, "child-workspace");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, ".placeholder"), "x");

        scope.State.SetProjectPath(root);
        scope.State.OpenWorkspacePicker();

        Assert.True(scope.State.IsWorkspacePickerOpen);
        Assert.Contains(scope.State.WorkspaceBrowserEntries, entry => entry.Path == child);

        scope.State.ChooseWorkspaceFromPicker(child, loadImmediately: false);

        Assert.False(scope.State.IsWorkspacePickerOpen);
        Assert.Equal(child, scope.State.ProjectPathInput);
        Assert.False(scope.State.HasLoadedProject);
    }

    [Fact]
    public void LoadProject_loads_snapshot_and_supports_selecting_capabilities_fixtures_and_steps()
    {
        using var scope = CreateState();
        var projectRoot = CreateProject("state-project");

        scope.State.SetProjectPath(projectRoot);
        scope.State.LoadProject();

        Assert.True(scope.State.HasLoadedProject);
        Assert.Equal(1, scope.State.FlowCount);
        Assert.Equal(1, scope.State.CapabilityCount);
        Assert.Equal(1, scope.State.FixtureCount);
        Assert.Equal(1, scope.State.StepCount);
        Assert.Contains("Loaded", scope.State.StatusMessage, StringComparison.OrdinalIgnoreCase);

        var capability = Assert.Single(scope.State.Snapshot!.Catalog.Capabilities);
        scope.State.SelectCapability(capability);
        Assert.Equal(capability.Name, scope.State.SelectionHeadline);
        Assert.Contains("# Search capability", scope.State.SourceEditorText, StringComparison.Ordinal);

        scope.State.SelectFixture("shared.fixture");
        Assert.Equal("shared.fixture", scope.State.SelectionHeadline);
        Assert.Contains("seed.customer", scope.State.SelectedAssetSummary, StringComparison.Ordinal);

        scope.State.SelectStep("http.get");
        Assert.Equal("http.get", scope.State.SelectionHeadline);
        Assert.Contains("operation: get", scope.State.SourceEditorText, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadProject_with_blank_path_uses_suggested_workspace()
    {
        using var scope = CreateState();

        scope.State.SetProjectPath(null);
        scope.State.LoadProject();

        Assert.True(scope.State.HasLoadedProject);
        Assert.Contains("Loaded", scope.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadDemoWorkspace_uses_real_demo_and_loads_project()
    {
        using var scope = CreateState();
        Assert.NotEmpty(scope.State.DemoWorkspaces);
        var demo = scope.State.DemoWorkspaces[0];

        scope.State.LoadDemoWorkspace(demo.Id);

        Assert.True(scope.State.HasLoadedProject);
        Assert.Contains(Path.GetFileName(demo.ProjectPath), scope.State.ProjectPathInput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(demo.PreferredProfile ?? scope.State.SelectedProfile, scope.State.SelectedProfile);
    }

    [Fact]
    public void LoadDemoWorkspace_loads_runnable_calculator_demo_flow()
    {
        using var scope = CreateState();
        var demo = Assert.Single(scope.State.DemoWorkspaces, item => item.Id == "calc-smoke");

        scope.State.LoadDemoWorkspace(demo.Id);

        Assert.True(scope.State.HasLoadedProject);
        var snapshot = scope.State.Snapshot;
        Assert.NotNull(snapshot);
        var flow = Assert.Single(snapshot!.Catalog.NormalizedFlows, item => item.FlowId == "calc.add-two-plus-two");
        var flowPath = Path.IsPathRooted(flow.SourceFile)
            ? flow.SourceFile
            : Path.Combine(snapshot.Catalog.ProjectRoot, flow.SourceFile);
        Assert.True(File.Exists(flowPath), $"Expected calculator flow at '{flowPath}'.");

        var source = scope.State.SelectedFlow is null
            ? File.ReadAllText(flowPath)
            : scope.State.SourceEditorText;

        Assert.Contains("ui.launch", source, StringComparison.Ordinal);
        Assert.Contains("status: ready", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CalculatorResults", source, StringComparison.Ordinal);
    }

    private StateScope CreateState()
    {
        var services = new ServiceCollection();
        services.AddCressStudioBackend();
        services.AddSingleton<IStudioRecorderService, FakeStudioRecorderService>();
        services.AddSingleton<StudioWorkspaceState>();
        var provider = services.BuildServiceProvider();
        return new StateScope(provider, provider.GetRequiredService<StudioWorkspaceState>());
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "cress-studio-state-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        _temporaryRoots.Add(Path.GetDirectoryName(path)!);
        return path;
    }

    private string CreateProject(string name)
    {
        var root = CreateDirectory(name);
        WriteFile(root, Path.Combine(".cress", "config.yaml"), """
        version: 1
        project:
          name: Studio state sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        """);
        WriteFile(root, Path.Combine(".cress", "profiles", "local.yaml"), """
        baseUrl: https://example.test
        """);
        WriteFile(root, Path.Combine("capabilities", "search.md"), """
        ---
        version: 1
        id: capability.search
        owner: qa
        risk: medium
        ---

        # Search capability

        ## Rules
        - Return relevant results.
        """);
        WriteFile(root, Path.Combine("flows", "search.flow.yaml"), """
        version: 1
        id: flow.search
        name: Search flow
        fixtures:
          customer:
            use: shared.fixture
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
        """);
        WriteFile(root, Path.Combine("fixtures", "fixtures.yaml"), """
        version: 1
        fixtures:
          shared.fixture:
            type: seed.customer
            strategy: static
        """);

        return root;
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    public void Dispose()
    {
        foreach (var root in _temporaryRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private sealed record StateScope(ServiceProvider Provider, StudioWorkspaceState State) : IDisposable
    {
        public void Dispose()
            => Provider.Dispose();
    }
}
