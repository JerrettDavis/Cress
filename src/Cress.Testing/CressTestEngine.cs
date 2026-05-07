using System.Text;
using Cress.Core.Models;
using Cress.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Testing;

public static class CressTestEngine
{
    public static async Task<RunResult> RunProjectAsync(
        string projectPath,
        string? profile = null,
        IReadOnlyList<string>? reportFormats = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedProjectPath = CressTestPaths.ResolveProjectPath(projectPath);
        var result = await ExecuteAsync(resolvedProjectPath, new RunOptions
        {
            Profile = profile,
            ReportFormats = reportFormats ?? ["json"]
        }, cancellationToken);

        EnsurePassed(result, $"project '{resolvedProjectPath}'");
        return result;
    }

    public static async Task<RunResult> RunFlowAsync(
        string projectPath,
        string flowPath,
        string? profile = null,
        IReadOnlyList<string>? reportFormats = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedProjectPath = CressTestPaths.ResolveProjectPath(projectPath);
        var resolvedFlowPath = CressTestPaths.ResolveFlowPath(resolvedProjectPath, flowPath);
        var result = await ExecuteAsync(resolvedProjectPath, new RunOptions
        {
            FlowPath = resolvedFlowPath,
            Profile = profile,
            ReportFormats = reportFormats ?? ["json"]
        }, cancellationToken);

        EnsurePassed(result, $"flow '{resolvedFlowPath}'");
        return result;
    }

    private static async Task<RunResult> ExecuteAsync(string projectPath, RunOptions options, CancellationToken cancellationToken)
    {
        using var services = new ServiceCollection()
            .AddCressRuntime()
            .BuildServiceProvider();

        var orchestrator = services.GetRequiredService<RuntimeOrchestrator>();
        return await orchestrator.ExecuteAsync(projectPath, options, cancellationToken);
    }

    private static void EnsurePassed(RunResult result, string subject)
    {
        if (result.Passed)
        {
            return;
        }

        throw new CressTestFailureException(BuildFailureMessage(subject, result));
    }

    private static string BuildFailureMessage(string subject, RunResult result)
    {
        var builder = new StringBuilder()
            .AppendLine($"Cress run failed for {subject}.")
            .AppendLine($"Artifact root: {result.Metadata.ArtifactRoot}");

        if (result.Diagnostics.Count > 0)
        {
            builder.AppendLine("Diagnostics:");
            foreach (var diagnostic in result.Diagnostics)
            {
                builder.Append("- ")
                    .Append(diagnostic.Code)
                    .Append(": ")
                    .AppendLine(diagnostic.Message);
            }
        }

        foreach (var flow in result.Flows.Where(flow => flow.Outcome is RunOutcome.Failed or RunOutcome.Errored))
        {
            builder.Append("Flow ")
                .Append(flow.FlowId)
                .Append(": ")
                .AppendLine(flow.FailureMessage ?? flow.Outcome.ToString());
        }

        return builder.ToString().TrimEnd();
    }
}
