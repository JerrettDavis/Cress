using Cress.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Specs;

public sealed class StepManifestParser
{
    public OperationResult<StepManifest> ParseFile(string filePath, bool strict = false)
        => Parse(File.ReadAllText(filePath), filePath, strict);

    public OperationResult<StepManifest> Parse(string content, string? sourceFile = null, bool strict = false)
    {
        try
        {
            var builder = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreFields();

            if (!strict)
            {
                builder = builder.IgnoreUnmatchedProperties();
            }

            var manifest = builder.Build().Deserialize<StepManifest>(content) ?? new StepManifest();
            manifest = manifest with
            {
                SourceFile = sourceFile,
                Steps = manifest.Steps.Select(step => step with { SourceFile = sourceFile }).ToList()
            };

            return new OperationResult<StepManifest>
            {
                Value = manifest,
                Diagnostics = Validate(manifest, sourceFile)
            };
        }
        catch (YamlException ex)
        {
            return new OperationResult<StepManifest>
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "STP001",
                        Message = "Step manifest YAML is invalid.",
                        File = sourceFile,
                        Line = (int)ex.Start.Line,
                        Column = (int)ex.Start.Column,
                        Details = ex.Message
                    }
                ]
            };
        }
    }

    private static IReadOnlyList<Diagnostic> Validate(StepManifest manifest, string? sourceFile)
    {
        var diagnostics = new List<Diagnostic>();

        if (manifest.Version <= 0)
        {
            diagnostics.Add(CreateDiagnostic("STP002", "Step manifest version is required.", sourceFile));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in manifest.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
            {
                diagnostics.Add(CreateDiagnostic("STP003", "Step name is required.", sourceFile));
                continue;
            }

            if (!seen.Add(step.Name))
            {
                diagnostics.Add(CreateDiagnostic("STP004", $"Duplicate step '{step.Name}' was found in a manifest.", sourceFile));
            }

            if (step.Implementation is null || string.IsNullOrWhiteSpace(step.Implementation.Operation))
            {
                diagnostics.Add(CreateDiagnostic("STP005", $"Step '{step.Name}' is missing an implementation operation.", sourceFile));
            }
        }

        return diagnostics;
    }

    private static Diagnostic CreateDiagnostic(string code, string message, string? sourceFile)
        => new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = code,
            Message = message,
            File = sourceFile
        };
}
