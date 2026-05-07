using System.Text.RegularExpressions;
using Cress.Core.Models;

namespace Cress.Exporters.TestFrameworks;

internal static class DotNetTestExporterSupport
{
    private static readonly Regex InvalidIdentifierCharactersRegex = new("[^a-zA-Z0-9_]+", RegexOptions.Compiled);

    public static string BuildClassName(NormalizedFlow flow, string suffix, string? classNameOverride)
        => string.IsNullOrWhiteSpace(classNameOverride)
            ? $"{SanitizeIdentifier(flow.FlowId, "CressFlow")}{suffix}"
            : classNameOverride;

    public static string BuildMethodName(NormalizedFlow flow)
        => SanitizeIdentifier(flow.Name, "RunsCressFlow");

    public static string GetRelativeFlowPath(NormalizedFlow flow, string projectRoot)
        => flow.SourceFile is null
            ? throw new InvalidOperationException($"Flow '{flow.FlowId}' does not have a source file.")
            : Path.GetRelativePath(projectRoot, flow.SourceFile);

    public static string ToVerbatimLiteral(string value)
        => "@\"" + value.Replace("\"", "\"\"") + "\"";

    public static string SanitizeIdentifier(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var cleaned = InvalidIdentifierCharactersRegex.Replace(value, " ");
        var parts = cleaned
            .Split([' ', '-', '.', '/', '\\', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            .ToArray();

        if (parts.Length == 0)
        {
            return fallback;
        }

        var identifier = string.Concat(parts);
        return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
    }
}
