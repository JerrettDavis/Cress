using System.CommandLine;
using System.CommandLine.Invocation;
using Cress.Core.Models;
using Cress.Gherkin;
using Cress.Gherkin.Phrases;
using Cress.Importers.Playwright;
using Cress.Importers.Postman;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Cli.Commands;

public static class ImportCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("import", "Import flows from external formats");
        command.AddCommand(CreateGherkinCommand());
        command.AddCommand(CreatePlaywrightCommand());
        command.AddCommand(CreatePostmanCommand());
        return command;
    }

    private static Command CreateGherkinCommand()
    {
        var command = new Command("gherkin", "Import a Gherkin .feature file and emit a flow.yaml");

        var featureFileArgument = new Argument<string>("feature-file")
        {
            Description = "Path to the .feature file to import"
        };
        var outputOption = new Option<string?>("--output") { Description = "Output flow.yaml path (default: <feature-name>.flow.yaml alongside the input file)" };
        var phraseOverrideOption = new Option<string?>("--phrases") { Description = "Path to a phrases.yaml override file" };

        command.AddArgument(featureFileArgument);
        command.AddOption(outputOption);
        command.AddOption(phraseOverrideOption);

        command.SetHandler((InvocationContext context) =>
        {
            var featureFilePath = context.ParseResult.GetValueForArgument(featureFileArgument);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);
            var phrasesOverridePath = context.ParseResult.GetValueForOption(phraseOverrideOption);

            if (!File.Exists(featureFilePath))
            {
                Console.Error.WriteLine($"Feature file not found: {featureFilePath}");
                context.ExitCode = 1;
                return;
            }

            // Build phrase library
            var effectivePhrasesPath = phrasesOverridePath
                ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(featureFilePath))!, ".cress", "phrases.yaml");

            var library = PhraseLibrary.CreateWithOverrides(effectivePhrasesPath);
            var ingester = new GherkinIngester(library);

            // Ingest
            var featureText = File.ReadAllText(featureFilePath);
            CressFlow flow;
            try
            {
                flow = ingester.Ingest(featureText);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to ingest feature file: {ex.Message}");
                context.ExitCode = 1;
                return;
            }

            // Determine output path
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var baseName = Path.GetFileNameWithoutExtension(featureFilePath);
                var dir = Path.GetDirectoryName(Path.GetFullPath(featureFilePath))!;
                outputPath = Path.Combine(dir, $"{baseName}.flow.yaml");
            }

            // Serialize to YAML
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
                .Build();

            var yaml = serializer.Serialize(flow);

            // Write or print
            if (outputPath == "-")
            {
                Console.Out.Write(yaml);
            }
            else
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(outputPath, yaml);
                Console.Out.WriteLine($"Imported: {outputPath}");
            }

            context.ExitCode = 0;
        });

        return command;
    }

    private static Command CreatePlaywrightCommand()
    {
        var command = new Command("playwright", "Import a Playwright codegen .ts/.js file and emit a flow.yaml");

        var codegenFileArgument = new Argument<string>("codegen-file")
        {
            Description = "Path to the Playwright codegen .ts or .js file to import"
        };
        var outputOption = new Option<string?>("--output") { Description = "Output flow.yaml path (default: <codegen-name>.flow.yaml alongside the input file)" };
        var nameOption = new Option<string?>("--name") { Description = "Override the flow name (default: derived from test('...') in the file)" };

        command.AddArgument(codegenFileArgument);
        command.AddOption(outputOption);
        command.AddOption(nameOption);

        command.SetHandler((InvocationContext context) =>
        {
            var codegenFilePath = context.ParseResult.GetValueForArgument(codegenFileArgument);
            var outputPath = context.ParseResult.GetValueForOption(outputOption);
            var flowNameOverride = context.ParseResult.GetValueForOption(nameOption);

            if (!File.Exists(codegenFilePath))
            {
                Console.Error.WriteLine($"Codegen file not found: {codegenFilePath}");
                context.ExitCode = 1;
                return;
            }

            var importer = new PlaywrightCodegenImporter();
            var codegenText = File.ReadAllText(codegenFilePath);

            CressFlow flow;
            try
            {
                flow = importer.Import(codegenText, flowNameOverride);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to import Playwright codegen file: {ex.Message}");
                context.ExitCode = 1;
                return;
            }

            // Determine output path
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var baseName = Path.GetFileNameWithoutExtension(codegenFilePath);
                // Strip trailing .spec if present (e.g. login.spec.ts → login.flow.yaml)
                if (baseName.EndsWith(".spec", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName[..^".spec".Length];
                }

                var dir = Path.GetDirectoryName(Path.GetFullPath(codegenFilePath))!;
                outputPath = Path.Combine(dir, $"{baseName}.flow.yaml");
            }

            // Serialize to YAML
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
                .Build();

            var yaml = serializer.Serialize(flow);

            // Write or print
            if (outputPath == "-")
            {
                Console.Out.Write(yaml);
            }
            else
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(outputPath, yaml);
                Console.Out.WriteLine($"Imported: {outputPath}");
            }

            context.ExitCode = 0;
        });

        return command;
    }

    private static Command CreatePostmanCommand()
    {
        var command = new Command("postman", "Import a Postman Collection v2.1 JSON file and emit flow.yaml files");

        var collectionArgument = new Argument<string>("collection")
        {
            Description = "Path to the Postman Collection v2.1 JSON file"
        };
        var outputOption = new Option<string?>("--output") { Description = "Output directory for generated flow.yaml files (default: directory of the input file)" };
        var singleFlowOption = new Option<bool>("--single-flow") { Description = "Combine all requests into a single flow.yaml instead of one file per request" };

        command.AddArgument(collectionArgument);
        command.AddOption(outputOption);
        command.AddOption(singleFlowOption);

        command.SetHandler((InvocationContext context) =>
        {
            var collectionPath = context.ParseResult.GetValueForArgument(collectionArgument);
            var outputDir = context.ParseResult.GetValueForOption(outputOption);
            var singleFlow = context.ParseResult.GetValueForOption(singleFlowOption);

            if (!File.Exists(collectionPath))
            {
                Console.Error.WriteLine($"Collection file not found: {collectionPath}");
                context.ExitCode = 1;
                return;
            }

            var importer = new PostmanImporter();
            var json = File.ReadAllText(collectionPath);

            IReadOnlyList<CressFlow> flows;
            try
            {
                flows = importer.Import(json, singleFlow);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to import Postman collection: {ex.Message}");
                context.ExitCode = 1;
                return;
            }

            if (flows.Count == 0)
            {
                Console.Out.WriteLine("No requests found in the collection.");
                context.ExitCode = 0;
                return;
            }

            // Determine output directory
            var effectiveOutputDir = string.IsNullOrWhiteSpace(outputDir)
                ? Path.GetDirectoryName(Path.GetFullPath(collectionPath))!
                : outputDir;

            Directory.CreateDirectory(effectiveOutputDir);

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
                .Build();

            foreach (var flow in flows)
            {
                var fileName = $"{flow.Id}.flow.yaml";
                var outputPath = Path.Combine(effectiveOutputDir, fileName);
                var yaml = serializer.Serialize(flow);
                File.WriteAllText(outputPath, yaml);
                Console.Out.WriteLine($"Imported: {outputPath}");
            }

            context.ExitCode = 0;
        });

        return command;
    }
}
