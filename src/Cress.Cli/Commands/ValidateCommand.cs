using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Validation;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Cress.Cli.Commands;

public static class ValidateCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("validate", "Validate the current Cress project");
        var jsonOption = new Option<bool>("--json") { Description = "Emit machine-readable output" };
        var strictOption = new Option<bool>("--strict") { Description = "Treat stricter validation rules as errors" };

        command.AddOption(jsonOption);
        command.AddOption(strictOption);
        command.SetHandler((InvocationContext context) =>
        {
            try
            {
                var validator = services.GetRequiredService<ProjectValidator>();
                var locator = services.GetRequiredService<ProjectLocator>();
                var catalogService = services.GetRequiredService<ProjectCatalogService>();
                var runtimeDrivers = services.GetServices<IRuntimeDriver>().ToDictionary(driver => driver.Name, StringComparer.OrdinalIgnoreCase);
                var json = context.ParseResult.GetValueForOption(jsonOption);
                var strict = context.ParseResult.GetValueForOption(strictOption);
                var result = validator.Validate(Environment.CurrentDirectory, strict);
                var diagnostics = result.Diagnostics.ToList();
                var projectRoot = locator.FindProjectRoot(Environment.CurrentDirectory);

                if (projectRoot is not null && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
                {
                    var catalogResult = catalogService.Load(projectRoot);
                    diagnostics.AddRange(catalogResult.Diagnostics);

                    if (catalogResult.Value is not null)
                    {
                        foreach (var driver in catalogResult.Value.EffectiveConfig.Config.Drivers.Where(entry => entry.Value.Enabled))
                        {
                            if (runtimeDrivers.TryGetValue(driver.Key, out var runtimeDriver))
                            {
                                diagnostics.AddRange(runtimeDriver.HealthCheck(catalogResult.Value));
                            }
                            else
                            {
                                diagnostics.Add(new Diagnostic
                                {
                                    Severity = DiagnosticSeverity.Error,
                                    Code = "DRV900",
                                    Message = $"Driver '{driver.Key}' is enabled but no runtime implementation is registered.",
                                    File = Path.Combine(projectRoot, ".cress", "config.yaml")
                                });
                            }
                        }
                    }
                }

                if (!json && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
                {
                    Console.Out.WriteLine("Validation succeeded.");
                }

                CommandSupport.WriteDiagnostics(diagnostics, json);
                context.ExitCode = CommandSupport.GetExitCode(diagnostics);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error: {ex.Message}");
                context.ExitCode = 2;
            }
        });

        return command;
    }
}
