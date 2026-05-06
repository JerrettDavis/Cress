using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Cress.Core.Models;

namespace Cress.Execution;

public sealed class ReportGenerator
{
    public IReadOnlyDictionary<string, string> Generate(ProjectCatalog catalog, RunResult result, IReadOnlyList<string> formats)
    {
        var requestedFormats = formats.Count == 0 ? ["html", "json", "junit", "markdown"] : formats;
        var reportRoot = Path.Combine(catalog.ProjectRoot, catalog.EffectiveConfig.Config.Paths.Reports, result.Metadata.RunId);
        Directory.CreateDirectory(reportRoot);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var includeMarkdown = false;

        foreach (var format in requestedFormats.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            switch (format.Trim().ToLowerInvariant())
            {
                case "html":
                    paths["html"] = WriteHtml(reportRoot, result);
                    break;
                case "json":
                    paths["json"] = WriteJson(reportRoot, result);
                    break;
                case "junit":
                    paths["junit"] = WriteJUnit(reportRoot, result);
                    break;
                case "markdown":
                case "md":
                    includeMarkdown = true;
                    break;
            }
        }

        if (includeMarkdown)
        {
            paths["markdown"] = WriteMarkdown(reportRoot, result with { Reports = paths });
        }

        return paths;
    }

    public IReadOnlyList<string> ListReports(string projectRoot, string reportsPath)
    {
        var root = Path.Combine(projectRoot, reportsPath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string WriteJson(string reportRoot, RunResult result)
    {
        var path = Path.Combine(reportRoot, "report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, ExecutionJson.Options), Encoding.UTF8);
        return path;
    }

    private static string WriteJUnit(string reportRoot, RunResult result)
    {
        var path = Path.Combine(reportRoot, "junit.xml");
        var suites = new XElement("testsuites",
            new XElement("testsuite",
                new XAttribute("name", result.Metadata.ProjectName),
                new XAttribute("tests", result.Flows.Count),
                new XAttribute("failures", result.Flows.Count(flow => flow.Outcome == RunOutcome.Failed || flow.Outcome == RunOutcome.Errored)),
                new XAttribute("skipped", result.Flows.Count(flow => flow.Outcome == RunOutcome.Skipped || flow.Outcome == RunOutcome.Blocked)),
                result.Flows.Select(flow =>
                {
                    var testCase = new XElement("testcase",
                        new XAttribute("classname", flow.CapabilityId ?? result.Metadata.ProjectName),
                        new XAttribute("name", flow.FlowId),
                        new XAttribute("time", (flow.DurationMs / 1000d).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)));

                    if (flow.Outcome is RunOutcome.Failed or RunOutcome.Errored)
                    {
                        testCase.Add(new XElement("failure",
                            new XAttribute("message", flow.FailureMessage ?? "Flow failed."),
                            $"{flow.FailureClassification}{Environment.NewLine}{flow.FailureMessage}"));
                    }
                    else if (flow.Outcome is RunOutcome.Skipped or RunOutcome.Blocked)
                    {
                        testCase.Add(new XElement("skipped", flow.FailureMessage ?? "Skipped."));
                    }

                    return testCase;
                })));
        File.WriteAllText(path, new XDocument(new XDeclaration("1.0", "utf-8", "yes"), suites).ToString(), Encoding.UTF8);
        return path;
    }

    private static string WriteMarkdown(string reportRoot, RunResult result)
    {
        var path = Path.Combine(reportRoot, "summary.md");
        var failed = result.Flows.Where(flow => flow.Outcome is RunOutcome.Failed or RunOutcome.Errored).ToList();
        var builder = new StringBuilder();
        builder.AppendLine($"# Cress run {result.Metadata.RunId}");
        builder.AppendLine();
        builder.AppendLine($"- Passed: {result.Flows.Count(flow => flow.Outcome == RunOutcome.Passed)}");
        builder.AppendLine($"- Failed: {failed.Count}");
        builder.AppendLine($"- Skipped/Blocked: {result.Flows.Count(flow => flow.Outcome is RunOutcome.Skipped or RunOutcome.Blocked)}");
        builder.AppendLine($"- Artifacts: `{result.Metadata.ArtifactRoot}`");
        builder.AppendLine($"- Trigger: `{result.Invocation.Trigger}`");
        builder.AppendLine($"- Retry count: `{result.Invocation.RetryCount}`");
        builder.AppendLine($"- Screenshot policy: `{result.Invocation.ScreenshotPolicy}`");
        if (!string.IsNullOrWhiteSpace(result.Invocation.StartFromStep))
        {
            builder.AppendLine($"- Rerun start step: `{result.Invocation.StartFromStep}`");
        }

        builder.AppendLine();
        if (failed.Count > 0)
        {
            builder.AppendLine("## Failed flows");
            foreach (var flow in failed)
            {
                builder.AppendLine($"- **{flow.Name}** (`{flow.FlowId}`){FormatTraceability(flow.Traceability)}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Flow summary");
        foreach (var flow in result.Flows)
        {
            builder.AppendLine($"- **{flow.Name}** (`{flow.FlowId}`) — {flow.Outcome}{(flow.PassedWithRetry ? " after retry" : string.Empty)} — {flow.DurationMs:F0} ms");
            foreach (var step in flow.Steps)
            {
                builder.AppendLine($"  - {step.Kind}:{step.Name} attempt {step.Attempt} — {step.Outcome} — {step.DurationMs:F0} ms");
            }
        }

        builder.AppendLine();
        foreach (var report in result.Reports)
        {
            builder.AppendLine($"- {report.Key}: `{report.Value}`");
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        return path;
    }

    private static string WriteHtml(string reportRoot, RunResult result)
    {
        var path = Path.Combine(reportRoot, "report.html");
        var artifactRelative = Path.GetRelativePath(reportRoot, result.Metadata.ArtifactRoot).Replace('\\', '/');
        var html = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>Cress report {{result.Metadata.RunId}}</title>
          <style>
            body { font-family: Arial, sans-serif; margin: 24px; }
            table { border-collapse: collapse; width: 100%; margin-top: 16px; }
            th, td { border: 1px solid #d0d7de; padding: 8px; text-align: left; vertical-align: top; }
            .passed { color: #1a7f37; font-weight: bold; }
            .failed, .errored { color: #d1242f; font-weight: bold; }
            .skipped, .blocked { color: #9a6700; font-weight: bold; }
            code { background: #f6f8fa; padding: 1px 4px; }
          </style>
        </head>
        <body>
          <h1>{{result.Metadata.ProjectName}}</h1>
          <p><strong>Run:</strong> {{result.Metadata.RunId}}<br />
             <strong>Profile:</strong> {{result.Metadata.Profile}}<br />
             <strong>Environment:</strong> {{result.Metadata.Environment ?? "n/a"}}<br />
             <strong>Started:</strong> {{result.Metadata.StartedAt}}<br />
             <strong>Ended:</strong> {{result.Metadata.EndedAt}}<br />
             <strong>Duration:</strong> {{result.Metadata.DurationMs}} ms<br />
             <strong>Trigger:</strong> {{result.Invocation.Trigger}}<br />
             <strong>Retries:</strong> {{result.Invocation.RetryCount}}<br />
             <strong>Screenshot policy:</strong> {{result.Invocation.ScreenshotPolicy}}<br />
              <strong>Artifacts:</strong> <a href="{{artifactRelative}}">{{artifactRelative}}</a></p>
           <h2>Flows</h2>
           <table>
             <thead>
               <tr><th>Flow</th><th>Status</th><th>Traceability</th><th>Drivers</th><th>Failure</th><th>Steps</th><th>Artifacts</th></tr>
             </thead>
             <tbody>
               {{string.Join(Environment.NewLine, result.Flows.Select(flow => $"""
               <tr>
                 <td><strong>{flow.Name}</strong><br /><code>{flow.FlowId}</code></td>
                 <td class="{flow.Outcome.ToString().ToLowerInvariant()}">{flow.Outcome}{(flow.PassedWithRetry ? " (retry)" : string.Empty)}<br /><small>{flow.DurationMs:F0} ms</small></td>
                 <td>{FormatTraceability(flow.Traceability)}</td>
                 <td>{string.Join(", ", flow.Drivers)}</td>
                 <td>{System.Net.WebUtility.HtmlEncode(flow.FailureMessage ?? string.Empty)}<br />{System.Net.WebUtility.HtmlEncode(flow.FailureClassification ?? string.Empty)}</td>
                 <td>{string.Join("<br />", flow.Steps.Select(step => $"<span class=\"{step.Outcome.ToString().ToLowerInvariant()}\">{step.Kind}: {step.Name}</span> — {step.Outcome} — {step.DurationMs:F0} ms (attempt {step.Attempt})"))}</td>
                 <td>{string.Join("<br />", flow.Steps.SelectMany(step => step.Artifacts).Select(artifact => $"<a href=\"{artifactRelative}/{artifact.RelativePath.Replace('\\', '/')}\">{System.Net.WebUtility.HtmlEncode(Path.GetFileName(artifact.RelativePath))}</a>"))}</td>
               </tr>
               """))}}
             </tbody>
           </table>
        </body>
        </html>
        """;
        File.WriteAllText(path, html, Encoding.UTF8);
        return path;
    }

    private static string FormatTraceability(TraceabilityInfo? traceability)
    {
        if (traceability is null)
        {
            return string.Empty;
        }

        var items = new List<string>();
        if (!string.IsNullOrWhiteSpace(traceability.Requirement))
        {
            items.Add(traceability.Requirement);
        }

        if (traceability.AcceptanceCriteria is not null && traceability.AcceptanceCriteria.Count > 0)
        {
            items.Add(string.Join(", ", traceability.AcceptanceCriteria));
        }

        return string.Join(" / ", items);
    }
}
