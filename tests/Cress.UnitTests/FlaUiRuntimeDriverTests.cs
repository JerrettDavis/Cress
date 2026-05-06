using System.Diagnostics;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Execution.Drivers;
using Cress.ProjectSystem;
using Cress.Specs;

namespace Cress.UnitTests;

public sealed class FlaUiRuntimeDriverTests
{
    [Fact]
    public void ProfileLoader_LoadsFlaUiConfiguration()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "local.yaml"), """
        profile: local
        flaui:
          applicationPath: C:\Apps\Sample.exe
          arguments: --demo
          windowTitle: Sample App
          launchTimeoutMs: 12345
        """);

        var loader = new ProfileLoader();
        var result = loader.Load(workspace.GetPath("project"), "local");

        Assert.True(result.Success);
        Assert.Equal(@"C:\Apps\Sample.exe", result.Value!.FlaUi!.ApplicationPath);
        Assert.Equal("--demo", result.Value.FlaUi.Arguments);
        Assert.Equal("Sample App", result.Value.FlaUi.WindowTitle);
        Assert.Equal(12345, result.Value.FlaUi.LaunchTimeoutMs);
    }

    [Fact]
    public async Task RuntimeOrchestrator_RunsRealFlaUiFlow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var applicationPath = await BuildFlaUiTestAppAsync();

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
              automationId: NameInput
              value: Ada
          - step: desktop.click
            with:
              automationId: ContinueButton
          - step: desktop.capture
            with:
              name: greeting
        then:
          - expect: desktop.text
            with:
              automationId: GreetingLabel
              text: Hello Ada
          - expect: desktop.window_title
            with:
              title: Cress FlaUI Test App
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "flaui.yaml"), """
        version: 1
        steps:
          - name: desktop.open
            drivers:
              - flaui
            retrySafe: true
            implementation:
              plugin: builtin.flaui
              operation: open
          - name: desktop.fill
            drivers:
              - flaui
            retrySafe: true
            implementation:
              plugin: builtin.flaui
              operation: fill
          - name: desktop.click
            drivers:
              - flaui
            retrySafe: true
            implementation:
              plugin: builtin.flaui
              operation: click
          - name: desktop.capture
            drivers:
              - flaui
            retrySafe: true
            implementation:
              plugin: builtin.flaui
              operation: screenshot
          - name: desktop.text
            drivers:
              - flaui
            retrySafe: true
            implementation:
              plugin: builtin.flaui
              operation: assert-text
          - name: desktop.window_title
            drivers:
              - flaui
            retrySafe: true
            implementation:
              plugin: builtin.flaui
              operation: assert-window-title
        """);

        var orchestrator = CreateRuntimeOrchestrator();
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.True(result.Passed, string.Join(" | ", result.Flows.Select(flow => flow.FailureMessage ?? flow.FailureClassification)));
        Assert.Single(result.Flows);
        Assert.Contains("flaui", result.Flows[0].Drivers, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.ArtifactIndex.Entries.TryGetValue("screenshots", out var screenshots));
        Assert.True(screenshots!.Count >= 2);
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
                new FlaUiRuntimeDriver(),
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
          flaui:
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
        flaui:
          applicationPath: '{{escapedPath}}'
          windowTitle: Cress FlaUI Test App
          launchTimeoutMs: 10000
        variables:
          environment: test
        """);

        Directory.CreateDirectory(workspace.GetPath("project", "steps", "manifests"));
        Directory.CreateDirectory(workspace.GetPath("project", "fixtures"));
    }

    private static async Task<string> BuildFlaUiTestAppAsync()
    {
        var projectFile = Path.Combine(GetRepositoryRoot(), "tests", "Cress.FlaUi.TestApp", "Cress.FlaUi.TestApp.csproj");
        await RunProcessAsync("dotnet", GetRepositoryRoot(), "build", projectFile, "-v", "minimal");
        return Path.Combine(GetRepositoryRoot(), "tests", "Cress.FlaUi.TestApp", "bin", "Debug", "net10.0-windows", "Cress.FlaUi.TestApp.exe");
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
