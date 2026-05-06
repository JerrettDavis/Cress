using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Specs;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;

namespace Cress.Cli.Commands;

public static class DiscoverCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("discover", "Discover project assets");
        var jsonOption = new Option<bool>("--json", "Emit machine-readable output");
        command.AddOption(jsonOption);
        command.SetHandler((InvocationContext context) => Execute(services, context, "all", jsonOption));

        foreach (var area in new[] { "flows", "steps", "capabilities", "fixtures", "drivers" })
        {
            var subcommand = new Command(area, $"Discover {area}");
            var subJsonOption = new Option<bool>("--json", "Emit machine-readable output");
            subcommand.AddOption(subJsonOption);
            subcommand.SetHandler((InvocationContext context) => Execute(services, context, area, subJsonOption));
            command.AddCommand(subcommand);
        }

        return command;
    }

    private static void Execute(IServiceProvider services, InvocationContext context, string mode, Option<bool> jsonOption)
    {
        try
        {
            var locator = services.GetRequiredService<ProjectLocator>();
            var configLoader = services.GetRequiredService<ConfigLoader>();
            var flowParser = services.GetRequiredService<FlowParser>();
            var capabilityParser = services.GetRequiredService<CapabilityParser>();
            var stepParser = services.GetRequiredService<StepManifestParser>();
            var fixtureParser = services.GetRequiredService<FixtureManifestParser>();
            var runtimeDrivers = services.GetServices<IRuntimeDriver>().ToDictionary(driver => driver.Name, StringComparer.OrdinalIgnoreCase);
            var projectRoot = locator.FindProjectRoot(Environment.CurrentDirectory);
            var json = context.ParseResult.GetValueForOption(jsonOption);

            if (projectRoot is null)
            {
                CommandSupport.WriteDiagnostics(
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "PRJ001",
                        Message = "Could not locate a Cress project root.",
                        File = Environment.CurrentDirectory
                    }
                ], json);

                context.ExitCode = 1;
                return;
            }

            var configResult = configLoader.Load(projectRoot);
            if (!configResult.Success)
            {
                CommandSupport.WriteDiagnostics(configResult.Diagnostics, json);
                context.ExitCode = 1;
                return;
            }

            var config = configResult.Value!;
            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (mode is "all" or "flows")
            {
                payload["flows"] = DiscoverFlows(projectRoot, config.Paths.Flows, flowParser);
            }

            if (mode is "all" or "steps")
            {
                payload["steps"] = DiscoverSteps(projectRoot, config.Paths.Steps, stepParser);
            }

            if (mode is "all" or "capabilities")
            {
                payload["capabilities"] = DiscoverCapabilities(projectRoot, config.Paths.Capabilities, capabilityParser);
            }

            if (mode is "all" or "fixtures")
            {
                payload["fixtures"] = DiscoverFixtures(projectRoot, config.Paths.Fixtures, fixtureParser);
            }

            if (mode is "all" or "drivers")
            {
                payload["drivers"] = config.Drivers.Select(driver => new
                {
                    name = driver.Key,
                    enabled = driver.Value.Enabled,
                    config = driver.Value.Config,
                    available = runtimeDrivers.ContainsKey(driver.Key),
                    implementation = runtimeDrivers.TryGetValue(driver.Key, out var runtimeDriver)
                        ? runtimeDriver.GetType().Name
                        : null
                }).ToList();
            }

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(payload, CommandSupport.JsonOptions));
            }
            else
            {
                WriteText(payload);
            }

            context.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            context.ExitCode = 2;
        }
    }

    private static IReadOnlyList<object> DiscoverFlows(string projectRoot, string relativePath, FlowParser parser)
    {
        var root = Path.Combine(projectRoot, relativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*.flow.yaml", SearchOption.AllDirectories)
            .Select(file => parser.ParseFile(file))
            .Where(result => result.Value is not null)
            .Select(result => new
            {
                id = result.Value!.Id,
                name = result.Value.Name,
                capability = result.Value.CapabilityId,
                file = Path.GetRelativePath(projectRoot, result.Value.SourceFile!)
            })
            .Cast<object>()
            .ToList();
    }

    private static IReadOnlyList<object> DiscoverCapabilities(string projectRoot, string relativePath, CapabilityParser parser)
    {
        var root = Path.Combine(projectRoot, relativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Select(file => parser.ParseFile(file))
            .Where(result => result.Value is not null)
            .Select(result => new
            {
                id = result.Value!.Id,
                name = result.Value.Name,
                file = Path.GetRelativePath(projectRoot, result.Value.SourceFile!)
            })
            .Cast<object>()
            .ToList();
    }

    private static IReadOnlyList<string> DiscoverRelativeFiles(string projectRoot, string relativePath)
    {
        var root = Path.Combine(projectRoot, relativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(projectRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<object> DiscoverSteps(string projectRoot, string relativePath, StepManifestParser parser)
    {
        var root = Path.Combine(projectRoot, relativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(file => parser.ParseFile(file))
            .Where(result => result.Value is not null)
            .SelectMany(result => result.Value!.Steps.Select(step => new
            {
                name = step.Name,
                description = step.Description,
                drivers = step.Drivers,
                file = Path.GetRelativePath(projectRoot, step.SourceFile!)
            }))
            .Cast<object>()
            .ToList();
    }

    private static IReadOnlyList<object> DiscoverFixtures(string projectRoot, string relativePath, FixtureManifestParser parser)
    {
        var root = Path.Combine(projectRoot, relativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(file => parser.ParseFile(file))
            .Where(result => result.Value is not null)
            .SelectMany(result => result.Value!.Fixtures.Select(fixture => new
            {
                name = fixture.Key,
                strategy = fixture.Value.Strategy,
                type = fixture.Value.Type,
                file = Path.GetRelativePath(projectRoot, fixture.Value.SourceFile!)
            }))
            .Cast<object>()
            .ToList();
    }

    private static void WriteText(IReadOnlyDictionary<string, object?> payload)
    {
        foreach (var entry in payload)
        {
            Console.Out.WriteLine($"{entry.Key}:");

            switch (entry.Value)
            {
                case IEnumerable<string> strings:
                    foreach (var item in strings)
                    {
                        Console.Out.WriteLine($"  - {item}");
                    }

                    break;
                case IEnumerable<object> objects:
                    foreach (var item in objects)
                    {
                        Console.Out.WriteLine($"  - {JsonSerializer.Serialize(item, CommandSupport.JsonOptions)}");
                    }

                    break;
                default:
                    Console.Out.WriteLine("  - none");
                    break;
            }
        }
    }
}
