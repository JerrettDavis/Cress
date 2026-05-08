namespace Cress.Recorder.Inference;

/// <summary>
/// The recording domain that determines event-to-step mapping rules.
/// </summary>
public enum InferenceDomain
{
    /// <summary>
    /// Desktop UIA recording (Windows UI Automation). Original behaviour; all existing tests
    /// rely on this domain. AssertionTargetAutomationId is relevant here.
    /// </summary>
    Desktop,

    /// <summary>
    /// Web browser recording (Playwright CDP). Uses ARIA/testId locator preference
    /// order and treats all ValueChanged events as SetValue (fills), never assertions.
    /// Fill events are debounced with the longer <see cref="InferenceOptions.WebDebounceWindow"/>.
    /// </summary>
    Web,
}

/// <summary>
/// Controls how <see cref="StepInferenceEngine"/> processes a sequence of
/// <see cref="RecordedEvent"/> objects into <see cref="InferredStep"/> objects.
/// </summary>
public sealed record InferenceOptions
{
    /// <summary>
    /// The recording domain. Defaults to <see cref="InferenceDomain.Desktop"/>
    /// to preserve backward-compatible behaviour for all existing tests and workflows.
    /// </summary>
    public InferenceDomain Domain { get; init; } = InferenceDomain.Desktop;

    /// <summary>
    /// When set, events whose <see cref="RecordedEvent.Element"/> ProcessId does not match
    /// are discarded before inference. Leave null to accept events from all processes.
    /// </summary>
    public int? TargetProcessId { get; init; }

    /// <summary>
    /// Identical consecutive events within this window are collapsed to a single event.
    /// Identity is defined as: same <see cref="EventKind"/> + same element RuntimeId +
    /// same <see cref="RecordedEvent.Value"/> / <see cref="RecordedEvent.Key"/>.
    /// Defaults to 50 ms (conservative; avoids double-event noise without hiding real repeated clicks).
    /// </summary>
    public TimeSpan DebounceWindow { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Debounce window applied to consecutive <see cref="EventKind.ValueChanged"/> events
    /// on the same element when <see cref="Domain"/> is <see cref="InferenceDomain.Web"/>.
    /// Human typing has natural inter-key pauses longer than the desktop debounce window,
    /// so a longer window (250 ms default) collapses a "hello" keystroke stream into one fill.
    /// The last value in the window is kept (not the first, unlike the standard debounce).
    /// </summary>
    public TimeSpan WebDebounceWindow { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// When true (default), <see cref="EventKind.FocusChanged"/> events are discarded.
    /// Focus events are almost always noise and rarely correspond to a user-visible action.
    /// </summary>
    public bool IgnoreFocusEvents { get; init; } = true;

    /// <summary>
    /// When set, <see cref="EventKind.ValueChanged"/> events (and PropertyChanged events
    /// carrying a new value) on the element whose AutomationId matches this string are
    /// emitted as <see cref="StepKind.AssertText"/> steps rather than <see cref="StepKind.SetValue"/> steps.
    /// For Calculator this is <c>"CalculatorResults"</c>.
    /// Only applies when <see cref="Domain"/> is <see cref="InferenceDomain.Desktop"/>.
    /// </summary>
    public string? AssertionTargetAutomationId { get; init; }
}
