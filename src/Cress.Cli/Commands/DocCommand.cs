using System.CommandLine;
using System.CommandLine.Invocation;
using Cress.Execution;
using Cress.LivingDocs;
using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Studio.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Cli.Commands;

public static class DocCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("doc", "Living documentation commands");
        command.AddCommand(CreateGenerateCommand(services));
        return command;
    }

    private static Command CreateGenerateCommand(IServiceProvider services)
    {
        var command = new Command("generate", "Generate a living doc HTML page for a project");
        var projectArgument = new Argument<string?>("project")
        {
            Description = "Path to the Cress project",
            Arity = ArgumentArity.ZeroOrOne
        };
        var templateOption = new Option<string>("--template")
        {
            Description = "Template name (executive|technical|public) or path to a .scriban-html file",
            DefaultValueFactory = _ => "executive"
        };
        var outputOption = new Option<string?>("--output") { Description = "Output file path (default: <project>/reports/living-doc.html)" };
        var titleOption = new Option<string?>("--title") { Description = "Page title override" };
        var logoOption = new Option<string?>("--logo") { Description = "Logo URL" };
        var accentOption = new Option<string?>("--accent") { Description = "Accent colour hex (e.g. #22c55e)" };

        command.AddArgument(projectArgument);
        command.AddOption(templateOption);
        command.AddOption(outputOption);
        command.AddOption(titleOption);
        command.AddOption(logoOption);
        command.AddOption(accentOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var projectPath = context.ParseResult.GetValueForArgument(projectArgument);
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                projectPath = Environment.CurrentDirectory;
            }
            else if (!Path.IsPathRooted(projectPath))
            {
                projectPath = Path.GetFullPath(projectPath, Environment.CurrentDirectory);
            }

            // Resolve config to get project name and paths
            var configLoader = services.GetRequiredService<ConfigLoader>();
            var configResult = configLoader.Load(projectPath);
            var config = configResult.Value;

            // Build branding from config + overrides
            var projectName = config?.Project.Name is { Length: > 0 } n ? n : Path.GetFileName(projectPath);
            var defaultTitle = $"{projectName} — Living Doc";
            var branding = new DocumentBranding(
                Title: context.ParseResult.GetValueForOption(titleOption) ?? defaultTitle,
                LogoUrl: context.ParseResult.GetValueForOption(logoOption),
                AccentColor: context.ParseResult.GetValueForOption(accentOption) ?? "#6366f1");

            // Determine output path
            var outputPath = context.ParseResult.GetValueForOption(outputOption);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var reportsDir = Path.Combine(projectPath, config?.Paths.Reports ?? "reports");
                outputPath = Path.Combine(reportsDir, "living-doc.html");
            }
            else if (!Path.IsPathRooted(outputPath))
            {
                outputPath = Path.GetFullPath(outputPath, Environment.CurrentDirectory);
            }

            // Build the document model
            var builder = new DocumentBuilder(
                services.GetRequiredService<RunResultRepository>(),
                new RunMetricsService(),
                services.GetRequiredService<FlowParser>(),
                configLoader);

            var options = new DocumentBuildOptions
            {
                ProjectPath = projectPath,
                Branding = branding,
                MaxRecentRuns = 15,
                MaxScreenshots = 20,
                GitSha = ReadGitSha(projectPath)
            };

            DocumentModel model;
            try
            {
                model = await builder.BuildAsync(options, context.GetCancellationToken());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: Failed to build document model: {ex.Message}");
                context.ExitCode = 1;
                return;
            }

            // Render the template
            var renderer = new TemplateRenderer();
            var templateName = context.ParseResult.GetValueForOption(templateOption) ?? "executive";
            string html;
            try
            {
                var knownTemplates = new[] { "executive", "technical", "public" };
                html = knownTemplates.Contains(templateName, StringComparer.OrdinalIgnoreCase)
                    ? renderer.RenderEmbedded(templateName, model)
                    : renderer.Render(templateName, model);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: Template rendering failed: {ex.Message}");
                context.ExitCode = 1;
                return;
            }

            // Write output
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, html, context.GetCancellationToken());

            Console.Out.WriteLine(outputPath);
            Console.Out.WriteLine($"Template  : {templateName}");
            Console.Out.WriteLine($"Flows     : {model.Suite.TotalFlows}");
            Console.Out.WriteLine($"Pass rate : {model.Suite.PassRate:P1}");
            Console.Out.WriteLine($"Runs      : {model.RecentRuns.Count}");
            context.ExitCode = 0;
        });

        return command;
    }

    private static string ReadGitSha(string projectPath)
    {
        try
        {
            var headFile = Path.Combine(projectPath, ".git", "HEAD");
            if (!File.Exists(headFile)) return string.Empty;

            var head = File.ReadAllText(headFile).Trim();
            if (head.StartsWith("ref:", StringComparison.Ordinal))
            {
                var refPath = head[5..].Trim();
                var shaFile = Path.Combine(projectPath, ".git", refPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(shaFile))
                {
                    return File.ReadAllText(shaFile).Trim();
                }
            }

            return head;
        }
        catch
        {
            return string.Empty;
        }
    }
}
