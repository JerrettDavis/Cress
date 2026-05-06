using System.Text.RegularExpressions;
using Cress.Core.Models;
using Cress.Execution;

namespace Cress.Studio.Services;

public sealed class StudioAuthoringService
{
    private static readonly Regex FlowIdPattern = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public FlowEditorAnalysis Analyze(ProjectCatalog? catalog, FlowEditorDocument document)
    {
        var diagnostics = new List<FlowEditorDiagnostic>();
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            diagnostics.Add(new FlowEditorDiagnostic("metadata", DiagnosticSeverity.Error, "Flow id is required.", "Use a stable kebab-case id."));
        }
        else if (!FlowIdPattern.IsMatch(document.Id))
        {
            diagnostics.Add(new FlowEditorDiagnostic("metadata", DiagnosticSeverity.Warning, "Flow id should use kebab-case.", "Example: checkout-happy-path."));
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            diagnostics.Add(new FlowEditorDiagnostic("metadata", DiagnosticSeverity.Error, "Flow name is required.", "Add a report-friendly title."));
        }

        if (catalog is not null && !string.IsNullOrWhiteSpace(document.CapabilityId)
            && !catalog.Capabilities.Any(item => string.Equals(item.Id, document.CapabilityId, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new FlowEditorDiagnostic("metadata", DiagnosticSeverity.Warning, $"Capability '{document.CapabilityId}' was not found.", "Pick a known capability or leave it blank."));
        }

        if (document.Actions.Count == 0)
        {
            diagnostics.Add(new FlowEditorDiagnostic("actions", DiagnosticSeverity.Warning, "The flow has no actions.", "Apply a quick template or add the first action."));
        }

        if (document.Expectations.Count == 0)
        {
            diagnostics.Add(new FlowEditorDiagnostic("expectations", DiagnosticSeverity.Warning, "The flow has no expectations.", "Add at least one expected outcome."));
        }

        foreach (var duplicate in document.Fixtures
                     .Where(item => !string.IsNullOrWhiteSpace(item.Alias))
                     .GroupBy(item => item.Alias, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new FlowEditorDiagnostic("fixtures", DiagnosticSeverity.Warning, $"Fixture alias '{duplicate.Key}' is duplicated.", "Keep aliases unique to avoid setup/cleanup confusion."));
        }

        ValidateRows("action", document.Actions, catalog?.StepRegistry, diagnostics);
        ValidateRows("expectation", document.Expectations, catalog?.StepRegistry, diagnostics);

        return new FlowEditorAnalysis
        {
            Summary = diagnostics.Count == 0
                ? "Designer is ready to save."
                : $"{diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error)} error(s), {diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning)} warning(s)",
            Diagnostics = diagnostics,
            QuickActions = BuildQuickActions(catalog)
        };
    }

    public FlowEditorDocument ApplyQuickAction(ProjectCatalog? catalog, FlowEditorDocument document, string actionId)
    {
        var updated = Clone(document);
        switch (actionId)
        {
            case "metadata.smoke":
                updated.TagsText = AddTags(updated.TagsText, "smoke", "studio");
                updated.Status ??= "draft";
                updated.Summary ??= "Created from a studio quick action.";
                break;
            case "fixture.session":
                updated.Fixtures.Add(new EditableFixture
                {
                    Alias = "session",
                    Use = catalog?.FixtureDefinitions.Keys.FirstOrDefault() ?? string.Empty
                });
                break;
            case "template.web-smoke":
                ApplyTemplate(updated,
                    ResolveStep(catalog, "browser.goto", "open", "goto", "navigate"),
                    ResolveStep(catalog, "expect_visible", "browser.expect_visible"),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["path"] = "/" },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["text"] = "Ready" });
                break;
            case "template.desktop-smoke":
                ApplyTemplate(updated,
                    ResolveStep(catalog, "app.open", "application.open"),
                    ResolveStep(catalog, "app.is_visible", "window.is_visible"),
                    null,
                    null);
                break;
            case "template.api-health":
                ApplyTemplate(updated,
                    ResolveStep(catalog, "api.request"),
                    ResolveStep(catalog, "api.status_ok"),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["method"] = "GET",
                        ["path"] = "/health"
                    },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = "200"
                    });
                break;
            case "action.screenshot":
                AppendRow(updated.Actions, ResolveStep(catalog, "browser.screenshot", "screenshot", "capture-screenshot"), null);
                break;
            case "expect.visible":
                AppendRow(updated.Expectations, ResolveStep(catalog, "expect_visible", "browser.expect_visible", "app.is_visible"), null);
                break;
        }

        return updated;
    }

    private static void ValidateRows(string prefix, IReadOnlyList<EditableExecutable> rows, StepRegistrySnapshot? registry, ICollection<FlowEditorDiagnostic> diagnostics)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var target = $"{prefix}:{index}";
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                diagnostics.Add(new FlowEditorDiagnostic(target, DiagnosticSeverity.Warning, "Step name is empty.", "Choose a step from the registry."));
                continue;
            }

            foreach (var malformedLine in row.InputsText
                         .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(line => !line.Contains('=') && !line.Contains(':')))
            {
                diagnostics.Add(new FlowEditorDiagnostic(target, DiagnosticSeverity.Warning, $"'{malformedLine}' should use key=value syntax.", "Use one input per line."));
            }

            if (registry is null || !registry.TryResolve(row.Name, out var definition))
            {
                diagnostics.Add(new FlowEditorDiagnostic(target, DiagnosticSeverity.Warning, $"Step '{row.Name}' is not registered.", "Refresh the project or pick a known step."));
                continue;
            }

            var inputs = ParseInputs(row.InputsText);
            if (definition.Inputs is null)
            {
                continue;
            }

            foreach (var requiredInput in definition.Inputs.Where(item => item.Value.Required && !inputs.ContainsKey(item.Key)))
            {
                diagnostics.Add(new FlowEditorDiagnostic(target, DiagnosticSeverity.Warning, $"'{row.Name}' is missing required input '{requiredInput.Key}'.", requiredInput.Value.Description));
            }
        }
    }

    private static IReadOnlyList<FlowQuickAction> BuildQuickActions(ProjectCatalog? catalog)
    {
        var actions = new List<FlowQuickAction>
        {
            new("metadata.smoke", "Apply smoke metadata", "Adds smoke/studio tags and starter metadata.", "Metadata")
        };

        if (catalog?.FixtureDefinitions.Count > 0)
        {
            actions.Add(new FlowQuickAction("fixture.session", "Add fixture row", "Adds a reusable fixture row.", "Fixtures"));
        }

        if (ResolveStep(catalog, "browser.goto", "open", "goto", "navigate") is not null
            && ResolveStep(catalog, "expect_visible", "browser.expect_visible") is not null)
        {
            actions.Add(new FlowQuickAction("template.web-smoke", "Web smoke template", "Adds a navigation action and a visible assertion.", "Templates"));
        }

        if (ResolveStep(catalog, "app.open", "application.open") is not null
            && ResolveStep(catalog, "app.is_visible", "window.is_visible") is not null)
        {
            actions.Add(new FlowQuickAction("template.desktop-smoke", "Desktop smoke template", "Adds an app launch action and a visibility assertion.", "Templates"));
        }

        if (ResolveStep(catalog, "api.request") is not null && ResolveStep(catalog, "api.status_ok") is not null)
        {
            actions.Add(new FlowQuickAction("template.api-health", "API health template", "Adds a request plus status expectation.", "Templates"));
        }

        if (ResolveStep(catalog, "browser.screenshot", "screenshot", "capture-screenshot") is not null)
        {
            actions.Add(new FlowQuickAction("action.screenshot", "Capture screenshot", "Adds a screenshot step.", "Actions"));
        }

        if (ResolveStep(catalog, "expect_visible", "browser.expect_visible", "app.is_visible") is not null)
        {
            actions.Add(new FlowQuickAction("expect.visible", "Visible expectation", "Adds a visible assertion.", "Expectations"));
        }

        return actions;
    }

    private static FlowEditorDocument Clone(FlowEditorDocument document)
        => new()
        {
            FilePath = document.FilePath,
            Id = document.Id,
            Name = document.Name,
            CapabilityId = document.CapabilityId,
            Summary = document.Summary,
            Status = document.Status,
            TagsText = document.TagsText,
            SourceText = document.SourceText,
            Fixtures = document.Fixtures.Select(item => new EditableFixture
            {
                Alias = item.Alias,
                Use = item.Use,
                Source = item.Source,
                For = item.For
            }).ToList(),
            Actions = document.Actions.Select(item => new EditableExecutable
            {
                Name = item.Name,
                InputsText = item.InputsText
            }).ToList(),
            Expectations = document.Expectations.Select(item => new EditableExecutable
            {
                Name = item.Name,
                InputsText = item.InputsText
            }).ToList()
        };

    private static void ApplyTemplate(FlowEditorDocument document, string? actionName, string? expectationName, IReadOnlyDictionary<string, string>? actionInputs, IReadOnlyDictionary<string, string>? expectationInputs)
    {
        AppendRow(document.Actions, actionName, actionInputs);
        AppendRow(document.Expectations, expectationName, expectationInputs);
        document.TagsText = AddTags(document.TagsText, "studio");
    }

    private static void AppendRow(ICollection<EditableExecutable> rows, string? name, IReadOnlyDictionary<string, string>? inputs)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        rows.Add(new EditableExecutable
        {
            Name = name,
            InputsText = inputs is null || inputs.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, inputs.Select(item => $"{item.Key}={item.Value}"))
        });
    }

    private static string? ResolveStep(ProjectCatalog? catalog, params string[] candidates)
    {
        if (catalog is null)
        {
            return candidates.FirstOrDefault();
        }

        foreach (var candidate in candidates)
        {
            if (catalog.StepRegistry.TryResolve(candidate, out var definition))
            {
                return definition.Name;
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseInputs(string value)
        => value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex < 0)
                {
                    separatorIndex = line.IndexOf(':');
                }

                return separatorIndex > 0
                    ? new KeyValuePair<string, string>(line[..separatorIndex].Trim(), line[(separatorIndex + 1)..].Trim())
                    : default;
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private static string AddTags(string existing, params string[] tags)
        => string.Join(", ", existing.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(tags)
            .Distinct(StringComparer.OrdinalIgnoreCase));
}

public sealed record FlowEditorAnalysis
{
    public string Summary { get; init; } = "Ready";
    public IReadOnlyList<FlowEditorDiagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<FlowQuickAction> QuickActions { get; init; } = [];
}

public sealed record FlowEditorDiagnostic(string Target, DiagnosticSeverity Severity, string Message, string? Recommendation);

public sealed record FlowQuickAction(string Id, string Title, string Description, string Category);
