using System.CommandLine;
using System.CommandLine.Invocation;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Exporters.Cypress;
using Cress.Exporters.SeleniumIde;
using Cress.Exporters.TestFrameworks;
using Cress.Gherkin;
using Cress.Gherkin.Phrases;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Cli.Commands;

public static class ExportCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("export", "Export flows to external formats");
        command.AddCommand(CreateGherkinCommand(services));
        command.AddCommand(CreateCypressCommand(services));
        command.AddCommand(CreateSeleniumIdeCommand(services));
        command.AddCommand(CreateXunitCommand(services));
        command.AddCommand(CreateNUnitCommand(services));
        command.AddCommand(CreateMsTestCommand(services));
        return command;
    }

    private static Command CreateGherkinCommand(IServiceProvider services)
    {
        var command = new Command("gherkin", "Export a flow to a Gherkin .feature file");

        var projectArgument = new Argument<string?>("project")
        {
            Description = "Path to the Cress project directory",
            Arity = ArgumentArity.ZeroOrOne
        };
        var flowOption = new Option<string?>("--flow") { Description = "Flow id to export (partial match supported)" };
        var outputOption = new Option<string?>("--output") { Description = "Output .feature file path (default: <project>/exports/<flowId>.feature)" };
        var phraseOverrideOption = new Option<string?>("--phrases") { Description = "Path to a phrases.yaml override file" };

        command.AddArgument(projectArgument);
        command.AddOption(flowOption);
        command.AddOption(outputOption);
        command.AddOption(phraseOverrideOption);

        command.SetHandler((InvocationContext context) =>
        {
            var projectPath = context.ParseResult.GetValueForArgument(projectArgument);
            var flowId = context.ParseResult.GetValueForOption(flowOption);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);
            var phrasesOverridePath = context.ParseResult.GetValueForOption(phraseOverrideOption);

            // Resolve the working directory for project discovery.
            var searchRoot = string.IsNullOrWhiteSpace(projectPath)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(projectPath);

            var catalogService = services.GetRequiredService<ProjectCatalogService>();
            var catalogResult = catalogService.Load(searchRoot);

            if (catalogResult.Value is null)
            {
                CommandSupport.WriteDiagnostics(catalogResult.Diagnostics, false);
                context.ExitCode = 1;
                return;
            }

            var catalog = catalogResult.Value;

            // Select the flow(s) to export.
            NormalizedFlow? flow = null;
            if (!string.IsNullOrWhiteSpace(flowId))
            {
                flow = catalog.NormalizedFlows.FirstOrDefault(f =>
                    string.Equals(f.FlowId, flowId, StringComparison.OrdinalIgnoreCase)
                    || f.FlowId.Contains(flowId, StringComparison.OrdinalIgnoreCase));

                if (flow is null)
                {
                    Console.Error.WriteLine($"No flow found matching '{flowId}'.");
                    Console.Error.WriteLine($"Available flows: {string.Join(", ", catalog.NormalizedFlows.Select(f => f.FlowId))}");
                    context.ExitCode = 1;
                    return;
                }
            }
            else if (catalog.NormalizedFlows.Count == 1)
            {
                flow = catalog.NormalizedFlows[0];
            }
            else if (catalog.NormalizedFlows.Count == 0)
            {
                Console.Error.WriteLine("No flows found in the project.");
                context.ExitCode = 1;
                return;
            }
            else
            {
                Console.Error.WriteLine("Multiple flows found. Specify --flow <flowId>.");
                foreach (var f in catalog.NormalizedFlows)
                {
                    Console.Error.WriteLine($"  {f.FlowId}");
                }

                context.ExitCode = 1;
                return;
            }

            // Build the phrase library.
            var effectivePhrasesPath = phrasesOverridePath
                ?? Path.Combine(catalog.ProjectRoot, ".cress", "phrases.yaml");

            var library = PhraseLibrary.CreateWithOverrides(effectivePhrasesPath);
            var converter = new FlowToGherkinConverter(library);
            var featureText = converter.Convert(flow);

            // Determine output path.
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var safeId = flow.FlowId.Replace('/', '-').Replace('\\', '-').Replace(':', '-');
                var exportsDir = Path.Combine(catalog.ProjectRoot, "exports");
                Directory.CreateDirectory(exportsDir);
                outputPath = Path.Combine(exportsDir, $"{safeId}.feature");
            }

            // Write or print.
            if (outputPath == "-")
            {
                Console.Out.Write(featureText);
            }
            else
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(outputPath, featureText);
                Console.Out.WriteLine($"Exported: {outputPath}");
            }

            context.ExitCode = 0;
        });

        return command;
    }

    private static Command CreateCypressCommand(IServiceProvider services)
    {
        var command = new Command("cypress", "Export a flow to a Cypress .cy.ts test file");

        var projectArgument = new Argument<string?>("project")
        {
            Description = "Path to the Cress project directory",
            Arity = ArgumentArity.ZeroOrOne
        };
        var flowOption = new Option<string?>("--flow") { Description = "Flow id to export (partial match supported)" };
        var outputOption = new Option<string?>("--output") { Description = "Output .cy.ts file path (default: <project>/exports/<flowId>.cy.ts)" };

        command.AddArgument(projectArgument);
        command.AddOption(flowOption);
        command.AddOption(outputOption);

        command.SetHandler((InvocationContext context) =>
        {
            var projectPath = context.ParseResult.GetValueForArgument(projectArgument);
            var flowId = context.ParseResult.GetValueForOption(flowOption);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);

            var flow = ResolveFlow(services, projectPath, flowId, context, out var catalog);
            if (flow is null)
            {
                return;
            }

            // Export
            var exporter = new CypressExporter();
            var tsText = exporter.Export(flow);

            // Determine output path
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var safeId = flow.FlowId.Replace('/', '-').Replace('\\', '-').Replace(':', '-');
                var exportsDir = Path.Combine(catalog!.ProjectRoot, "exports");
                Directory.CreateDirectory(exportsDir);
                outputPath = Path.Combine(exportsDir, $"{safeId}.cy.ts");
            }

            WriteOrPrint(outputPath, tsText);
            context.ExitCode = 0;
        });

        return command;
    }

    private static Command CreateSeleniumIdeCommand(IServiceProvider services)
    {
        var command = new Command("selenium-ide", "Export a flow to a Selenium IDE .side JSON file");

        var projectArgument = new Argument<string?>("project")
        {
            Description = "Path to the Cress project directory",
            Arity = ArgumentArity.ZeroOrOne
        };
        var flowOption = new Option<string?>("--flow") { Description = "Flow id to export (partial match supported)" };
        var outputOption = new Option<string?>("--output") { Description = "Output .side file path (default: <project>/exports/<flowId>.side)" };

        command.AddArgument(projectArgument);
        command.AddOption(flowOption);
        command.AddOption(outputOption);

        command.SetHandler((InvocationContext context) =>
        {
            var projectPath = context.ParseResult.GetValueForArgument(projectArgument);
            var flowId = context.ParseResult.GetValueForOption(flowOption);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);

            var flow = ResolveFlow(services, projectPath, flowId, context, out var catalog);
            if (flow is null)
            {
                return;
            }

            // Export
            var exporter = new SeleniumIdeExporter();
            var sideJson = exporter.Export(flow);

            // Determine output path
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var safeId = flow.FlowId.Replace('/', '-').Replace('\\', '-').Replace(':', '-');
                var exportsDir = Path.Combine(catalog!.ProjectRoot, "exports");
                Directory.CreateDirectory(exportsDir);
                outputPath = Path.Combine(exportsDir, $"{safeId}.side");
            }

            WriteOrPrint(outputPath, sideJson);
            context.ExitCode = 0;
        });

        return command;
    }

    private static Command CreateXunitCommand(IServiceProvider services)
    {
        var command = new Command("xunit", "Export a flow to an xUnit C# test backed by the Cress engine");
        ConfigureDotNetTestExportCommand(
            services,
            command,
            defaultExtension: ".xunit.cs",
            defaultNamespace: "Cress.Generated.Xunit",
            export: (flow, catalog, options) => new XunitExporter().Export(flow, catalog.ProjectRoot, options));
        return command;
    }

    private static Command CreateNUnitCommand(IServiceProvider services)
    {
        var command = new Command("nunit", "Export a flow to an NUnit C# test backed by the Cress engine");
        ConfigureDotNetTestExportCommand(
            services,
            command,
            defaultExtension: ".nunit.cs",
            defaultNamespace: "Cress.Generated.NUnit",
            export: (flow, catalog, options) => new NUnitExporter().Export(flow, catalog.ProjectRoot, options));
        return command;
    }

    private static Command CreateMsTestCommand(IServiceProvider services)
    {
        var command = new Command("mstest", "Export a flow to an MSTest C# test backed by the Cress engine");
        ConfigureDotNetTestExportCommand(
            services,
            command,
            defaultExtension: ".mstest.cs",
            defaultNamespace: "Cress.Generated.MSTest",
            export: (flow, catalog, options) => new MsTestExporter().Export(flow, catalog.ProjectRoot, options));
        return command;
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolve a single <see cref="NormalizedFlow"/> from the project catalog.
    /// Sets <paramref name="catalog"/> on success (needed for default output path).
    /// Returns <see langword="null"/> and sets exit code on failure.
    /// </summary>
    private static NormalizedFlow? ResolveFlow(
        IServiceProvider services,
        string? projectPath,
        string? flowId,
        InvocationContext context,
        out ProjectCatalog? catalog)
    {
        catalog = null;

        var searchRoot = string.IsNullOrWhiteSpace(projectPath)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(projectPath);

        var catalogService = services.GetRequiredService<ProjectCatalogService>();
        var catalogResult = catalogService.Load(searchRoot);

        if (catalogResult.Value is null)
        {
            CommandSupport.WriteDiagnostics(catalogResult.Diagnostics, false);
            context.ExitCode = 1;
            return null;
        }

        catalog = catalogResult.Value;

        NormalizedFlow? flow = null;
        if (!string.IsNullOrWhiteSpace(flowId))
        {
            flow = catalog.NormalizedFlows.FirstOrDefault(f =>
                string.Equals(f.FlowId, flowId, StringComparison.OrdinalIgnoreCase)
                || f.FlowId.Contains(flowId, StringComparison.OrdinalIgnoreCase));

            if (flow is null)
            {
                Console.Error.WriteLine($"No flow found matching '{flowId}'.");
                Console.Error.WriteLine($"Available flows: {string.Join(", ", catalog.NormalizedFlows.Select(f => f.FlowId))}");
                context.ExitCode = 1;
                return null;
            }
        }
        else if (catalog.NormalizedFlows.Count == 1)
        {
            flow = catalog.NormalizedFlows[0];
        }
        else if (catalog.NormalizedFlows.Count == 0)
        {
            Console.Error.WriteLine("No flows found in the project.");
            context.ExitCode = 1;
            return null;
        }
        else
        {
            Console.Error.WriteLine("Multiple flows found. Specify --flow <flowId>.");
            foreach (var f in catalog.NormalizedFlows)
            {
                Console.Error.WriteLine($"  {f.FlowId}");
            }

            context.ExitCode = 1;
            return null;
        }

        return flow;
    }

    private static void WriteOrPrint(string outputPath, string content)
    {
        if (outputPath == "-")
        {
            Console.Out.Write(content);
        }
        else
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(outputPath, content);
            Console.Out.WriteLine($"Exported: {outputPath}");
        }
    }

    private static void ConfigureDotNetTestExportCommand(
        IServiceProvider services,
        Command command,
        string defaultExtension,
        string defaultNamespace,
        Func<NormalizedFlow, ProjectCatalog, DotNetTestExportOptions, string> export)
    {
        var projectArgument = new Argument<string?>("project")
        {
            Description = "Path to the Cress project directory",
            Arity = ArgumentArity.ZeroOrOne
        };
        var flowOption = new Option<string?>("--flow") { Description = "Flow id to export (partial match supported)" };
        var outputOption = new Option<string?>("--output") { Description = $"Output {defaultExtension} file path (default: <project>/exports/<flowId>{defaultExtension})" };
        var namespaceOption = new Option<string?>("--namespace") { Description = $"Namespace for the generated test class (default: {defaultNamespace})" };
        var classNameOption = new Option<string?>("--class-name") { Description = "Class name for the generated test type" };
        var profileOption = new Option<string?>("--profile") { Description = "Optional profile to embed in the generated test" };

        command.AddArgument(projectArgument);
        command.AddOption(flowOption);
        command.AddOption(outputOption);
        command.AddOption(namespaceOption);
        command.AddOption(classNameOption);
        command.AddOption(profileOption);

        command.SetHandler((InvocationContext context) =>
        {
            var projectPath = context.ParseResult.GetValueForArgument(projectArgument);
            var flowId = context.ParseResult.GetValueForOption(flowOption);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);
            var namespaceValue = context.ParseResult.GetValueForOption(namespaceOption) ?? defaultNamespace;
            var className = context.ParseResult.GetValueForOption(classNameOption);
            var profile = context.ParseResult.GetValueForOption(profileOption);

            var flow = ResolveFlow(services, projectPath, flowId, context, out var catalog);
            if (flow is null || catalog is null)
            {
                return;
            }

            var projectLiteral = DetermineProjectPathLiteral(projectPath, catalog.ProjectRoot);
            var content = export(flow, catalog, new DotNetTestExportOptions
            {
                Namespace = namespaceValue,
                ClassName = className,
                Profile = profile,
                ProjectPath = projectLiteral
            });

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var safeId = flow.FlowId.Replace('/', '-').Replace('\\', '-').Replace(':', '-');
                var exportsDir = Path.Combine(catalog.ProjectRoot, "exports");
                Directory.CreateDirectory(exportsDir);
                outputPath = Path.Combine(exportsDir, $"{safeId}{defaultExtension}");
            }

            WriteOrPrint(outputPath, content);
            context.ExitCode = 0;
        });
    }

    private static string DetermineProjectPathLiteral(string? projectPath, string projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            return projectPath;
        }

        var repositoryRoot = FindRepositoryRoot(projectRoot);
        return repositoryRoot is null
            ? Path.GetFileName(projectRoot)
            : Path.GetRelativePath(repositoryRoot, projectRoot);
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
