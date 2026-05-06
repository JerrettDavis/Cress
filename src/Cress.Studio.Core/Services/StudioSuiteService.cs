using Cress.Core.Models;
using Cress.Execution;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Studio.Services;

public sealed class StudioSuiteService
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public StudioSuiteService()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitNull)
            .Build();
    }

    public OperationResult<IReadOnlyList<StudioSuiteDocument>> LoadAll(string projectRoot)
    {
        var suites = new List<StudioSuiteDocument>();
        var diagnostics = new List<Diagnostic>();
        var suitesRoot = GetSuitesRoot(projectRoot);
        if (!Directory.Exists(suitesRoot))
        {
            return new OperationResult<IReadOnlyList<StudioSuiteDocument>>
            {
                Value = suites
            };
        }

        foreach (var path in Directory.EnumerateFiles(suitesRoot, "*.suite.yaml", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var load = Load(path);
            diagnostics.AddRange(load.Diagnostics);
            if (load.Value is not null)
            {
                suites.Add(load.Value);
            }
        }

        return new OperationResult<IReadOnlyList<StudioSuiteDocument>>
        {
            Value = suites,
            Diagnostics = diagnostics
        };
    }

    public OperationResult<StudioSuiteDocument> Load(string filePath)
    {
        var diagnostics = new List<Diagnostic>();
        if (!File.Exists(filePath))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "STE001",
                Message = "Suite file could not be found.",
                File = filePath
            });
            return new OperationResult<StudioSuiteDocument> { Diagnostics = diagnostics };
        }

        try
        {
            var document = _deserializer.Deserialize<StudioSuiteDocument>(File.ReadAllText(filePath)) ?? new StudioSuiteDocument();
            document = document with { FilePath = filePath };
            diagnostics.AddRange(Validate(document));
            return new OperationResult<StudioSuiteDocument>
            {
                Value = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? null : document,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "STE002",
                Message = $"Suite file could not be parsed: {ex.Message}",
                File = filePath
            });
            return new OperationResult<StudioSuiteDocument> { Diagnostics = diagnostics };
        }
    }

    public StudioSuiteDocument CreateNew(string projectRoot)
    {
        var root = GetSuitesRoot(projectRoot);
        Directory.CreateDirectory(root);

        var candidate = "new-suite.suite.yaml";
        var index = 1;
        while (File.Exists(Path.Combine(root, candidate)))
        {
            candidate = $"new-suite-{index++}.suite.yaml";
        }

        var document = new StudioSuiteDocument
        {
            FilePath = Path.Combine(root, candidate),
            Id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(candidate)),
            Name = "New suite",
            ReportFormats = ["html", "json", "markdown"]
        };

        return document;
    }

    public OperationResult<string> Save(StudioSuiteDocument document)
    {
        var diagnostics = Validate(document);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new OperationResult<string> { Diagnostics = diagnostics };
        }

        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "STE006",
                Message = "Suite file path is required."
            });
            return new OperationResult<string> { Diagnostics = diagnostics };
        }

        var directory = Path.GetDirectoryName(document.FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var source = _serializer.Serialize(document with { FilePath = null });
        File.WriteAllText(document.FilePath, source);
        return new OperationResult<string>
        {
            Value = source,
            Diagnostics = diagnostics
        };
    }

    public void Delete(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    public IReadOnlyList<NormalizedFlow> ResolveFlows(ProjectCatalog catalog, StudioSuiteDocument suite, ICollection<Diagnostic>? diagnostics = null)
    {
        IEnumerable<NormalizedFlow> selected = catalog.NormalizedFlows;

        if (suite.FlowIds.Count > 0)
        {
            var requested = new HashSet<string>(suite.FlowIds, StringComparer.OrdinalIgnoreCase);
            selected = selected.Where(flow => requested.Contains(flow.FlowId));
        }

        if (!string.IsNullOrWhiteSpace(suite.Tag))
        {
            selected = selected.Where(flow => flow.Tags.Contains(suite.Tag, StringComparer.OrdinalIgnoreCase));
        }

        var resolved = selected.OrderBy(flow => flow.FlowId, StringComparer.OrdinalIgnoreCase).ToList();
        if (resolved.Count == 0)
        {
            diagnostics?.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "STE003",
                Message = "No flows matched the suite selection.",
                File = suite.FilePath
            });
        }

        return resolved;
    }

    public static string GetSuitesRoot(string projectRoot)
        => Path.Combine(projectRoot, ".cress", "suites");

    private static List<Diagnostic> Validate(StudioSuiteDocument document)
    {
        var diagnostics = new List<Diagnostic>();
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "STE004",
                Message = "Suite id is required.",
                File = document.FilePath
            });
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "STE005",
                Message = "Suite name is required.",
                File = document.FilePath
            });
        }

        return diagnostics;
    }
}

public sealed record StudioSuiteDocument
{
    public int Version { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Profile { get; init; }
    public string? Tag { get; init; }
    public List<string> FlowIds { get; init; } = [];
    public List<string> ReportFormats { get; init; } = [];

    [YamlDotNet.Serialization.YamlIgnore]
    public string? FilePath { get; init; }
}
