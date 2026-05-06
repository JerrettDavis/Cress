using Cress.Core.Models;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Specs;

public sealed class CapabilityParser
{
    public OperationResult<CressCapability> ParseFile(string filePath, bool strict = false)
        => Parse(File.ReadAllText(filePath), filePath, strict);

    public OperationResult<CressCapability> Parse(string content, string? sourceFile = null, bool strict = false)
    {
        if (!TrySplitFrontMatter(content, out var frontMatter, out var body))
        {
            return new OperationResult<CressCapability>
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "CAP001",
                        Message = "Capability file must start with YAML front matter.",
                        File = sourceFile
                    }
                ]
            };
        }

        try
        {
            var builder = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreFields();

            if (!strict)
            {
                builder = builder.IgnoreUnmatchedProperties();
            }

            var metadata = builder.Build().Deserialize<CapabilityFrontMatter>(frontMatter) ?? new CapabilityFrontMatter();
            var bodyModel = ParseBody(body);

            var capability = new CressCapability
            {
                Version = metadata.Version,
                Id = metadata.Id ?? string.Empty,
                Name = bodyModel.Name ?? string.Empty,
                Owner = metadata.Owner,
                Risk = metadata.Risk,
                Tags = metadata.Tags ?? [],
                Rules = bodyModel.Rules.Count == 0 ? null : bodyModel.Rules,
                AcceptanceCriteria = bodyModel.AcceptanceCriteria.Count == 0 ? null : bodyModel.AcceptanceCriteria,
                SourceFile = sourceFile
            };

            return new OperationResult<CressCapability>
            {
                Value = capability,
                Diagnostics = Validate(capability, sourceFile)
            };
        }
        catch (YamlException ex)
        {
            return new OperationResult<CressCapability>
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "CAP002",
                        Message = "Capability front matter YAML is invalid.",
                        File = sourceFile,
                        Line = (int)ex.Start.Line,
                        Column = (int)ex.Start.Column,
                        Details = ex.Message
                    }
                ]
            };
        }
    }

    private static IReadOnlyList<Diagnostic> Validate(CressCapability capability, string? sourceFile)
    {
        var diagnostics = new List<Diagnostic>();

        if (capability.Version <= 0)
        {
            diagnostics.Add(CreateDiagnostic("CAP003", "Capability version is required.", sourceFile));
        }

        if (string.IsNullOrWhiteSpace(capability.Id))
        {
            diagnostics.Add(CreateDiagnostic("CAP004", "Capability id is required.", sourceFile));
        }

        if (string.IsNullOrWhiteSpace(capability.Name))
        {
            diagnostics.Add(CreateDiagnostic("CAP005", "Capability heading is required.", sourceFile));
        }

        return diagnostics;
    }

    private static BodyParseResult ParseBody(string body)
    {
        var document = Markdown.Parse(body);
        string? name = null;
        var rules = new List<string>();
        var acceptanceCriteria = new List<AcceptanceCriterion>();
        var currentSection = string.Empty;
        AcceptanceCriterion? currentCriterion = null;

        foreach (var block in document)
        {
            switch (block)
            {
                case HeadingBlock heading:
                {
                    var headingText = ExtractInlineText(heading.Inline).Trim();
                    if (heading.Level == 1 && string.IsNullOrWhiteSpace(name))
                    {
                        name = headingText.StartsWith("Capability:", StringComparison.OrdinalIgnoreCase)
                            ? headingText["Capability:".Length..].Trim()
                            : headingText;
                    }

                    if (heading.Level == 2)
                    {
                        currentSection = headingText;
                        currentCriterion = null;
                    }
                    else if (heading.Level == 3 && currentSection.Equals("Acceptance Criteria", StringComparison.OrdinalIgnoreCase))
                    {
                        currentCriterion = new AcceptanceCriterion
                        {
                            Id = headingText,
                            Description = string.Empty
                        };
                        acceptanceCriteria.Add(currentCriterion);
                    }

                    break;
                }
                case ListBlock listBlock when currentSection.Equals("Rules", StringComparison.OrdinalIgnoreCase):
                    foreach (var item in listBlock.OfType<ListItemBlock>())
                    {
                        var text = ExtractBlockText(item).Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            rules.Add(text);
                        }
                    }

                    break;
                case ParagraphBlock paragraphBlock when currentCriterion is not null:
                    var paragraph = ExtractInlineText(paragraphBlock.Inline).Trim();
                    if (!string.IsNullOrWhiteSpace(paragraph))
                    {
                        currentCriterion = currentCriterion with
                        {
                            Description = string.IsNullOrWhiteSpace(currentCriterion.Description)
                                ? paragraph
                                : $"{currentCriterion.Description} {paragraph}"
                        };

                        acceptanceCriteria[^1] = currentCriterion;
                    }

                    break;
            }
        }

        return new BodyParseResult(name, rules, acceptanceCriteria);
    }

    private static bool TrySplitFrontMatter(string content, out string frontMatter, out string body)
    {
        frontMatter = string.Empty;
        body = string.Empty;

        var normalized = content.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return false;
        }

        var endMarkerIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (endMarkerIndex < 0)
        {
            return false;
        }

        frontMatter = normalized[4..endMarkerIndex];
        body = normalized[(endMarkerIndex + 5)..];
        return true;
    }

    private static string ExtractBlockText(ContainerBlock block)
    {
        var parts = new List<string>();
        foreach (var child in block)
        {
            switch (child)
            {
                case ParagraphBlock paragraph:
                    parts.Add(ExtractInlineText(paragraph.Inline));
                    break;
                case ListBlock list:
                    parts.AddRange(list.OfType<ListItemBlock>().Select(ExtractBlockText));
                    break;
            }
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string ExtractInlineText(ContainerInline? inline)
    {
        if (inline is null)
        {
            return string.Empty;
        }

        var text = new List<string>();
        var current = inline.FirstChild;
        while (current is not null)
        {
            switch (current)
            {
                case LiteralInline literal:
                    text.Add(literal.Content.Text.Substring(literal.Content.Start, literal.Content.Length));
                    break;
                case ContainerInline container:
                    text.Add(ExtractInlineText(container));
                    break;
            }

            current = current.NextSibling;
        }

        return string.Join(string.Empty, text);
    }

    private static Diagnostic CreateDiagnostic(string code, string message, string? sourceFile)
        => new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = code,
            Message = message,
            File = sourceFile
        };

    private sealed record CapabilityFrontMatter
    {
        public int Version { get; init; }
        public string? Id { get; init; }
        public string? Owner { get; init; }
        public string? Risk { get; init; }
        public List<string>? Tags { get; init; }
    }

    private sealed record BodyParseResult(string? Name, List<string> Rules, List<AcceptanceCriterion> AcceptanceCriteria);
}
