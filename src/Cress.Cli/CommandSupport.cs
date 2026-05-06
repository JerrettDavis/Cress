using Cress.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cress.Cli;

internal static class CommandSupport
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static int GetExitCode(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? 1 : 0;

    public static void WriteDiagnostics(IEnumerable<Diagnostic> diagnostics, bool json)
    {
        var materialized = diagnostics.ToList();

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                success = materialized.All(d => d.Severity != DiagnosticSeverity.Error),
                diagnostics = materialized
            }, JsonOptions));

            return;
        }

        foreach (var diagnostic in materialized)
        {
            var location = diagnostic.File is null
                ? string.Empty
                : diagnostic.Line is null || diagnostic.Column is null
                    ? $" ({diagnostic.File})"
                    : $" ({diagnostic.File}:{diagnostic.Line}:{diagnostic.Column})";

            Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}{location}");
            if (!string.IsNullOrWhiteSpace(diagnostic.Details))
            {
                Console.Error.WriteLine($"  {diagnostic.Details}");
            }
        }
    }
}
