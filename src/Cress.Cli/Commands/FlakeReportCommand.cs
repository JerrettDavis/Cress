using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Studio.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Cli.Commands;

public static class FlakeReportCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("flake-report", "Report flaky flows for a project based on run history");
        var projectArgument = new Argument<string>("project", "Path to the project (default: current directory)") { Arity = ArgumentArity.ZeroOrOne };
        var formatOption = new Option<string>("--format", () => "table", "Output format: table or json");
        var windowOption = new Option<int?>("--window", "Number of most-recent runs to analyse (overrides project config)");
        var thresholdOption = new Option<double?>("--threshold", "Fail-rate threshold 0.0–1.0 for flaky classification (overrides project config)");
        var exitCodeOption = new Option<bool>("--exit-code-on-flaky", "Exit with code 1 if any flow is flagged as flaky (CI gate)");

        command.AddArgument(projectArgument);
        command.AddOption(formatOption);
        command.AddOption(windowOption);
        command.AddOption(thresholdOption);
        command.AddOption(exitCodeOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            await Task.CompletedTask; // satisfy async

            var projectPath = context.ParseResult.GetValueForArgument(projectArgument);
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                projectPath = Environment.CurrentDirectory;
            }
            else if (!Path.IsPathRooted(projectPath))
            {
                projectPath = Path.GetFullPath(projectPath, Environment.CurrentDirectory);
            }

            var format = context.ParseResult.GetValueForOption(formatOption) ?? "table";
            var windowOverride = context.ParseResult.GetValueForOption(windowOption);
            var thresholdOverride = context.ParseResult.GetValueForOption(thresholdOption);
            var exitCodeOnFlaky = context.ParseResult.GetValueForOption(exitCodeOption);

            // Load project config
            var configLoader = services.GetRequiredService<ConfigLoader>();
            var configResult = configLoader.Load(projectPath);
            if (!configResult.Success || configResult.Value is null)
            {
                Console.Error.WriteLine($"error: Could not load project config from '{projectPath}'.");
                context.ExitCode = 1;
                return;
            }

            var config = configResult.Value!;

            // Build effective FlakeConfig, applying any CLI overrides
            var baseFlake = config.Flake;
            var flakeConfig = baseFlake with
            {
                Window = windowOverride ?? baseFlake.Window,
                Threshold = thresholdOverride ?? baseFlake.Threshold
            };

            // Load run history
            var repository = services.GetRequiredService<RunResultRepository>();
            var runs = repository.ListRuns(projectPath, config.Paths.Artifacts, maxCount: flakeConfig.Window * 2);

            // Analyse
            var insightsService = new StudioRunInsightsService();
            var insights = insightsService.Analyze(new ProjectCatalog(), runs, flakeConfig);

            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(insights.FlakyFlows, CommandSupport.JsonOptions));
            }
            else
            {
                PrintTable(insights, runs.Count);
            }

            context.ExitCode = exitCodeOnFlaky && insights.FlakyFlows.Count > 0 ? 1 : 0;
        });

        return command;
    }

    // -----------------------------------------------------------------------
    // Table renderer — public so the unit test project can call it directly
    // -----------------------------------------------------------------------

    public static void PrintTable(StudioRunInsights insights, int totalRunsLoaded)
    {
        var allFlows = insights.FlowHealth;

        Console.Out.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.Out.WriteLine($"║  Flake Report   runs-in-window: {totalRunsLoaded,4}   flows analysed: {allFlows.Count,4}            ║");
        Console.Out.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        if (allFlows.Count == 0)
        {
            Console.Out.WriteLine("║  No run history found. Run some flows first.                                ║");
            Console.Out.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            return;
        }

        // Header row
        Console.Out.WriteLine($"║  {"Flow",-34} {"Runs",4} {"Pass",4} {"Fail",4} {"Flake%",7}  {"Flaky?",7}  ║");
        Console.Out.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        foreach (var flow in allFlows)
        {
            // Derive pass count from PassRate and FailureCount:
            //   PassRate = passCount / (passCount + failCount) * 100
            //   → passCount = failCount * PassRate / (100 - PassRate)  [clamped for edge cases]
            int derivedPass;
            if (flow.PassRate >= 100.0)
            {
                derivedPass = flow.FailureCount == 0 ? 1 : (int)Math.Round(flow.PassRate / 100.0 * (flow.FailureCount + 1));
            }
            else if (flow.PassRate <= 0.0)
            {
                derivedPass = 0;
            }
            else
            {
                derivedPass = (int)Math.Round(flow.FailureCount * flow.PassRate / (100.0 - flow.PassRate));
            }

            var totalRuns = derivedPass + flow.FailureCount;
            var flakyMark = flow.IsFlaky ? " YES  " : "  -   ";

            Console.Out.WriteLine($"║  {Truncate(flow.FlowId, 34),-34} {totalRuns,4} {derivedPass,4} {flow.FailureCount,4} {flow.PassRate,6:F1}%  {flakyMark}  ║");

            // Top 3 unstable steps (only if flow has step breakdown)
            var topSteps = flow.StepBreakdown
                .Where(s => s.FailCount > 0)
                .OrderByDescending(s => s.FlakeRate)
                .Take(3)
                .ToList();

            foreach (var step in topSteps)
            {
                Console.Out.WriteLine($"║    ↳ {Truncate(step.StepName, 30),-30} fail {step.FailCount}/{step.TotalCount} ({step.FlakeRate:F0}%)        ║");
            }
        }

        Console.Out.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
        var flakyCount = insights.FlakyFlows.Count;
        var statusLine = flakyCount == 0
            ? "  All flows are stable."
            : $"  {flakyCount} flaky flow(s) detected.";
        Console.Out.WriteLine($"║{statusLine,-76}║");
        Console.Out.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
