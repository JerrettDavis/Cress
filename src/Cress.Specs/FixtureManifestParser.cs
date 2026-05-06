using Cress.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Specs;

public sealed class FixtureManifestParser
{
    public OperationResult<FixtureManifest> ParseFile(string filePath, bool strict = false)
        => Parse(File.ReadAllText(filePath), filePath, strict);

    public OperationResult<FixtureManifest> Parse(string content, string? sourceFile = null, bool strict = false)
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

            var manifest = builder.Build().Deserialize<FixtureManifest>(content) ?? new FixtureManifest();
            manifest = manifest with
            {
                SourceFile = sourceFile,
                Fixtures = manifest.Fixtures.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value with { Name = entry.Key, SourceFile = sourceFile },
                    StringComparer.OrdinalIgnoreCase)
            };

            return new OperationResult<FixtureManifest>
            {
                Value = manifest,
                Diagnostics = Validate(manifest, sourceFile)
            };
        }
        catch (YamlException ex)
        {
            return new OperationResult<FixtureManifest>
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "FIX001",
                        Message = "Fixture manifest YAML is invalid.",
                        File = sourceFile,
                        Line = (int)ex.Start.Line,
                        Column = (int)ex.Start.Column,
                        Details = ex.Message
                    }
                ]
            };
        }
    }

    private static IReadOnlyList<Diagnostic> Validate(FixtureManifest manifest, string? sourceFile)
    {
        var diagnostics = new List<Diagnostic>();

        if (manifest.Version <= 0)
        {
            diagnostics.Add(CreateDiagnostic("FIX002", "Fixture manifest version is required.", sourceFile));
        }

        foreach (var fixture in manifest.Fixtures.Values)
        {
            if (string.IsNullOrWhiteSpace(fixture.Type))
            {
                diagnostics.Add(CreateDiagnostic("FIX003", $"Fixture '{fixture.Name}' must declare a type.", sourceFile));
            }

            if (string.IsNullOrWhiteSpace(fixture.Strategy))
            {
                diagnostics.Add(CreateDiagnostic("FIX004", $"Fixture '{fixture.Name}' must declare a strategy.", sourceFile));
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
