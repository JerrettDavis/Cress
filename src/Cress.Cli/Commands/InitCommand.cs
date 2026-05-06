using System.CommandLine;
using System.CommandLine.Invocation;

namespace Cress.Cli.Commands;

public static class InitCommand
{
    public static Command Create()
    {
        var command = new Command("init", "Initialize a new Cress project");
        var pathArgument = new Argument<DirectoryInfo?>("path", () => null, "Target directory");
        var forceOption = new Option<bool>("--force", "Overwrite an existing Cress project");

        command.AddArgument(pathArgument);
        command.AddOption(forceOption);
        command.SetHandler((InvocationContext context) =>
        {
            var targetDirectory = context.ParseResult.GetValueForArgument(pathArgument)?.FullName ?? Environment.CurrentDirectory;
            var force = context.ParseResult.GetValueForOption(forceOption);
            context.ExitCode = Execute(targetDirectory, force);
        });

        return command;
    }

    private static int Execute(string targetDirectory, bool force)
    {
        try
        {
            Directory.CreateDirectory(targetDirectory);

            var projectName = new DirectoryInfo(targetDirectory).Name;
            var configPath = Path.Combine(targetDirectory, ".cress", "config.yaml");

            if (File.Exists(configPath) && !force)
            {
                Console.Error.WriteLine($"A Cress project already exists at '{targetDirectory}'. Use --force to overwrite.");
                return 1;
            }

            foreach (var directory in GetDirectories(targetDirectory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(configPath, GetConfigYaml(projectName));
            File.WriteAllText(Path.Combine(targetDirectory, ".cress", "policy.yaml"), GetPolicyYaml());
            File.WriteAllText(Path.Combine(targetDirectory, ".cress", "profiles", "local.yaml"), GetLocalProfileYaml());
            File.WriteAllText(Path.Combine(targetDirectory, ".cress", "profiles", "ci.yaml"), GetCiProfileYaml());
            File.WriteAllText(Path.Combine(targetDirectory, "capabilities", "example.md"), GetCapabilityMarkdown());
            File.WriteAllText(Path.Combine(targetDirectory, "flows", "example", "example-flow.flow.yaml"), GetFlowYaml());

            Console.Out.WriteLine($"Initialized Cress project at {targetDirectory}");
            Console.Out.WriteLine();
            Console.Out.WriteLine("Recommended next steps:");
            Console.Out.WriteLine("  1. Review .cress\\config.yaml");
            Console.Out.WriteLine("  2. Add your own capability and flow files");
            Console.Out.WriteLine("  3. Run `cress validate`");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 2;
        }
    }

    private static IEnumerable<string> GetDirectories(string root)
    {
        yield return Path.Combine(root, ".cress");
        yield return Path.Combine(root, ".cress", "profiles");
        yield return Path.Combine(root, "capabilities");
        yield return Path.Combine(root, "flows");
        yield return Path.Combine(root, "flows", "example");
        yield return Path.Combine(root, "models");
        yield return Path.Combine(root, "fixtures");
        yield return Path.Combine(root, "fixtures", "personas");
        yield return Path.Combine(root, "fixtures", "data");
        yield return Path.Combine(root, "steps");
        yield return Path.Combine(root, "steps", "manifests");
        yield return Path.Combine(root, "steps", "dotnet");
        yield return Path.Combine(root, "steps", "node");
        yield return Path.Combine(root, "plugins");
        yield return Path.Combine(root, "artifacts");
        yield return Path.Combine(root, "artifacts", "runs");
        yield return Path.Combine(root, "reports");
        yield return Path.Combine(root, "schemas");
    }

    private static string GetConfigYaml(string projectName) => $"""
version: 1
project:
  name: {projectName}
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
  playwright:
    enabled: false
  flaui:
    enabled: false
  http:
    enabled: true
""";

    private static string GetPolicyYaml() => """
version: 1
validation:
  strict: false
execution:
  requireEvidence: true
""";

    private static string GetLocalProfileYaml() => """
profile: local
baseUrl: http://localhost:5000
timeouts:
  step: 30000
  expectation: 10000
evidence:
  mode: standard
  screenshots: true
# flaui:
#   applicationPath: C:\Path\To\YourApp.exe
#   windowTitle: Your application
#   launchTimeoutMs: 10000
variables:
  environment: local
""";

    private static string GetCiProfileYaml() => """
profile: ci
timeouts:
  step: 60000
  expectation: 15000
evidence:
  mode: standard
  screenshots: true
variables:
  environment: ci
""";

    private static string GetCapabilityMarkdown() => """
---
version: 1
id: example
owner: Platform
risk: low
tags:
  - example
---

# Capability: Example capability

An example capability to demonstrate the Cress project structure.

## Rules

- System must be available.

## Acceptance Criteria

### EXAMPLE-AC1

Given the system is available, when a user opens the app, then the app is visible.
""";

    private static string GetFlowYaml() => """
version: 1
id: example-flow
name: Example flow
capability: example

tags:
  - example

given:
  - system is available

when:
  - step: app.open

then:
  - expect: app.is_visible
""";
}
