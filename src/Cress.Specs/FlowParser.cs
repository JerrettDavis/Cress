using Cress.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Specs;

public sealed class FlowParser
{
    public OperationResult<CressFlow> ParseFile(string filePath, bool strict = false)
        => Parse(File.ReadAllText(filePath), filePath, strict);

    public OperationResult<CressFlow> Parse(string content, string? sourceFile = null, bool strict = false)
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

            var flow = builder.Build().Deserialize<CressFlow>(content) ?? new CressFlow();
            flow = flow with { SourceFile = sourceFile };

            return new OperationResult<CressFlow>
            {
                Value = flow,
                Diagnostics = Validate(flow, sourceFile)
            };
        }
        catch (YamlException ex)
        {
            return new OperationResult<CressFlow>
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "FLW001",
                        Message = "Flow YAML is invalid.",
                        File = sourceFile,
                        Line = (int)ex.Start.Line,
                        Column = (int)ex.Start.Column,
                        Details = ex.Message
                    }
                ]
            };
        }
    }

    private static IReadOnlyList<Diagnostic> Validate(CressFlow flow, string? sourceFile)
    {
        var diagnostics = new List<Diagnostic>();

        if (flow.Version <= 0)
        {
            diagnostics.Add(CreateRequiredDiagnostic("FLW002", "Flow version is required.", sourceFile));
        }

        if (string.IsNullOrWhiteSpace(flow.Id))
        {
            diagnostics.Add(CreateRequiredDiagnostic("FLW003", "Flow id is required.", sourceFile));
        }

        if (string.IsNullOrWhiteSpace(flow.Name))
        {
            diagnostics.Add(CreateRequiredDiagnostic("FLW004", "Flow name is required.", sourceFile));
        }

        if (flow.When.Count == 0)
        {
            diagnostics.Add(CreateRequiredDiagnostic("FLW005", "Flow must contain at least one 'when' action.", sourceFile));
        }

        if (flow.Then.Count == 0)
        {
            diagnostics.Add(CreateRequiredDiagnostic("FLW006", "Flow must contain at least one 'then' expectation.", sourceFile));
        }

        if (flow.Fixtures is not null)
        {
            foreach (var fixture in flow.Fixtures)
            {
                if (string.IsNullOrWhiteSpace(fixture.Key))
                {
                    diagnostics.Add(CreateRequiredDiagnostic("FLW009", "Flow fixtures must use a non-empty alias.", sourceFile));
                    continue;
                }

                if (fixture.Value is null || (string.IsNullOrWhiteSpace(fixture.Value.Use) && string.IsNullOrWhiteSpace(fixture.Value.Source)))
                {
                    diagnostics.Add(CreateRequiredDiagnostic("FLW010", $"Fixture '{fixture.Key}' must declare either 'use' or 'source'.", sourceFile));
                }
            }
        }

        for (var index = 0; index < flow.When.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(flow.When[index].Step))
            {
                diagnostics.Add(CreateRequiredDiagnostic("FLW007", $"Flow action #{index + 1} is missing a step value.", sourceFile));
            }
        }

        for (var index = 0; index < flow.Then.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(flow.Then[index].Expect))
            {
                diagnostics.Add(CreateRequiredDiagnostic("FLW008", $"Flow expectation #{index + 1} is missing an expect value.", sourceFile));
            }
        }

        return diagnostics;
    }

    private static Diagnostic CreateRequiredDiagnostic(string code, string message, string? file)
        => new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = code,
            Message = message,
            File = file
        };
}
