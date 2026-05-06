using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Studio.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Cli.Commands;

public static class MetricsCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("metrics", "Aggregate run history metrics for a project");
        var projectArgument = new Argument<string>("project", "Path to the project (default: current directory)") { Arity = ArgumentArity.ZeroOrOne };
        var formatOption = new Option<string>("--format", () => "table", "Output format: table or json");
        var windowOption = new Option<string?>("--window", "Restrict analysis to runs within this duration (e.g. 7d, 24h, 30m)");
        var maxOption = new Option<int?>("--max", "Maximum number of runs to include");

        command.AddArgument(projectArgument);
        command.AddOption(formatOption);
        command.AddOption(windowOption);
        command.AddOption(maxOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            await Task.CompletedTask; // satisfy async requirement

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
            var windowStr = context.ParseResult.GetValueForOption(windowOption);
            var maxRuns = context.ParseResult.GetValueForOption(maxOption);

            TimeSpan? window = null;
            if (!string.IsNullOrWhiteSpace(windowStr))
            {
                if (!TryParseDuration(windowStr, out var parsed))
                {
                    Console.Error.WriteLine($"error: Cannot parse --window value '{windowStr}'. Use formats like 7d, 24h, 30m.");
                    context.ExitCode = 1;
                    return;
                }

                window = parsed;
            }

            // Load run history
            var configLoader = services.GetRequiredService<ConfigLoader>();
            var configResult = configLoader.Load(projectPath);
            if (!configResult.Success || configResult.Value is null)
            {
                Console.Error.WriteLine($"error: Could not load project config from '{projectPath}'.");
                context.ExitCode = 1;
                return;
            }

            var config = configResult.Value;
            var repository = services.GetRequiredService<RunResultRepository>();
            var runs = repository.ListRuns(projectPath, config.Paths.Artifacts, maxCount: window.HasValue ? 500 : (maxRuns ?? 100));

            var metricsService = new RunMetricsService();
            var options = new MetricsOptions(window, maxRuns);
            var metrics = metricsService.Aggregate(runs, options);

            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(metrics, CommandSupport.JsonOptions));
            }
            else
            {
                PrintTable(metrics);
            }

            context.ExitCode = 0;
        });

        return command;
    }

    private static void PrintTable(RunMetrics metrics)
    {
        var s = metrics.Suite;
        Console.Out.WriteLine("=== Suite Metrics ===");
        Console.Out.WriteLine($"  Total runs   : {s.TotalRuns}");
        Console.Out.WriteLine($"  Passed       : {s.PassedRuns}");
        Console.Out.WriteLine($"  Failed       : {s.FailedRuns}");
        Console.Out.WriteLine($"  Pass rate    : {s.PassRate:P1}");
        Console.Out.WriteLine($"  Total time   : {FormatDuration(s.TotalDuration)}");
        Console.Out.WriteLine($"  Avg run time : {FormatDuration(s.AvgDuration)}");

        if (metrics.Flows.Count > 0)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine("=== Top 5 Slowest Flows (by P95) ===");
            Console.Out.WriteLine($"  {"Flow ID",-36} {"Runs",5} {"Pass%",7} {"Avg",9} {"P50",9} {"P95",9} {"P99",9} {"Flake%",7}");
            Console.Out.WriteLine($"  {new string('-', 36),-36} {"-----",5} {"-------",7} {"---------",9} {"---------",9} {"---------",9} {"---------",9} {"-------",7}");

            foreach (var f in metrics.Flows.OrderByDescending(f => f.P95).Take(5))
            {
                Console.Out.WriteLine($"  {Truncate(f.FlowId, 36),-36} {f.Runs,5} {f.PassRate,7:P1} {FormatDuration(f.AvgDuration),9} {FormatDuration(f.P50),9} {FormatDuration(f.P95),9} {FormatDuration(f.P99),9} {f.FlakeRate,7:P1}");
            }

            Console.Out.WriteLine();
            Console.Out.WriteLine("=== Top 5 Flakiest Flows ===");
            var flakyFlows = metrics.Flows.Where(f => f.FlakeRate > 0).OrderByDescending(f => f.FlakeRate).Take(5).ToList();
            if (flakyFlows.Count == 0)
            {
                Console.Out.WriteLine("  (none)");
            }
            else
            {
                Console.Out.WriteLine($"  {"Flow ID",-36} {"Runs",5} {"Flake%",7} {"MTTR",10}");
                Console.Out.WriteLine($"  {new string('-', 36),-36} {"-----",5} {"-------",7} {"----------",10}");
                foreach (var f in flakyFlows)
                {
                    var mttrStr = f.MTTR.HasValue ? FormatDuration(f.MTTR.Value) : "n/a";
                    Console.Out.WriteLine($"  {Truncate(f.FlowId, 36),-36} {f.Runs,5} {f.FlakeRate,7:P1} {mttrStr,10}");
                }
            }
        }

        if (metrics.Trend.Count > 0)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine("=== Trend (up to 3 most recent buckets) ===");
            Console.Out.WriteLine($"  {"Timestamp",-24} {"Passed",7} {"Failed",7} {"Avg Duration",14}");
            Console.Out.WriteLine($"  {new string('-', 24),-24} {"-------",7} {"-------",7} {"--------------",14}");

            foreach (var point in metrics.Trend.TakeLast(3))
            {
                Console.Out.WriteLine($"  {point.Timestamp.ToString("yyyy-MM-dd HH:mm"),-24} {point.PassedFlows,7} {point.FailedFlows,7} {FormatDuration(point.AvgFlowDuration),14}");
            }
        }
    }

    private static bool TryParseDuration(string value, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var unit = value[^1];
        if (!double.TryParse(value[..^1], out var amount))
        {
            return false;
        }

        result = unit switch
        {
            'd' or 'D' => TimeSpan.FromDays(amount),
            'h' or 'H' => TimeSpan.FromHours(amount),
            'm' or 'M' => TimeSpan.FromMinutes(amount),
            's' or 'S' => TimeSpan.FromSeconds(amount),
            _ => TimeSpan.Zero
        };

        return result > TimeSpan.Zero;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return $"{ts.TotalHours:F1}h";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{ts.TotalMinutes:F1}m";
        }

        if (ts.TotalSeconds >= 1)
        {
            return $"{ts.TotalSeconds:F1}s";
        }

        return $"{ts.TotalMilliseconds:F0}ms";
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
