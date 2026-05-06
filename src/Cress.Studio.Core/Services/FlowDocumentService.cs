using System.Text;
using Cress.Core.Models;
using Cress.Specs;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Studio.Services;

public sealed class FlowDocumentService
{
    private readonly FlowParser _flowParser;
    private readonly ISerializer _serializer;

    public FlowDocumentService(FlowParser flowParser)
    {
        _flowParser = flowParser;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitNull)
            .Build();
    }

    public OperationResult<FlowEditorDocument> Load(string filePath)
        => FromParseResult(_flowParser.ParseFile(filePath));

    public OperationResult<FlowEditorDocument> LoadFromSource(string sourceText, string? filePath = null)
        => FromParseResult(_flowParser.Parse(sourceText, filePath));

    public FlowEditorDocument CreateNew(string projectRoot, string flowsRelativePath)
    {
        var flowsRoot = Path.Combine(projectRoot, flowsRelativePath);
        Directory.CreateDirectory(flowsRoot);

        var candidateName = "new-flow.flow.yaml";
        var index = 1;
        while (File.Exists(Path.Combine(flowsRoot, candidateName)))
        {
            candidateName = $"new-flow-{index++}.flow.yaml";
        }

        var document = new FlowEditorDocument
        {
            FilePath = Path.Combine(flowsRoot, candidateName),
            Id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(candidateName)),
            Name = "New flow",
            TagsText = "studio",
            Actions =
            [
                new EditableExecutable
                {
                    Name = "app.open"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "app.is_visible"
                }
            ]
        };

        document.SourceText = Serialize(document);
        return document;
    }

    public OperationResult<string> Save(FlowEditorDocument document)
    {
        var source = Serialize(document);
        var parseResult = _flowParser.Parse(source, document.FilePath);
        if (parseResult.Value is null)
        {
            return new OperationResult<string> { Diagnostics = parseResult.Diagnostics };
        }

        var directory = Path.GetDirectoryName(document.FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(document.FilePath, source, Encoding.UTF8);
        return new OperationResult<string>
        {
            Value = source,
            Diagnostics = parseResult.Diagnostics
        };
    }

    public string Serialize(FlowEditorDocument document)
        => _serializer.Serialize(ToSerializableFlow(document));

    private OperationResult<FlowEditorDocument> FromParseResult(OperationResult<CressFlow> parseResult)
    {
        if (parseResult.Value is null)
        {
            return new OperationResult<FlowEditorDocument>
            {
                Diagnostics = parseResult.Diagnostics
            };
        }

        return new OperationResult<FlowEditorDocument>
        {
            Value = FromFlow(parseResult.Value),
            Diagnostics = parseResult.Diagnostics
        };
    }

    private FlowEditorDocument FromFlow(CressFlow flow)
    {
        var document = new FlowEditorDocument
        {
            FilePath = flow.SourceFile ?? string.Empty,
            Id = flow.Id,
            Name = flow.Name,
            CapabilityId = flow.CapabilityId,
            Summary = flow.Summary,
            Status = flow.Status,
            TagsText = string.Join(", ", flow.Tags)
        };

        if (flow.Fixtures is not null)
        {
            document.Fixtures.AddRange(flow.Fixtures.Select(item => new EditableFixture
            {
                Alias = item.Key,
                Use = item.Value.Use,
                Source = item.Value.Source,
                For = item.Value.For
            }));
        }

        document.Actions.AddRange(flow.When.Select(action => new EditableExecutable
        {
            Name = action.Step,
            InputsText = FormatInputs(action.With)
        }));

        document.Expectations.AddRange(flow.Then.Select(expectation => new EditableExecutable
        {
            Name = expectation.Expect,
            InputsText = FormatInputs(expectation.With)
        }));

        document.SourceText = File.Exists(document.FilePath) ? File.ReadAllText(document.FilePath) : Serialize(document);
        return document;
    }

    private static SerializableFlow ToSerializableFlow(FlowEditorDocument document)
        => new()
        {
            Version = 1,
            Id = document.Id,
            Name = document.Name,
            CapabilityId = string.IsNullOrWhiteSpace(document.CapabilityId) ? null : document.CapabilityId,
            Summary = string.IsNullOrWhiteSpace(document.Summary) ? null : document.Summary,
            Tags = ParseTags(document.TagsText),
            Status = string.IsNullOrWhiteSpace(document.Status) ? null : document.Status,
            Fixtures = document.Fixtures.Count == 0
                ? null
                : document.Fixtures.ToDictionary(
                    item => item.Alias,
                    item => new FlowFixtureRef
                    {
                        Use = string.IsNullOrWhiteSpace(item.Use) ? null : item.Use,
                        Source = string.IsNullOrWhiteSpace(item.Source) ? null : item.Source,
                        For = string.IsNullOrWhiteSpace(item.For) ? null : item.For
                    },
                    StringComparer.OrdinalIgnoreCase),
            When = document.Actions.Select(action => new FlowAction
            {
                Step = action.Name,
                With = ParseInputs(action.InputsText)
            }).ToList(),
            Then = document.Expectations.Select(expectation => new FlowExpectation
            {
                Expect = expectation.Name,
                With = ParseInputs(expectation.InputsText)
            }).ToList()
        };

    private static List<string>? ParseTags(string tagsText)
    {
        var tags = tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return tags.Count == 0 ? null : tags;
    }

    private static Dictionary<string, string>? ParseInputs(string? inputsText)
    {
        if (string.IsNullOrWhiteSpace(inputsText))
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in inputsText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                separatorIndex = line.IndexOf(':');
            }

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static string FormatInputs(IReadOnlyDictionary<string, string>? inputs)
        => inputs is null || inputs.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, inputs.Select(input => $"{input.Key}={input.Value}"));

    private sealed record SerializableFlow
    {
        public int Version { get; init; }
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;

        [YamlMember(Alias = "capability")]
        public string? CapabilityId { get; init; }

        public string? Summary { get; init; }
        public List<string>? Tags { get; init; }
        public Dictionary<string, FlowFixtureRef>? Fixtures { get; init; }
        public List<FlowAction> When { get; init; } = [];
        public List<FlowExpectation> Then { get; init; } = [];
        public string? Status { get; init; }
    }
}

public sealed class FlowEditorDocument
{
    public string FilePath { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CapabilityId { get; set; }
    public string? Summary { get; set; }
    public string? Status { get; set; }
    public string TagsText { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public List<EditableFixture> Fixtures { get; set; } = [];
    public List<EditableExecutable> Actions { get; set; } = [];
    public List<EditableExecutable> Expectations { get; set; } = [];
}

public sealed class EditableFixture
{
    public string Alias { get; init; } = string.Empty;
    public string? Use { get; init; }
    public string? Source { get; init; }
    public string? For { get; init; }
}

public sealed class EditableExecutable
{
    public string Name { get; init; } = string.Empty;
    public string InputsText { get; init; } = string.Empty;
}
