using Cress.Core.Models;
using Cress.Gherkin.Phrases;
using System.Text;

namespace Cress.Gherkin;

/// <summary>
/// Converts an in-memory <see cref="CressFlow"/> (or <see cref="NormalizedFlow"/>)
/// to a Gherkin <c>.feature</c> string.
/// </summary>
public sealed class FlowToGherkinConverter
{
    private readonly PhraseLibrary _library;

    public FlowToGherkinConverter(PhraseLibrary library)
    {
        _library = library;
    }

    /// <summary>Convert a parsed <see cref="CressFlow"/> to a .feature string.</summary>
    public string Convert(CressFlow flow)
    {
        var sb = new StringBuilder();

        // Tags
        if (flow.Tags.Count > 0)
        {
            sb.AppendLine(string.Join(" ", flow.Tags.Select(t => "@" + t)));
        }

        // Feature header
        sb.AppendLine($"Feature: {flow.Name}");
        if (!string.IsNullOrWhiteSpace(flow.Summary))
        {
            // Indent as a Gherkin description block
            foreach (var line in flow.Summary.Trim().Split('\n'))
            {
                sb.AppendLine($"  {line.TrimEnd()}");
            }
        }

        sb.AppendLine();

        // Scenario
        sb.AppendLine($"  Scenario: {flow.Name}");

        // Given section (flow.Given is a list of precondition strings, not steps)
        if (flow.Given is { Count: > 0 })
        {
            EmitGivenStrings(sb, flow.Given);
        }

        // When section
        EmitSteps(sb, flow.When.Select(a => (a.Step, a.With)), GherkinKeyword.When);

        // Then section
        EmitSteps(sb, flow.Then.Select(e => (e.Expect, e.With)), GherkinKeyword.Then);

        return sb.ToString();
    }

    /// <summary>Convert a <see cref="NormalizedFlow"/> to a .feature string.</summary>
    public string Convert(NormalizedFlow flow)
    {
        var sb = new StringBuilder();

        // Tags
        if (flow.Tags.Count > 0)
        {
            sb.AppendLine(string.Join(" ", flow.Tags.Select(t => "@" + t)));
        }

        // Feature header
        sb.AppendLine($"Feature: {flow.Name}");
        if (!string.IsNullOrWhiteSpace(flow.Summary))
        {
            foreach (var line in flow.Summary.Trim().Split('\n'))
            {
                sb.AppendLine($"  {line.TrimEnd()}");
            }
        }

        sb.AppendLine();

        // Scenario
        sb.AppendLine($"  Scenario: {flow.Name}");

        // When section (actions)
        EmitSteps(sb,
            flow.Actions.Select(a => (a.Name, (Dictionary<string, string>?)a.Inputs)),
            GherkinKeyword.When);

        // Then section (expectations)
        EmitSteps(sb,
            flow.Expectations.Select(e => (e.Name, (Dictionary<string, string>?)e.Inputs)),
            GherkinKeyword.Then);

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void EmitSteps(
        StringBuilder sb,
        IEnumerable<(string StepOp, Dictionary<string, string>? With)> steps,
        GherkinKeyword sectionKeyword)
    {
        var isFirst = true;
        foreach (var (stepOp, with) in steps)
        {
            var withBlock = (IReadOnlyDictionary<string, string>?)with
                            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var phrase = _library.Resolve(stepOp, withBlock);
            if (phrase is null)
            {
                // Emit a TODO comment so the feature file stays syntactically valid
                // (comments don't need a keyword)
                var keyword = isFirst ? sectionKeyword.ToString() : "And";
                sb.AppendLine($"    {keyword} # TODO: add phrase for {stepOp}");
            }
            else
            {
                var naturalKeyword = isFirst ? phrase.Keyword : GherkinKeyword.And;
                var text = PhraseLibrary.Expand(phrase, withBlock);
                sb.AppendLine($"    {naturalKeyword} {text}");
            }

            isFirst = false;
        }
    }

    private static void EmitGivenStrings(StringBuilder sb, IReadOnlyList<string> given)
    {
        for (var i = 0; i < given.Count; i++)
        {
            var keyword = i == 0 ? "Given" : "And";
            sb.AppendLine($"    {keyword} {given[i]}");
        }
    }
}
