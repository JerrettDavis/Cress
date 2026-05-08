using System.Text;
using Cress.Studio.ViewModels;

namespace Cress.Studio.Services;

public static class FlowGherkinPreviewBuilder
{
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

        foreach (var action in document.Actions.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            builder.AppendLine($"    When {BuildExecutablePhrase(action.Name, action.InputsText)}");
        }

        foreach (var expectation in document.Expectations.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            builder.AppendLine($"    Then {BuildExecutablePhrase(expectation.Name, expectation.InputsText)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildFixturePhrase(FlowDocumentViewModel.EditableFixtureRow fixture)
    {
        var subject = Coalesce(fixture.Alias, "fixture");
        var source = Coalesce(fixture.Use, fixture.Source, "configured data");
        var target = string.IsNullOrWhiteSpace(fixture.For) ? string.Empty : $" for {fixture.For}";
        return $"{subject} uses {source}{target}";
    }

    private static string BuildExecutablePhrase(string name, string? inputsText)
    {
        var inputs = inputsText?
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray() ?? [];

        return inputs.Length == 0
            ? name
            : $"{name} with {string.Join(", ", inputs)}";
    }

    private static string Coalesce(params string?[] candidates)
        => candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
