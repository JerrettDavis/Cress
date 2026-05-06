using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Cress.Core.Models;
using Cress.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Cli.Commands;

public static class PlanCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("plan", "Generate an execution plan without running it");
        var pathArgument = new Argument<string?>("flow")
        {
            Description = "Optional flow path",
            Arity = ArgumentArity.ZeroOrOne
        };
        var tagOption = new Option<string?>("--tag") { Description = "Select flows by tag" };
        var profileOption = new Option<string?>("--profile") { Description = "Profile to use" };
        var outputOption = new Option<FileInfo?>("--output") { Description = "Write the plan JSON to a file" };
        var jsonOption = new Option<bool>("--json") { Description = "Emit JSON to stdout" };
        command.AddArgument(pathArgument);
        command.AddOption(tagOption);
        command.AddOption(profileOption);
        command.AddOption(outputOption);
        command.AddOption(jsonOption);
        command.SetHandler((InvocationContext context) =>
        {
            var catalogService = services.GetRequiredService<ProjectCatalogService>();
            var planGenerator = services.GetRequiredService<PlanGenerator>();
            var diagnostics = new List<Diagnostic>();
            var catalogResult = catalogService.Load(Environment.CurrentDirectory, context.ParseResult.GetValueForOption(profileOption));
            diagnostics.AddRange(catalogResult.Diagnostics);
            if (catalogResult.Value is null)
            {
                CommandSupport.WriteDiagnostics(diagnostics, context.ParseResult.GetValueForOption(jsonOption));
                context.ExitCode = 1;
                return;
            }

            var flows = catalogService.SelectFlows(
                catalogResult.Value,
                context.ParseResult.GetValueForArgument(pathArgument),
                context.ParseResult.GetValueForOption(tagOption),
                diagnostics);
            var plans = planGenerator.Generate(catalogResult.Value, flows);
            diagnostics.AddRange(plans.Diagnostics);
            var payload = new
            {
                success = diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
                plans = plans.Plans,
                diagnostics
            };

            var output = JsonSerializer.Serialize(payload, CommandSupport.JsonOptions);
            var outputFile = context.ParseResult.GetValueForOption(outputOption);
            if (outputFile is not null)
            {
                Directory.CreateDirectory(outputFile.DirectoryName!);
                File.WriteAllText(outputFile.FullName, output);
            }

            if (context.ParseResult.GetValueForOption(jsonOption) || outputFile is null)
            {
                if (context.ParseResult.GetValueForOption(jsonOption))
                {
                    Console.Out.WriteLine(output);
                }
                else
                {
                    foreach (var plan in plans.Plans)
                    {
                        Console.Out.WriteLine($"{plan.FlowId}:");
                        foreach (var action in plan.Actions)
                        {
                            Console.Out.WriteLine($"  - {action.Kind}: {action.Name} {(string.IsNullOrWhiteSpace(action.Driver) ? string.Empty : $"[{action.Driver}]")}".TrimEnd());
                        }
                    }
                }
            }

            if (diagnostics.Count > 0)
            {
                CommandSupport.WriteDiagnostics(diagnostics, false);
            }

            context.ExitCode = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? 1 : 0;
        });

        return command;
    }
}
