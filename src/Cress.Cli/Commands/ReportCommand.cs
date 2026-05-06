using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text.Json;
using Cress.Execution;
using Cress.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Cli.Commands;

public static class ReportCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("report", "Work with generated reports");
        command.AddCommand(CreateListCommand(services));
        command.AddCommand(CreateOpenCommand(services));
        command.AddCommand(CreateSummarizeCommand(services));
        return command;
    }

    private static Command CreateListCommand(IServiceProvider services)
    {
        var command = new Command("list", "List recent reports");
        var jsonOption = new Option<bool>("--json") { Description = "Emit machine-readable output" };
        command.AddOption(jsonOption);
        command.SetHandler((InvocationContext context) =>
        {
            if (!TryGetProject(services, out var projectRoot, out var reportsPath))
            {
                context.ExitCode = 1;
                return;
            }

            var generator = services.GetRequiredService<ReportGenerator>();
            var reports = generator.ListReports(projectRoot!, reportsPath!);
            if (context.ParseResult.GetValueForOption(jsonOption))
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(reports, CommandSupport.JsonOptions));
            }
            else
            {
                foreach (var report in reports)
                {
                    Console.Out.WriteLine(report);
                }
            }

            context.ExitCode = 0;
        });
        return command;
    }

    private static Command CreateOpenCommand(IServiceProvider services)
    {
        var command = new Command("open", "Open the latest HTML report");
        command.SetHandler((InvocationContext context) =>
        {
            if (!TryGetProject(services, out var projectRoot, out var reportsPath))
            {
                context.ExitCode = 1;
                return;
            }

            var generator = services.GetRequiredService<ReportGenerator>();
            var latest = generator.ListReports(projectRoot!, reportsPath!).FirstOrDefault();
            if (latest is null)
            {
                Console.Error.WriteLine("No reports were found.");
                context.ExitCode = 1;
                return;
            }

            var path = Path.Combine(latest, "report.html");
            Console.Out.WriteLine(path);
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
            }

            context.ExitCode = File.Exists(path) ? 0 : 1;
        });
        return command;
    }

    private static Command CreateSummarizeCommand(IServiceProvider services)
    {
        var command = new Command("summarize", "Summarize a selected run");
        var pathArgument = new Argument<string?>("path")
        {
            Description = "Run artifact or report directory",
            Arity = ArgumentArity.ZeroOrOne
        };
        var jsonOption = new Option<bool>("--json") { Description = "Emit machine-readable output" };
        command.AddArgument(pathArgument);
        command.AddOption(jsonOption);
        command.SetHandler((InvocationContext context) =>
        {
            string? targetPath = context.ParseResult.GetValueForArgument(pathArgument);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                if (!TryGetProject(services, out var projectRoot, out var reportsPath))
                {
                    context.ExitCode = 1;
                    return;
                }

                targetPath = services.GetRequiredService<ReportGenerator>().ListReports(projectRoot!, reportsPath!).FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                Console.Error.WriteLine("No report or run path was supplied.");
                context.ExitCode = 1;
                return;
            }

            var fullPath = Path.GetFullPath(targetPath);
            var jsonFile = Directory.Exists(fullPath)
                ? new[] { Path.Combine(fullPath, "report.json"), Path.Combine(fullPath, "result.json") }.FirstOrDefault(File.Exists)
                : File.Exists(fullPath)
                    ? fullPath
                    : null;
            if (jsonFile is null)
            {
                Console.Error.WriteLine("Could not locate report.json or result.json.");
                context.ExitCode = 1;
                return;
            }

            var content = File.ReadAllText(jsonFile);
            if (context.ParseResult.GetValueForOption(jsonOption))
            {
                Console.Out.WriteLine(content);
            }
            else
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;
                Console.Out.WriteLine($"Run ID: {root.GetProperty("metadata").GetProperty("runId").GetString()}");
                Console.Out.WriteLine($"Profile: {root.GetProperty("metadata").GetProperty("profile").GetString()}");
                Console.Out.WriteLine($"Flows: {root.GetProperty("flows").GetArrayLength()}");
            }

            context.ExitCode = 0;
        });
        return command;
    }

    private static bool TryGetProject(IServiceProvider services, out string? projectRoot, out string? reportsPath)
    {
        var locator = services.GetRequiredService<ProjectLocator>();
        var configLoader = services.GetRequiredService<ConfigLoader>();
        projectRoot = locator.FindProjectRoot(Environment.CurrentDirectory);
        reportsPath = null;
        if (projectRoot is null)
        {
            Console.Error.WriteLine("Could not locate a Cress project root.");
            return false;
        }

        var config = configLoader.Load(projectRoot);
        if (config.Value is null)
        {
            CommandSupport.WriteDiagnostics(config.Diagnostics, false);
            return false;
        }

        reportsPath = config.Value.Paths.Reports;
        return true;
    }
}
