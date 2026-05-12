using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Gherkin.Phrases;

/// <summary>
/// Manages the mapping from step operations to Gherkin phrase templates.
/// Ships a built-in default set; project-level overrides are loaded from
/// .cress/phrases.yaml when present.
/// </summary>
public sealed class PhraseLibrary
{
    private readonly List<StepPhrase> _phrases;

    private PhraseLibrary(List<StepPhrase> phrases)
    {
        _phrases = phrases;
    }

    /// <summary>Build the library from the embedded defaults only.</summary>
    public static PhraseLibrary CreateDefault() => new(BuildDefaults());

    /// <summary>
    /// Build the library from the embedded defaults, then merge any overrides
    /// found at <paramref name="overrideYamlPath"/>.
    /// </summary>
    public static PhraseLibrary CreateWithOverrides(string overrideYamlPath)
    {
        var phrases = BuildDefaults();
        if (File.Exists(overrideYamlPath))
        {
            var overrides = LoadOverrides(overrideYamlPath);
            // Override: same StepOp replaces the first default with no RequiredKeys,
            // or is appended as a new variant.
            foreach (var @override in overrides)
            {
                var idx = phrases.FindIndex(p =>
                    string.Equals(p.StepOp, @override.StepOp, StringComparison.OrdinalIgnoreCase)
                    && (p.RequiredKeys is null || p.RequiredKeys.Count == 0)
                    && (@override.RequiredKeys is null || @override.RequiredKeys.Count == 0));
                if (idx >= 0)
                {
                    phrases[idx] = @override;
                }
                else
                {
                    phrases.Add(@override);
                }
            }
        }

        return new PhraseLibrary(phrases);
    }

    /// <summary>
    /// Returns every phrase in the library (used by the ingester for reverse-lookup).
    /// </summary>
    public IReadOnlyList<StepPhrase> GetAllPhrases() => _phrases.AsReadOnly();

    /// <summary>
    /// Resolve the best-matching phrase for a given step name and with-block keys.
    /// Priority: phrase with the most RequiredKeys that are all satisfied wins.
    /// Returns null if no phrase is registered for the step.
    /// </summary>
    public StepPhrase? Resolve(string stepOp, IReadOnlyDictionary<string, string> withBlock)
    {
        var candidates = _phrases
            .Where(p => string.Equals(p.StepOp, stepOp, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // Prefer phrases whose RequiredKeys are all present in the with-block.
        // Among those, prefer the one with the most required keys (most specific).
        var matching = candidates
            .Where(p => p.RequiredKeys is null
                        || p.RequiredKeys.All(k => withBlock.ContainsKey(k)))
            .OrderByDescending(p => p.RequiredKeys?.Count ?? 0)
            .ToList();

        return matching.FirstOrDefault() ?? candidates.First();
    }

    /// <summary>Expand a phrase template using the step's with-block values.</summary>
    public static string Expand(StepPhrase phrase, IReadOnlyDictionary<string, string> withBlock)
    {
        var text = phrase.Template;
        foreach (var (key, value) in withBlock)
        {
            text = text.Replace("{" + key + "}", HumanizeInputValue(key, value), StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    // -------------------------------------------------------------------------
    // Default phrase library (~20 built-in entries)
    // -------------------------------------------------------------------------

    private static List<StepPhrase> BuildDefaults() =>
    [
        // UI — attachment / launch
        new("browser.navigate", GherkinKeyword.When,  "the user opens {url}", ["url"]),

        new("ui.attach",      GherkinKeyword.Given, "the \"{windowTitle}\" window is open in the {processName} process", ["processName", "windowTitle"]),
        new("ui.attach",      GherkinKeyword.Given, "the {processName} application is open"),
        new("ui.launch",      GherkinKeyword.Given, "the user launches the {application} application", ["application"]),
        new("ui.launch",      GherkinKeyword.Given, "I launch the \"{application}\" application", ["application"]),
        new("ui.launch",      GherkinKeyword.Given, "the user launches {path}", ["path"]),
        new("ui.launch",      GherkinKeyword.Given, "I launch {path}", ["path"]),
        new("ui.close",       GherkinKeyword.When,  "the user closes the application"),
        new("ui.close",       GherkinKeyword.When,  "I close the application"),

        // UI — interactions (most specific first via RequiredKeys)
        new("ui.click",       GherkinKeyword.When,  "I click the \"{testId}\" element",     ["testId"]),
        new("ui.click",       GherkinKeyword.When,  "I click the {role} \"{label}\"",        ["role", "label"]),
        new("ui.click",       GherkinKeyword.When,  "I click {selector}",                    ["selector"]),
        new("ui.click",       GherkinKeyword.When,  "I click {automationId}"),

        new("ui.invoke",      GherkinKeyword.When,  "the user clicks the {role} \"{label}\"", ["role", "label"]),
        new("ui.invoke",      GherkinKeyword.When,  "the user clicks the {role} control",   ["role"]),
        new("ui.invoke",      GherkinKeyword.When,  "the user clicks the element labeled \"{label}\"", ["label"]),
        new("ui.invoke",      GherkinKeyword.When,  "the user clicks the element matching {cssSelector}", ["cssSelector"]),
        new("ui.invoke",      GherkinKeyword.When,  "the user clicks the element matching {xpath}", ["xpath"]),
        new("ui.invoke",      GherkinKeyword.When,  "the user clicks the {selector}",       ["selector"]),
        new("ui.invoke",      GherkinKeyword.When,  "I invoke {selector}",                   ["selector"]),
        new("ui.invoke",      GherkinKeyword.When,  "the user clicks the {automationId}"),
        new("ui.invoke",      GherkinKeyword.When,  "I invoke {automationId}"),

        new("ui.fill",        GherkinKeyword.When,  "the user fills the {label} field with \"{value}\"", ["label", "value"]),
        new("ui.fill",        GherkinKeyword.When,  "the user fills {testId} with \"{value}\"", ["testId", "value"]),
        new("ui.fill",        GherkinKeyword.When,  "the user fills the element matching {cssSelector} with \"{value}\"", ["cssSelector", "value"]),
        new("ui.fill",        GherkinKeyword.When,  "the user fills the {testId} with \"{value}\"", ["testId"]),
        new("ui.fill",        GherkinKeyword.When,  "the user fills the {automationId} with \"{value}\""),
        new("ui.fill",        GherkinKeyword.When,  "the user fills the {selector} with \"{value}\"", ["selector", "value"]),

        new("ui.press-key",   GherkinKeyword.When,  "the user presses {key}"),
        new("ui.press-key",   GherkinKeyword.When,  "I press {key}"),
        new("ui.screenshot",  GherkinKeyword.When,  "the user captures a screenshot named \"{name}\"", ["name"]),
        new("ui.screenshot",  GherkinKeyword.When,  "the user captures a screenshot"),
        new("ui.screenshot",  GherkinKeyword.When,  "I take a screenshot"),

        // UI — assertions (most specific first)
        new("ui.assert-text", GherkinKeyword.Then,  "the {testId} should display \"{expected}\"", ["testId", "expected"]),
        new("ui.assert-text", GherkinKeyword.Then,  "the \"{testId}\" element should show \"{expected}\"", ["testId", "expected"]),
        new("ui.assert-text", GherkinKeyword.Then,  "the {testId} should display \"{text}\"", ["testId", "text"]),
        new("ui.assert-text", GherkinKeyword.Then,  "the \"{testId}\" element should show \"{text}\"", ["testId", "text"]),
        new("ui.assert-text", GherkinKeyword.Then,  "the {testId} should display \"{expected}\"", ["testId"]),
        new("ui.assert-text", GherkinKeyword.Then,  "the \"{testId}\" element should show \"{expected}\"", ["testId"]),
        new("ui.assert-text", GherkinKeyword.Then,  "the {automationId} should display \"{text}\"", ["automationId", "text"]),
        new("ui.assert-text", GherkinKeyword.Then,  "the {automationId} should display \"{expected}\""),
        new("ui.assert-text", GherkinKeyword.Then,  "the {selector} should display \"{expected}\"", ["selector", "expected"]),
        new("ui.assert-text", GherkinKeyword.Then,  "the {selector} should display \"{text}\"", ["selector", "text"]),

        new("ui.assert-window-title", GherkinKeyword.Then, "the window title should be \"{title}\""),

        // HTTP — actions
        new("http.get",       GherkinKeyword.When,  "I GET {url}"),
        new("http.post",      GherkinKeyword.When,  "I POST {url} with body {body}"),

        // HTTP — assertions
        new("http.assert-status",        GherkinKeyword.Then, "the response status should be {status}"),
        new("http.assert-json",          GherkinKeyword.Then, "the response JSON {path} should equal \"{equals}\""),
        new("http.assert-body-contains", GherkinKeyword.Then, "the response body should contain \"{text}\""),
        new("http.assert-header",        GherkinKeyword.Then, "the response header {name} should be \"{value}\""),
    ];

    private static string HumanizeInputValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return key.ToLowerInvariant() switch
        {
            "selector" or "automationid" or "testid" => HumanizeUiTarget(value),
            _ => value
        };
    }

    private static string HumanizeUiTarget(string value)
    {
        var trimmed = value.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return value;
        }

        if (trimmed.StartsWith("num", StringComparison.OrdinalIgnoreCase)
            && trimmed.EndsWith("Button", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(trimmed[3..^6], out var number))
        {
            return $"number {number} button";
        }

        if (trimmed.EndsWith("Button", StringComparison.OrdinalIgnoreCase))
        {
            return $"{HumanizeWords(trimmed[..^6], false)} button";
        }

        if (trimmed.EndsWith("Results", StringComparison.OrdinalIgnoreCase))
        {
            return $"{HumanizeWords(trimmed, true)} accessibility text";
        }

        return HumanizeWords(trimmed, true);
    }

    private static string HumanizeWords(string value, bool titleCase)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0
                && char.IsUpper(character)
                && (char.IsLower(value[index - 1]) || char.IsDigit(value[index - 1])))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        var words = builder.ToString().Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(words))
        {
            return value;
        }

        return titleCase
            ? string.Join(' ', words.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()))
            : words.ToLowerInvariant();
    }

    // -------------------------------------------------------------------------
    // Override loading from .cress/phrases.yaml
    // -------------------------------------------------------------------------

    private static List<StepPhrase> LoadOverrides(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var doc = deserializer.Deserialize<PhrasesDocument>(yaml);
        if (doc?.Phrases is null)
        {
            return [];
        }

        return doc.Phrases.Select(entry =>
        {
            var keyword = Enum.TryParse<GherkinKeyword>(entry.Keyword, ignoreCase: true, out var kw)
                ? kw
                : GherkinKeyword.When;

            return new StepPhrase(
                entry.StepOp ?? string.Empty,
                keyword,
                entry.Template ?? string.Empty,
                entry.RequiredKeys);
        }).ToList();
    }

    // -------------------------------------------------------------------------
    // YAML deserialization helpers (internal only)
    // -------------------------------------------------------------------------

    private sealed class PhrasesDocument
    {
        public List<PhraseEntry>? Phrases { get; set; }
    }

    private sealed class PhraseEntry
    {
        public string? StepOp { get; set; }
        public string? Keyword { get; set; }
        public string? Template { get; set; }
        public List<string>? RequiredKeys { get; set; }
    }
}
