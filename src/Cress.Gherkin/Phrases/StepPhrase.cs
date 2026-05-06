namespace Cress.Gherkin.Phrases;

/// <summary>
/// Maps a step operation (and optionally a required set of with-block keys)
/// to a Gherkin keyword + phrase template.
/// </summary>
/// <param name="StepOp">Step name, e.g. "ui.click".</param>
/// <param name="Keyword">The natural Gherkin keyword for this operation.</param>
/// <param name="Template">
/// Phrase template. Placeholders are {key} where key matches a with-block key.
/// Example: "I click {automationId}"
/// </param>
/// <param name="RequiredKeys">
/// Optional set of keys that must be present in the with-block for this phrase
/// to be selected.  Used to distinguish overloads (e.g. testId variant of ui.click).
/// </param>
public sealed record StepPhrase(
    string StepOp,
    GherkinKeyword Keyword,
    string Template,
    IReadOnlyList<string>? RequiredKeys = null);
