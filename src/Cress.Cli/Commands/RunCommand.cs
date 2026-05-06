using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Cress.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Cli.Commands;

public static class RunCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("run", "Run one or more flows");
        var pathArgument = new Argument<string?>("flow", () => null, "Optional flow path");
        var tagOption = new Option<string?>("--tag", "Select flows by tag");
        var profileOption = new Option<string?>("--profile", "Profile to use");
        var parallelOption = new Option<int?>("--parallel", "Maximum parallel flows");
        var reportOption = new Option<string?>("--report", "Comma-separated report formats");
        var continueOption = new Option<bool>("--continue-on-failure", "Continue running flows after a failure");
        var dryRunOption = new Option<bool>("--dry-run", "Generate the plan and stop");
        var jsonOption = new Option<bool>("--json", "Emit machine-readable output");
        command.AddArgument(pathArgument);
        command.AddOption(tagOption);
        command.AddOption(profileOption);
        command.AddOption(parallelOption);
        command.AddOption(reportOption);
        command.AddOption(continueOption);
        command.AddOption(dryRunOption);
        command.AddOption(jsonOption);
        command.SetHandler(async (InvocationContext context) =>
        {
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            if (dryRun)
            {
                var planCommand = PlanCommand.Create(services);
                var args = new List<string>();
                var flow = context.ParseResult.GetValueForArgument(pathArgument);
                if (!string.IsNullOrWhiteSpace(flow))
                {
                    args.Add(flow);
                }

                if (!string.IsNullOrWhiteSpace(context.ParseResult.GetValueForOption(tagOption)))
                {
                    args.Add("--tag");
                    args.Add(context.ParseResult.GetValueForOption(tagOption)!);
                }

                if (!string.IsNullOrWhiteSpace(context.ParseResult.GetValueForOption(profileOption)))
                {
                    args.Add("--profile");
                    args.Add(context.ParseResult.GetValueForOption(profileOption)!);
                }

                args.Add("--json");
                context.ExitCode = await planCommand.InvokeAsync(args.ToArray());
                return;
            }

            var orchestrator = services.GetRequiredService<RuntimeOrchestrator>();
            var reportFormats = ParseCsv(context.ParseResult.GetValueForOption(reportOption));
            var result = await orchestrator.ExecuteAsync(Environment.CurrentDirectory, new Cress.Core.Models.RunOptions
            {
                FlowPath = context.ParseResult.GetValueForArgument(pathArgument),
                Tag = context.ParseResult.GetValueForOption(tagOption),
                Profile = context.ParseResult.GetValueForOption(profileOption),
                Parallel = context.ParseResult.GetValueForOption(parallelOption),
                ContinueOnFailure = context.ParseResult.GetValueForOption(continueOption),
                ReportFormats = reportFormats
            });

            if (context.ParseResult.GetValueForOption(jsonOption))
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(result, CommandSupport.JsonOptions));
            }
            else
            {
                Console.Out.WriteLine($"Run ID: {result.Metadata.RunId}");
                Console.Out.WriteLine($"Artifact path: {result.Metadata.ArtifactRoot}");
                Console.Out.WriteLine($"Passed: {result.Flows.Count(flowResult => flowResult.Outcome == Cress.Core.Models.RunOutcome.Passed)}");
                Console.Out.WriteLine($"Failed: {result.Flows.Count(flowResult => flowResult.Outcome != Cress.Core.Models.RunOutcome.Passed)}");
                foreach (var report in result.Reports)
                {
                    Console.Out.WriteLine($"{report.Key}: {report.Value}");
                }
            }

            if (result.Diagnostics.Count > 0)
            {
                CommandSupport.WriteDiagnostics(result.Diagnostics, false);
            }

            context.ExitCode = result.Passed ? 0 : 1;
        });

        return command;
    }

    private static IReadOnlyList<string> ParseCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
