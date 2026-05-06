namespace Cress.Recorder;

/// <summary>
/// Snapshot of an element's identity fields captured at event time.
/// Covers both desktop UIA fields (R1-R6) and web/ARIA fields (V3-V4).
/// </summary>
public sealed record ElementInfo
{
    // ── Desktop-native UIA fields (R1–R6) ────────────────────────────────────
    public string Name { get; init; } = string.Empty;
    public string AutomationId { get; init; } = string.Empty;
    public string ControlType { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string FrameworkId { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public int[] RuntimeId { get; init; } = [];

    // ── Web / ARIA locator fields (V3–V4) ────────────────────────────────────

    /// <summary>data-testid attribute value (web only).</summary>
    public string? TestId { get; init; }

    /// <summary>ARIA role (explicit or implicit, e.g. "button", "textbox").</summary>
    public string? Role { get; init; }

    /// <summary>aria-label or associated label text.</summary>
    public string? Label { get; init; }

    /// <summary>Visible innerText (truncated to 80 chars).</summary>
    public string? Text { get; init; }

    /// <summary>Input placeholder text.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Synthesised CSS path (fallback locator, web only).</summary>
    public string? CssSelector { get; init; }

    /// <summary>DOM XPath expression (web only).</summary>
    public string? XPath { get; init; }

    /// <summary>Lower-case HTML tag name (e.g. "input", "button"), web only.</summary>
    public string? TagName { get; init; }

    public override string ToString()
        => !string.IsNullOrWhiteSpace(AutomationId)
            ? $"{ControlType}[{AutomationId}]"
            : !string.IsNullOrWhiteSpace(TestId)
                ? $"[data-testid={TestId}]"
                : !string.IsNullOrWhiteSpace(Name)
                    ? $"{ControlType}[name='{Name}']"
                    : ControlType;
}
