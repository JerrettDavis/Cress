using System.Text;
using Cress.Gherkin.Phrases;
using Cress.Studio.ViewModels;

namespace Cress.Studio.Services;

public static class FlowGherkinPreviewBuilder
{
    private static readonly PhraseLibrary PhraseLibrary = PhraseLibrary.CreateDefault();

    public static string Build(FlowDocumentViewModel? document)
    {
        if (document is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Feature: {Coalesce(document.Name, "Untitled flow")}");

        if (!string.IsNullOrWhiteSpace(document.Summary))
        {
            builder.AppendLine($"  {document.Summary}");
        }

        builder.AppendLine();
        builder.AppendLine($"  Scenario: {Coalesce(document.Name, document.Id, "Unnamed scenario")}");

        foreach (var fixture in document.Fixtures
                     .Where(item => !string.IsNullOrWhiteSpace(item.Alias) || !string.IsNullOrWhiteSpace(item.Use) || !string.IsNullOrWhiteSpace(item.Source)))
        {
            builder.AppendLine($"    Given {BuildFixturePhrase(fixture)}");
        }

        AppendExecutablePhrases(builder, document.Actions, GherkinKeyword.When);
        AppendExecutablePhrases(builder, document.Expectations, GherkinKeyword.Then);

        return builder.ToString().TrimEnd();
    }

    public static string BuildExecutablePreview(string? name, string? inputsText, GherkinKeyword fallbackKeyword = GherkinKeyword.When)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var inputs = ParseInputs(inputsText);
        var phrase = PhraseLibrary.Resolve(name, inputs);
        var text = phrase is null
            ? BuildFallbackExecutablePhrase(name, inputs)
            : PhraseLibrary.Expand(phrase, inputs);
        var keyword = phrase?.Keyword ?? fallbackKeyword;
        return $"{keyword} {text}";
    }

    private static string BuildFixturePhrase(FlowDocumentViewModel.EditableFixtureRow fixture)
    {
        var subject = Coalesce(fixture.Alias, "fixture");
        var source = Coalesce(fixture.Use, fixture.Source, "configured data");
        var target = string.IsNullOrWhiteSpace(fixture.For) ? string.Empty : $" for {fixture.For}";
        return $"{subject} uses {source}{target}";
    }

    private static void AppendExecutablePhrases(
        StringBuilder builder,
        IEnumerable<FlowDocumentViewModel.EditableExecutableRow> executables,
        GherkinKeyword fallbackKeyword)
    {
        var isFirst = true;
        foreach (var executable in executables.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            var inputs = ParseInputs(executable.InputsText);
            var phrase = PhraseLibrary.Resolve(executable.Name, inputs);
            var text = phrase is null
                ? BuildFallbackExecutablePhrase(executable.Name, inputs)
                : PhraseLibrary.Expand(phrase, inputs);
            var keyword = isFirst
                ? phrase?.Keyword ?? fallbackKeyword
                : GherkinKeyword.And;

            builder.AppendLine($"    {keyword} {text}");
            isFirst = false;
        }
    }

    private static string Coalesce(params string?[] candidates)
        => candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IReadOnlyDictionary<string, string> ParseInputs(string? inputsText)
    {
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(inputsText))
        {
            return inputs;
        }

        foreach (var line in inputsText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                inputs[key] = value;
            }
        }

        return inputs;
    }

    private static string BuildFallbackExecutablePhrase(string name, IReadOnlyDictionary<string, string> inputs)
        => inputs.Count == 0
            ? name
            : $"{name} with {string.Join(", ", inputs.Select(pair => $"{pair.Key}={pair.Value}"))}";
}
