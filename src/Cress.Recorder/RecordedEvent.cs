namespace Cress.Recorder;

/// <summary>
/// The kind of UIA interaction captured.
/// </summary>
public enum EventKind
{
    Invoke,
    ValueChanged,
    FocusChanged,
    KeyDown,
    WindowOpened,

    /// <summary>
    /// A page/frame navigation was recorded (web recorder only).
    /// The <see cref="RecordedEvent.Url"/> field carries the destination URL.
    /// </summary>
    Navigate,
}

/// <summary>
/// An immutable snapshot of a single UIA event captured during a recording session.
/// </summary>
public sealed record RecordedEvent
{
    public int Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public EventKind Kind { get; init; }
    public ElementInfo Element { get; init; } = new();

    /// <summary>The new value for <see cref="EventKind.ValueChanged"/> events.</summary>
    public string? Value { get; init; }

    /// <summary>The key name for <see cref="EventKind.KeyDown"/> events.</summary>
    public string? Key { get; init; }

    /// <summary>The destination URL for <see cref="EventKind.Navigate"/> events (web recorder only).</summary>
    public string? Url { get; init; }

    public override string ToString()
        => $"[{Timestamp:HH:mm:ss.fff}] {Kind}: {Element}";
}
