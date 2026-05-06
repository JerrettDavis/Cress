using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Cress.Cli.Commands;

public static class DoctorCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("doctor", "Check environment readiness");
        var jsonOption = new Option<bool>("--json") { Description = "Emit machine-readable output" };

        command.AddOption(jsonOption);
        command.SetHandler((InvocationContext context) =>
        {
            try
            {
                var locator = services.GetRequiredService<ProjectLocator>();
                var configLoader = services.GetRequiredService<ConfigLoader>();
                var catalogService = services.GetRequiredService<ProjectCatalogService>();
                var pluginHost = services.GetRequiredService<PluginHost>();
                var runtimeDrivers = services.GetServices<IRuntimeDriver>().ToDictionary(driver => driver.Name, StringComparer.OrdinalIgnoreCase);
                var json = context.ParseResult.GetValueForOption(jsonOption);
                var diagnostics = new List<Diagnostic>();
                var projectRoot = locator.FindProjectRoot(Environment.CurrentDirectory);

                if (projectRoot is null)
                {
                    diagnostics.Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "DOC001",
                        Message = "Cress project is not initialized in this directory tree.",
                        File = Environment.CurrentDirectory,
                        Details = "Run `cress init` to create a project."
                    });

                    CommandSupport.WriteDiagnostics(diagnostics, json);
                    context.ExitCode = 1;
                    return;
                }

                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Info,
                    Code = "DOC002",
                    Message = "Project root found.",
                    File = projectRoot
                });

                var configResult = configLoader.Load(projectRoot);
                diagnostics.AddRange(configResult.Diagnostics);
                var catalogResult = catalogService.Load(projectRoot);
                diagnostics.AddRange(catalogResult.Diagnostics);

                if (configResult.Value is not null)
                {
                    foreach (var driver in configResult.Value.Drivers.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = driver.Value.Enabled ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                            Code = "DOC003",
                            Message = driver.Value.Enabled
                                ? $"Driver '{driver.Key}' is configured and enabled."
                                : $"Driver '{driver.Key}' is configured but disabled.",
                            File = Path.Combine(projectRoot, ".cress", "config.yaml"),
                            Details = driver.Value.Enabled
                                ? $"You can add assets for the '{driver.Key}' driver."
                                : $"Enable '{driver.Key}' under drivers in .cress\\config.yaml when you are ready."
                        });

                        if (driver.Value.Enabled && runtimeDrivers.TryGetValue(driver.Key, out var runtimeDriver) && catalogResult.Value is not null)
                        {
                            diagnostics.AddRange(runtimeDriver.HealthCheck(catalogResult.Value));
                        }
                    }
                }

                if (catalogResult.Value is not null)
                {
                    foreach (var plugin in catalogResult.Value.StepRegistry.Definitions.Values
                                 .Select(step => step.Implementation?.Plugin)
                                 .Where(plugin => !string.IsNullOrWhiteSpace(plugin) && !plugin!.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        diagnostics.AddRange(pluginHost.Probe(catalogResult.Value, plugin!));
                    }
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
