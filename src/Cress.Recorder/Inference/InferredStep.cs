namespace Cress.Recorder.Inference;

/// <summary>
/// The vocabulary of step kinds the inference engine can produce.
/// Maps directly to operations supported by <c>FlaUiRuntimeDriver</c>.
/// </summary>
public enum StepKind
{
    /// <summary>Invoke/click a UI element (button, menu item, etc.).</summary>
    Click,

    /// <summary>Assert that a text element displays a specific value.</summary>
    AssertText,

    /// <summary>Simulate a keyboard key press.</summary>
    PressKey,

    /// <summary>Wait for a window with a given title to appear.</summary>
    WaitForWindow,

    /// <summary>Set the value of an editable element.</summary>
    SetValue,

    /// <summary>Navigate a browser to a URL (web recorder only).</summary>
    Navigate,
}

/// <summary>
/// Describes which element to act on.
/// All fields are nullable; at least one of <see cref="AutomationId"/> or <see cref="Name"/>
/// should be non-null for the locator to be useful at replay time.
/// </summary>
public sealed record Locator
{
    // ── Desktop-native fields (R1–R6) ────────────────────────────────────────
    public string? Name { get; init; }
    public string? AutomationId { get; init; }
    public string? ControlType { get; init; }
    public string? ClassName { get; init; }

    // ── V1 additions — cross-platform / web locator strategy ─────────────────

    /// <summary>ARIA role / UIA ControlType cross-platform alias (e.g. "button", "textbox").</summary>
    public string? Role { get; init; } = null;

    /// <summary>Test data-attribute value (e.g. data-testid); maps to AutomationId for UIA.</summary>
    public string? TestId { get; init; } = null;

    /// <summary>Accessible label / aria-label / UIA Name-equivalent for labelled elements.</summary>
    public string? Label { get; init; } = null;

    /// <summary>Visible text content of the element.</summary>
    public string? Text { get; init; } = null;

    /// <summary>Raw CSS selector (web only; ignored by desktop drivers).</summary>
    public string? CssSelector { get; init; } = null;

    /// <summary>Raw XPath expression (web only; ignored by desktop drivers).</summary>
    public string? XPath { get; init; } = null;
}

/// <summary>
/// Intermediate representation of one inferred Cress flow step.
/// This is the engine's output format; YAML serialization happens in R3.
/// </summary>
public sealed record InferredStep
{
    public required StepKind Kind { get; init; }

    /// <summary>The element targeted by this step (null for <see cref="StepKind.WaitForWindow"/> and <see cref="StepKind.PressKey"/>).</summary>
    public Locator? Locator { get; init; }

    /// <summary>Expected / set value for <see cref="StepKind.AssertText"/> and <see cref="StepKind.SetValue"/>.</summary>
    public string? Value { get; init; }

    /// <summary>Key name for <see cref="StepKind.PressKey"/>.</summary>
    public string? Key { get; init; }

    /// <summary>Expected window title for <see cref="StepKind.WaitForWindow"/>.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>Destination URL for <see cref="StepKind.Navigate"/> steps (web recorder only).</summary>
    public string? NavigateUrl { get; init; }

    /// <summary>Timestamp of the source <see cref="RecordedEvent"/> — used for ordering.</summary>
    public DateTime SourceTimestamp { get; init; }

    public override string ToString() => Kind switch
    {
        StepKind.Click       => $"Click({DescribeLocator()})",
        StepKind.AssertText  => $"AssertText({DescribeLocator()}, value='{Value}')",
        StepKind.PressKey    => $"PressKey({Key})",
        StepKind.WaitForWindow => $"WaitForWindow('{WindowTitle}')",
        StepKind.SetValue    => $"SetValue({DescribeLocator()}, value='{Value}')",
        StepKind.Navigate    => $"Navigate(url='{NavigateUrl}')",
        _                    => Kind.ToString()
    };

    private string DescribeLocator()
        => Locator is null ? "(no locator)"
            : Locator.AutomationId is not null ? $"automationId={Locator.AutomationId}"
            : Locator.Name is not null ? $"name='{Locator.Name}'"
            : "(locator)";
}
