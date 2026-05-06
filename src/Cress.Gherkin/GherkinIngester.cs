using System.Text;
using System.Text.RegularExpressions;
using Cress.Core.Models;
using Cress.Gherkin.Phrases;

namespace Cress.Gherkin;

/// <summary>
/// Parses a Gherkin <c>.feature</c> file back into a <see cref="CressFlow"/>.
/// Uses the <see cref="PhraseLibrary"/> for reverse-lookup of step text → step op.
/// </summary>
public sealed class GherkinIngester
{
    private readonly PhraseLibrary _library;

    public GherkinIngester(PhraseLibrary library)
    {
        _library = library;
    }

    /// <summary>
    /// Parse the given <c>.feature</c> text and return a <see cref="CressFlow"/>.
    /// Only the first Scenario block is processed (multi-scenario is V11).
    /// </summary>
    public CressFlow Ingest(string featureText)
    {
        var lines = featureText.ReplaceLineEndings("\n").Split('\n');

        var tags = new List<string>();
        string name = string.Empty;
        var summaryLines = new List<string>();
        var whenActions = new List<FlowAction>();
        var thenExpectations = new List<FlowExpectation>();

        // Parser state machine
        var state = ParseState.Start;
        GherkinKeyword currentSection = GherkinKeyword.When;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();

            // Tag lines (before Feature or Scenario)
            if (trimmed.StartsWith('@') && state is ParseState.Start or ParseState.InFeatureDescription)
            {
                var lineTags = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.StartsWith('@'))
                    .Select(t => t.TrimStart('@'));
                tags.AddRange(lineTags);
                continue;
            }

            // Feature: <name>
            if (trimmed.StartsWith("Feature:", StringComparison.OrdinalIgnoreCase))
            {
                name = trimmed["Feature:".Length..].Trim();
                state = ParseState.InFeatureDescription;
                continue;
            }

            // Description lines between Feature: and Scenario:
            if (state == ParseState.InFeatureDescription)
            {
                if (trimmed.StartsWith("Scenario", StringComparison.OrdinalIgnoreCase))
                {
                    // Fall through to scenario handling below
                    state = ParseState.InScenario;
                    currentSection = GherkinKeyword.When; // reset; Given will flip it
                    // Don't continue — fall through to scenario step handling
                }
                else if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    summaryLines.Add(trimmed);
                    continue;
                }
                else
                {
                    // blank line in description — keep state
                    continue;
                }
            }

            if (state != ParseState.InScenario)
            {
                continue;
            }

            // Inside scenario — parse step lines
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Detect step keyword
            GherkinKeyword? lineKeyword = null;
            string stepText = trimmed;

            if (TryStripKeyword(trimmed, "Given", out var afterGiven))
            {
                lineKeyword = GherkinKeyword.Given;
                stepText = afterGiven;
            }
            else if (TryStripKeyword(trimmed, "When", out var afterWhen))
            {
                lineKeyword = GherkinKeyword.When;
                stepText = afterWhen;
            }
            else if (TryStripKeyword(trimmed, "Then", out var afterThen))
            {
                lineKeyword = GherkinKeyword.Then;
                stepText = afterThen;
            }
            else if (TryStripKeyword(trimmed, "And", out var afterAnd))
            {
                lineKeyword = GherkinKeyword.And; // inherits currentSection
                stepText = afterAnd;
            }
            else if (TryStripKeyword(trimmed, "But", out var afterBut))
            {
                lineKeyword = GherkinKeyword.But;
                stepText = afterBut;
            }

            if (lineKeyword is null)
            {
                // Not a step line (e.g. "Scenario: ..." was already handled above)
                continue;
            }

            // Resolve effective section
            var effectiveSection = lineKeyword switch
            {
                GherkinKeyword.And or GherkinKeyword.But => currentSection,
                _ => lineKeyword.Value
            };

            // Update currentSection for And/But tracking
            if (lineKeyword != GherkinKeyword.And && lineKeyword != GherkinKeyword.But)
            {
                currentSection = effectiveSection;
            }

            // Handle # TODO: <op> lines (stub steps)
            if (stepText.StartsWith("# TODO:", StringComparison.OrdinalIgnoreCase))
            {
                var stubOp = stepText["# TODO:".Length..].Trim();
                var stubWith = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["summary"] = "TODO: review"
                };
                AddStep(effectiveSection, stubOp, stubWith, whenActions, thenExpectations);
                continue;
            }

            // Skip pure comment lines that are not TODO stubs
            if (stepText.StartsWith('#'))
            {
                continue;
            }

            // Reverse-lookup in phrase library
            var (stepOp, withBlock) = ReverseLookup(stepText);

            if (stepOp is null)
            {
                // Unknown step — emit a stub with op = "unknown" and the raw text
                var unknownWith = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["text"] = stepText,
                    ["summary"] = "TODO: review"
                };
                AddStep(effectiveSection, "unknown", unknownWith, whenActions, thenExpectations);
            }
            else
            {
                AddStep(effectiveSection, stepOp, withBlock!, whenActions, thenExpectations);
            }
        }

        var summary = summaryLines.Count > 0
            ? string.Join(" ", summaryLines).Trim()
            : null;

        return new CressFlow
        {
            Version = 1,
            Id = SlugifyName(name),
            Name = name,
            Summary = summary,
            Tags = tags,
            When = whenActions,
            Then = thenExpectations
        };
    }

    // -------------------------------------------------------------------------
    // Phrase reverse-lookup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scan all phrases in the library and find the best match for
    /// <paramref name="stepText"/>. Returns (stepOp, withBlock) or (null, null).
    /// "Best" = the phrase whose template regex produces the most named captures.
    /// </summary>
    private (string? StepOp, Dictionary<string, string>? WithBlock) ReverseLookup(string stepText)
    {
        var allPhrases = _library.GetAllPhrases();

        // Score each phrase: build regex from template, attempt match, score by
        // number of captured placeholders (more = more specific).
        MatchResult? best = null;

        foreach (var phrase in allPhrases)
        {
            var (regex, keys) = TemplateToRegex(phrase.Template);
            var match = regex.Match(stepText);
            if (!match.Success)
            {
                continue;
            }

            // Capture all placeholder values
            var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var value = match.Groups[key].Value;
                if (!string.IsNullOrEmpty(value))
                {
                    with[key] = value;
                }
            }

            var score = keys.Count;

            if (best is null || score > best.Score)
            {
                best = new MatchResult(phrase.StepOp, with, score);
            }
        }

        if (best is null)
        {
            return (null, null);
        }

        return (best.StepOp, best.WithBlock);
    }

    private static readonly Dictionary<string, (Regex Regex, List<string> Keys)> _regexCache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Convert a phrase template like <c>I click {automationId}</c> to a full-match
    /// regex with named capture groups.
    /// </summary>
    private static (Regex Regex, List<string> Keys) TemplateToRegex(string template)
    {
        if (_regexCache.TryGetValue(template, out var cached))
        {
            return cached;
        }

        var keys = new List<string>();
        // Find all {placeholder} tokens
        var placeholderPattern = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);

        var regexText = new StringBuilder("^");
        var lastIndex = 0;

        foreach (Match m in placeholderPattern.Matches(template))
        {
            var literal = template[lastIndex..m.Index];
            regexText.Append(Regex.Escape(literal));
            var keyName = m.Groups[1].Value;
            keys.Add(keyName);
            // Named capture group — value can be anything except newline
            regexText.Append($"(?<{keyName}>.+?)");
            lastIndex = m.Index + m.Length;
        }

        // Append remainder
        regexText.Append(Regex.Escape(template[lastIndex..]));
        regexText.Append('$');

        var regex = new Regex(regexText.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        var result = (regex, keys);
        _regexCache[template] = result;
        return result;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool TryStripKeyword(string line, string keyword, out string rest)
    {
        var prefix = keyword + " ";
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = line[prefix.Length..];
            return true;
        }

        rest = string.Empty;
        return false;
    }

    private static void AddStep(
        GherkinKeyword section,
        string stepOp,
        Dictionary<string, string> withBlock,
        List<FlowAction> whenActions,
        List<FlowExpectation> thenExpectations)
    {
        if (section == GherkinKeyword.Then)
        {
            thenExpectations.Add(new FlowExpectation
            {
                Expect = stepOp,
                With = withBlock.Count > 0 ? withBlock : null
            });
        }
        else
        {
            // Given and When both map to the When section of the flow
            whenActions.Add(new FlowAction
            {
                Step = stepOp,
                With = withBlock.Count > 0 ? withBlock : null
            });
        }
    }

    /// <summary>Convert a human-readable name to a kebab/dot-slug suitable for a flow id.</summary>
    private static string SlugifyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "ingested-flow";
        }

        // Replace common separators with dots; lowercase; strip non-alphanumeric except dot/hyphen
        var slug = Regex.Replace(name.ToLowerInvariant(), @"[:\s]+", ".")
                        .Replace(",", string.Empty)
                        .Replace("'", string.Empty)
                        .Replace("\"", string.Empty);
        slug = Regex.Replace(slug, @"[^a-z0-9.\-]", string.Empty);
        slug = Regex.Replace(slug, @"\.{2,}", ".");
        slug = slug.Trim('.');
        return string.IsNullOrWhiteSpace(slug) ? "ingested-flow" : slug;
    }

    // -------------------------------------------------------------------------
    // Internal types
    // -------------------------------------------------------------------------

    private enum ParseState
    {
        Start,
        InFeatureDescription,
        InScenario
    }

    private sealed record MatchResult(string StepOp, Dictionary<string, string> WithBlock, int Score);
}
