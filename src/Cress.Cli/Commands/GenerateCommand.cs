using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Cress.Core.Models;
using Cress.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Cli.Commands;

public static class GenerateCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("generate", "Generate project assets");
        command.AddCommand(CreateStepsCommand(services));
        return command;
    }

    private static Command CreateStepsCommand(IServiceProvider services)
    {
        var command = new Command("steps", "Generate missing step stubs");
        var pathArgument = new Argument<string?>("flow", () => null, "Optional flow path");
        var tagOption = new Option<string?>("--tag", "Select flows by tag");
        var profileOption = new Option<string?>("--profile", "Profile to use");
        var languageOption = new Option<string>("--language", () => "dotnet", "Target language");
        var forceOption = new Option<bool>("--force", "Overwrite existing generated files");
        var jsonOption = new Option<bool>("--json", "Emit machine-readable output");
        command.AddArgument(pathArgument);
        command.AddOption(tagOption);
        command.AddOption(profileOption);
        command.AddOption(languageOption);
        command.AddOption(forceOption);
        command.AddOption(jsonOption);
        command.SetHandler((InvocationContext context) =>
        {
            var catalogService = services.GetRequiredService<ProjectCatalogService>();
            var generator = services.GetRequiredService<StepStubGenerator>();
            var diagnostics = new List<Diagnostic>();
            var catalog = catalogService.Load(Environment.CurrentDirectory, context.ParseResult.GetValueForOption(profileOption));
            diagnostics.AddRange(catalog.Diagnostics);
            if (catalog.Value is null)
            {
                CommandSupport.WriteDiagnostics(diagnostics, context.ParseResult.GetValueForOption(jsonOption));
                context.ExitCode = 1;
                return;
            }

            var flows = catalogService.SelectFlows(catalog.Value, context.ParseResult.GetValueForArgument(pathArgument), context.ParseResult.GetValueForOption(tagOption), diagnostics);
            var result = generator.Generate(catalog.Value, flows, context.ParseResult.GetValueForOption(languageOption) ?? "dotnet", context.ParseResult.GetValueForOption(forceOption));
            diagnostics.AddRange(result.Diagnostics);
            if (context.ParseResult.GetValueForOption(jsonOption))
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new
                {
                    success = diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
                    generated = result.Value,
                    diagnostics
                }, CommandSupport.JsonOptions));
            }
            else if (result.Value is not null)
            {
                Console.Out.WriteLine($"Generated {result.Value.Files.Count} file(s) for {result.Value.Steps.Count} missing step(s).");
                foreach (var file in result.Value.Files)
                {
                    Console.Out.WriteLine(file);
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
