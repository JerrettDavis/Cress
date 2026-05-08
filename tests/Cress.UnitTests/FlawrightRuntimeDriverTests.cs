using System.Diagnostics;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Execution.Drivers;
using Cress.ProjectSystem;
using Cress.Specs;

namespace Cress.UnitTests;

public sealed class FlawrightRuntimeDriverTests
{
    [Fact]
    public void ProfileLoader_LoadsFlawrightConfiguration()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "local.yaml"), """
        profile: local
        flawright:
          applicationPath: C:\Apps\Sample.exe
          arguments: --demo
          windowTitle: Sample App
          launchTimeoutMs: 12345
        """);

        var loader = new ProfileLoader();
        var result = loader.Load(workspace.GetPath("project"), "local");

        Assert.True(result.Success);
        Assert.Equal(@"C:\Apps\Sample.exe", result.Value!.Flawright!.ApplicationPath);
        Assert.Equal("--demo", result.Value.Flawright.Arguments);
        Assert.Equal("Sample App", result.Value.Flawright.WindowTitle);
        Assert.Equal(12345, result.Value.Flawright.LaunchTimeoutMs);
    }

    [Fact]
    public async Task RuntimeOrchestrator_RunsRealFlawrightFlow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var applicationPath = await BuildFlawrightTestAppAsync();

        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, applicationPath);
        workspace.WriteFile(Path.Combine("project", "flows", "desktop.flow.yaml"), """
        version: 1
        id: desktop-flow
        name: Desktop flow
        when:
          - step: desktop.open
          - step: desktop.fill
            with:
              selector: "#NameInput"
              value: Ada
          - step: desktop.click
            with:
              selector: "name:Continue"
          - step: desktop.capture
            with:
              name: greeting
        then:
          - expect: desktop.text
            with:
              selector: "#GreetingLabel"
              text: Hello Ada
          - expect: desktop.window_title
            with:
              title: Cress Flawright Test App
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "flawright.yaml"), """
        version: 1
        steps:
          - name: desktop.open
            drivers:
              - flawright
            retrySafe: true
            implementation:
              plugin: builtin.flawright
              operation: open
          - name: desktop.fill
            drivers:
              - flawright
            retrySafe: true
            implementation:
              plugin: builtin.flawright
              operation: fill
          - name: desktop.click
            drivers:
              - flawright
            retrySafe: true
            implementation:
              plugin: builtin.flawright
              operation: click
          - name: desktop.capture
            drivers:
              - flawright
            retrySafe: true
            implementation:
              plugin: builtin.flawright
              operation: screenshot
          - name: desktop.text
            drivers:
              - flawright
            retrySafe: true
            implementation:
              plugin: builtin.flawright
              operation: assert-text
          - name: desktop.window_title
            drivers:
              - flawright
            retrySafe: true
            implementation:
              plugin: builtin.flawright
              operation: assert-window-title
        """);

        var orchestrator = CreateRuntimeOrchestrator();
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.True(result.Passed, string.Join(" | ", result.Flows.Select(flow => flow.FailureMessage ?? flow.FailureClassification)));
        Assert.Single(result.Flows);
        Assert.Contains("flawright", result.Flows[0].Drivers, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.ArtifactIndex.Entries.TryGetValue("screenshots", out var screenshots));
        Assert.True(screenshots!.Count >= 2);
    }

    [Fact]
    public async Task RuntimeOrchestrator_can_launch_desktop_app_by_command_name_from_path()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var applicationPath = await BuildFlawrightTestAppAsync();
        var applicationDirectory = Path.GetDirectoryName(applicationPath)!;
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{applicationDirectory};{originalPath}");

            using var workspace = new TestWorkspace();
            WriteProjectFiles(workspace, applicationPath);
            workspace.WriteFile(Path.Combine("project", "flows", "desktop.flow.yaml"), """
            version: 1
            id: desktop-flow-path-launch
            name: Desktop flow path launch
            when:
              - step: desktop.open
                with:
                  application: Cress.Flawright.TestApp.exe
            then:
              - expect: desktop.window_title
                with:
                  title: Cress Flawright Test App
            """);
            workspace.WriteFile(Path.Combine("project", "steps", "manifests", "flawright.yaml"), """
            version: 1
            steps:
              - name: desktop.open
                drivers:
                  - flawright
                retrySafe: true
                implementation:
                  plugin: builtin.flawright
                  operation: open
              - name: desktop.window_title
                drivers:
                  - flawright
                retrySafe: true
                implementation:
                  plugin: builtin.flawright
                  operation: assert-window-title
            """);

            var orchestrator = CreateRuntimeOrchestrator();
            var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

            Assert.True(result.Passed, string.Join(" | ", result.Flows.Select(flow => flow.FailureMessage ?? flow.FailureClassification)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private static RuntimeOrchestrator CreateRuntimeOrchestrator()
    {
        var configLoader = new ConfigLoader(new ProjectLocator());
        var catalogService = CreateCatalogService();
        return new RuntimeOrchestrator(
            catalogService,
            new PlanGenerator(),
            configLoader,
            new PluginHost(),
            new ReportGenerator(),
            [
                new HttpRuntimeDriver(),
                new FlawrightRuntimeDriver(),
                new PlaywrightRuntimeDriver()
            ]);
    }

    private static ProjectCatalogService CreateCatalogService()
    {
        var locator = new ProjectLocator();
        var configLoader = new ConfigLoader(locator);
        var profileLoader = new ProfileLoader();
        var flowParser = new FlowParser();
        var flowNormalizer = new FlowNormalizer();
        var capabilityParser = new CapabilityParser();
        var stepParser = new StepManifestParser();
        var fixtureParser = new FixtureManifestParser();
        return new ProjectCatalogService(
            locator,
            configLoader,
            profileLoader,
            flowParser,
            flowNormalizer,
            capabilityParser,
            stepParser,
            fixtureParser,
            new StepRegistry());
    }

    private static void WriteProjectFiles(TestWorkspace workspace, string applicationPath)
    {
        workspace.WriteFile(Path.Combine("project", ".cress", "config.yaml"), """
        version: 1
        project:
          name: Sample Project
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: artifacts/runs
          reports: reports
        defaults:
          timeout: 30000
          retries: 0
          evidence: standard
          cleanup: on-success
        plugins:
          discover:
            - plugins
            - steps
        drivers:
          http:
            enabled: true
          playwright:
            enabled: false
          flawright:
            enabled: true
        """);

        var escapedPath = applicationPath.Replace("'", "''");
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "local.yaml"), $$"""
        profile: local
        timeouts:
          driver: 10000
        evidence:
          mode: full
          screenshots: true
        flawright:
          applicationPath: '{{escapedPath}}'
          windowTitle: Cress Flawright Test App
          launchTimeoutMs: 10000
        variables:
          environment: test
        """);

        Directory.CreateDirectory(workspace.GetPath("project", "steps", "manifests"));
        Directory.CreateDirectory(workspace.GetPath("project", "fixtures"));
    }

    private static async Task<string> BuildFlawrightTestAppAsync()
    {
        var projectFile = Path.Combine(GetRepositoryRoot(), "tests", "Cress.Flawright.TestApp", "Cress.Flawright.TestApp.csproj");
        await RunProcessAsync("dotnet", GetRepositoryRoot(), "build", projectFile, "-v", "minimal");
        return Path.Combine(GetRepositoryRoot(), "tests", "Cress.Flawright.TestApp", "bin", "Debug", "net10.0-windows", "Cress.Flawright.TestApp.exe");
    }

    private static async Task RunProcessAsync(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "plans", "PLAN.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located from the test assembly.");
    }
}
