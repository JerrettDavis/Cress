using System.Text;
using Cress.Recorder.Inference;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cress.Recorder.Serialization;

/// <summary>
/// Converts a list of <see cref="InferredStep"/> objects into a YAML string that is
/// schema-compatible with <c>FlowParser</c> / <c>CressFlow</c>.
/// </summary>
public sealed class RecordedFlowSerializer
{
    /// <summary>
    /// Metadata that decorates the serialized flow.
    /// </summary>
    public sealed record RecordedFlowMetadata
    {
        /// <summary>Flow identifier, e.g. <c>calc.add-two-plus-two</c>.</summary>
        public required string Id { get; init; }

        /// <summary>Human-readable name, e.g. <c>Calculator: 2 + 2 = 4</c>.</summary>
        public required string Name { get; init; }

        /// <summary>Optional capability id this flow belongs to.</summary>
        public string? Capability { get; init; }

        /// <summary>One-sentence summary shown in the Studio.</summary>
        public string? Summary { get; init; }

        /// <summary>Tags attached to the flow. Defaults to <c>["recorded", "draft"]</c>.</summary>
        public IReadOnlyList<string> Tags { get; init; } = ["recorded", "draft"];

        /// <summary>
        /// Lifecycle status. Per PLAN.md AC-24.1.6, recorded flows are <c>draft</c> until
        /// manually reviewed and promoted.
        /// </summary>
        public string Status { get; init; } = "draft";

        /// <summary>
        /// When set, a <c>ui.attach</c> step is prepended as the first <c>when</c> action
        /// using this value as the <c>processName</c> input. Set to the process name of
        /// the application that was recorded (e.g. <c>"calc"</c>).
        /// </summary>
        public string? AttachProcessName { get; init; }
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serializes <paramref name="steps"/> to a YAML string.
    /// </summary>
    /// <param name="steps">Ordered list of inferred steps (actions + expectations).</param>
    /// <param name="metadata">Flow header metadata.</param>
    /// <returns>YAML text compatible with <c>FlowParser.Parse()</c>.</returns>
    public string Serialize(IReadOnlyList<InferredStep> steps, RecordedFlowMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(metadata);

        var actions = steps
            .Where(s => s.Kind != StepKind.AssertText)
            .Select(BuildAction)
            .Where(a => a is not null)
            .Cast<FlowActionYaml>()
            .ToList();

        // Prepend ui.attach when a process name is configured
        if (!string.IsNullOrWhiteSpace(metadata.AttachProcessName))
        {
            actions.Insert(0, new FlowActionYaml
            {
                Step = "ui.attach",
                With = new Dictionary<string, string>
                {
                    ["processName"] = metadata.AttachProcessName
                }
            });
        }

        var expectations = steps
            .Where(s => s.Kind == StepKind.AssertText)
            .Select(BuildExpectation)
            .Where(e => e is not null)
            .Cast<FlowExpectationYaml>()
            .ToList();

        // A valid flow requires at least one `when` and one `then`. If there are no
        // assertion steps, inject a placeholder so the YAML parses without FLW006.
        if (expectations.Count == 0)
        {
            expectations.Add(new FlowExpectationYaml
            {
                Expect = "ui.assert-window-title",
                With = new Dictionary<string, string>
                {
                    ["title"] = "REVIEW: replace with expected window title"
                }
            });
        }

        var document = new FlowDocumentYaml
        {
            Version = 1,
            Id = metadata.Id,
            Name = metadata.Name,
            Capability = metadata.Capability,
            Summary = metadata.Summary,
            Tags = metadata.Tags.ToList(),
            Status = metadata.Status,
            When = actions,
            Then = expectations,
        };

        return SerializeDocument(document);
    }

    /// <summary>
    /// Serializes steps to YAML and writes the result to <paramref name="filePath"/>,
    /// creating parent directories if necessary.
    /// </summary>
    public void SaveToFile(IReadOnlyList<InferredStep> steps, RecordedFlowMetadata metadata, string filePath)
    {
        var yaml = Serialize(steps, metadata);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, yaml, Encoding.UTF8);
    }

    // -------------------------------------------------------------------------
    // Step → YAML action/expectation mapping
    // -------------------------------------------------------------------------

    private static FlowActionYaml? BuildAction(InferredStep step)
    {
        return step.Kind switch
        {
            StepKind.Click => new FlowActionYaml
            {
                Step = "ui.invoke",
                With = BuildLocatorInputs(step.Locator)
            },

            StepKind.SetValue => new FlowActionYaml
            {
                Step = "ui.fill",
                With = MergeInputs(BuildLocatorInputs(step.Locator), new Dictionary<string, string>
                {
                    ["value"] = step.Value ?? string.Empty
                })
            },

            StepKind.PressKey when !string.IsNullOrWhiteSpace(step.Key) => new FlowActionYaml
            {
                Step = "ui.press-key",
                With = new Dictionary<string, string>
                {
                    ["key"] = step.Key!
                }
            },

            StepKind.WaitForWindow when !string.IsNullOrWhiteSpace(step.WindowTitle) => new FlowActionYaml
            {
                Step = "ui.wait-for-window",
                With = new Dictionary<string, string>
                {
                    ["title"] = step.WindowTitle!
                }
            },

            // Navigate → browser.navigate with the destination URL.
            // Used by both the web recorder and (rarely) the desktop recorder when
            // a browser address-bar navigation is captured.
            // Decision: we use "browser.navigate" (not "ui.navigate") because it is a
            // browser-specific operation with no UIA/desktop equivalent. All other
            // recorded actions (click, fill, press-key) are shared between ui.* and
            // browser.* drivers via the same step shape — only navigation is web-exclusive.
            StepKind.Navigate when !string.IsNullOrWhiteSpace(step.NavigateUrl) => new FlowActionYaml
            {
                Step = "browser.navigate",
                With = new Dictionary<string, string>
                {
                    ["url"] = step.NavigateUrl!
                }
            },

            // Navigate with no URL — drop silently.
            StepKind.Navigate => null,

            // AssertText steps are handled as expectations, not actions.
            StepKind.AssertText => null,

            // PressKey with no key, WaitForWindow with no title — drop silently.
            _ => null,
        };
    }

    private static FlowExpectationYaml? BuildExpectation(InferredStep step)
    {
        if (step.Kind != StepKind.AssertText)
        {
            return null;
        }

        var inputs = MergeInputs(
            BuildLocatorInputs(step.Locator),
            new Dictionary<string, string>
            {
                ["text"] = step.Value ?? string.Empty
            });

        return new FlowExpectationYaml
        {
            Expect = "ui.assert-text",
            With = inputs
        };
    }

    private static Dictionary<string, string> BuildLocatorInputs(Locator? locator)
    {
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (locator is null)
        {
            return inputs;
        }

        // Desktop-native fields
        if (!string.IsNullOrWhiteSpace(locator.AutomationId))
        {
            inputs["automationId"] = locator.AutomationId;
        }

        if (!string.IsNullOrWhiteSpace(locator.Name))
        {
            inputs["name"] = locator.Name;
        }

        if (!string.IsNullOrWhiteSpace(locator.ControlType))
        {
            inputs["controlType"] = locator.ControlType;
        }

        if (!string.IsNullOrWhiteSpace(locator.ClassName))
        {
            inputs["className"] = locator.ClassName;
        }

        // V1 additions — cross-platform / web locator strategy
        if (!string.IsNullOrWhiteSpace(locator.Role))
        {
            inputs["role"] = locator.Role;
        }

        if (!string.IsNullOrWhiteSpace(locator.TestId))
        {
            inputs["testId"] = locator.TestId;
        }

        if (!string.IsNullOrWhiteSpace(locator.Label))
        {
            inputs["label"] = locator.Label;
        }

        if (!string.IsNullOrWhiteSpace(locator.Text))
        {
            inputs["text"] = locator.Text;
        }

        if (!string.IsNullOrWhiteSpace(locator.CssSelector))
        {
            inputs["cssSelector"] = locator.CssSelector;
        }

        if (!string.IsNullOrWhiteSpace(locator.XPath))
        {
            inputs["xpath"] = locator.XPath;
        }

        return inputs;
    }

    private static Dictionary<string, string> MergeInputs(
        Dictionary<string, string> primary,
        Dictionary<string, string> additional)
    {
        var merged = new Dictionary<string, string>(primary, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in additional)
        {
            merged[k] = v;
        }

        return merged;
    }

    // -------------------------------------------------------------------------
    // YAML serialization
    // -------------------------------------------------------------------------

    private static string SerializeDocument(FlowDocumentYaml document)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .Build();

        return serializer.Serialize(document);
    }

    // -------------------------------------------------------------------------
    // Internal YAML-shaped DTOs (separate from CressFlow to avoid YamlMember
    // attribute conflicts with the parser-side models)
    // -------------------------------------------------------------------------

    private sealed class FlowDocumentYaml
    {
        public int Version { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Capability { get; set; }
        public string? Summary { get; set; }
        public List<string>? Tags { get; set; }
        public string? Status { get; set; }
        public List<FlowActionYaml> When { get; set; } = [];
        public List<FlowExpectationYaml> Then { get; set; } = [];
    }

    private sealed class FlowActionYaml
    {
        public string Step { get; set; } = string.Empty;
        public Dictionary<string, string>? With { get; set; }
    }

    private sealed class FlowExpectationYaml
    {
        public string Expect { get; set; } = string.Empty;
        public Dictionary<string, string>? With { get; set; }
    }
}
