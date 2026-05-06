using Cress.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.ProjectSystem;

public sealed class ConfigLoader
{
    private readonly ProjectLocator _projectLocator;

    public ConfigLoader(ProjectLocator projectLocator)
    {
        _projectLocator = projectLocator;
    }

    public OperationResult<CressConfig> Load(string projectRoot, bool strict = false)
    {
        var configPath = _projectLocator.GetConfigPath(projectRoot);
        return LoadFile(configPath, strict);
    }

    public OperationResult<CressConfig> LoadFile(string configPath, bool strict = false)
    {
        if (!File.Exists(configPath))
        {
            return new OperationResult<CressConfig>
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "CFG001",
                        Message = "Configuration file was not found.",
                        File = configPath
                    }
                ]
            };
        }

        try
        {
            var deserializer = CreateDeserializer(strict);
            var content = File.ReadAllText(configPath);
            var config = deserializer.Deserialize<CressConfig>(content) ?? new CressConfig();

            return new OperationResult<CressConfig>
            {
                Value = config,
                Diagnostics = Validate(config, configPath)
            };
        }
        catch (YamlException ex)
        {
            return new OperationResult<CressConfig>
            {
                Diagnostics =
                [
                    CreateYamlDiagnostic("CFG002", "Configuration YAML is invalid.", configPath, ex)
                ]
            };
        }
    }

    public string Serialize<T>(T value)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
            .Build();

        return serializer.Serialize(value);
    }

    private static IDeserializer CreateDeserializer(bool strict)
    {
        var builder = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreFields();

        if (!strict)
        {
            builder = builder.IgnoreUnmatchedProperties();
        }

        return builder.Build();
    }

    private static IReadOnlyList<Diagnostic> Validate(CressConfig config, string configPath)
    {
        var diagnostics = new List<Diagnostic>();

        if (config.Version <= 0)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "CFG003",
                Message = "Configuration version is required and must be greater than zero.",
                File = configPath
            });
        }

        if (string.IsNullOrWhiteSpace(config.Project.Name))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "CFG004",
                Message = "Project name is required.",
                File = configPath
            });
        }

        if (string.IsNullOrWhiteSpace(config.Project.DefaultProfile))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "CFG005",
                Message = "Project defaultProfile is required.",
                File = configPath
            });
        }

        foreach (var (label, value) in EnumeratePaths(config.Paths))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "CFG006",
                    Message = $"Path '{label}' must not be empty.",
                    File = configPath
                });
            }
        }

        return diagnostics;
    }

    private static IEnumerable<(string Label, string Value)> EnumeratePaths(PathsConfig paths)
    {
        yield return ("capabilities", paths.Capabilities);
        yield return ("flows", paths.Flows);
        yield return ("models", paths.Models);
        yield return ("fixtures", paths.Fixtures);
        yield return ("steps", paths.Steps);
        yield return ("artifacts", paths.Artifacts);
        yield return ("reports", paths.Reports);
    }

    internal static Diagnostic CreateYamlDiagnostic(string code, string message, string file, YamlException ex)
        => new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = code,
            Message = message,
            File = file,
            Line = (int)ex.Start.Line,
            Column = (int)ex.Start.Column,
            Details = ex.Message
        };
}
