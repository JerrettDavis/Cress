using Cress.Core.Models;
using Cress.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Cress.Cli.Commands;

public static class ConfigCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("config", "Work with project configuration");
        command.AddCommand(CreatePrintCommand(services));
        return command;
    }

    private static Command CreatePrintCommand(IServiceProvider services)
    {
        var command = new Command("print", "Print effective configuration");
        var profileOption = new Option<string?>("--profile", "Profile to load");

        command.AddOption(profileOption);
        command.SetHandler((InvocationContext context) =>
        {
            var locator = services.GetRequiredService<ProjectLocator>();
            var configLoader = services.GetRequiredService<ConfigLoader>();
            var profileLoader = services.GetRequiredService<ProfileLoader>();
            var projectRoot = locator.FindProjectRoot(Environment.CurrentDirectory);

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
                ], false);

                context.ExitCode = 1;
                return;
            }

            var configResult = configLoader.Load(projectRoot);
            var diagnostics = new List<Diagnostic>(configResult.Diagnostics);
            if (!configResult.Success)
            {
                CommandSupport.WriteDiagnostics(diagnostics, false);
                context.ExitCode = 1;
                return;
            }

            var config = configResult.Value!;
            var requestedProfile = context.ParseResult.GetValueForOption(profileOption);
            var profileResult = profileLoader.LoadActive(projectRoot, config, requestedProfile);
            diagnostics.AddRange(profileResult.Diagnostics);

            if (!profileResult.Success)
            {
                CommandSupport.WriteDiagnostics(diagnostics, false);
                context.ExitCode = 1;
                return;
            }

            var effective = new EffectiveConfig
            {
                Config = config,
                ActiveProfile = profileResult.Value!.Profile,
                Profile = profileResult.Value!
            };

            Console.Out.Write(configLoader.Serialize(effective));
            context.ExitCode = 0;
        });

        return command;
    }
}
