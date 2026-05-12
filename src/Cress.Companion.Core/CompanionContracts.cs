using Cress.Recorder.Inference;

namespace Cress.Companion;

public enum CompanionSessionStatus
{
    Recording,
    Paused,
    Stopped,
    Faulted
}

public sealed record CompanionTargetInfo
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public string? MainModuleFileName { get; init; }
    public bool IsAttachable { get; init; } = true;
}

public sealed record CompanionWindowBounds(int Left, int Top, int Width, int Height);

public sealed record CompanionWindowState
{
    public string WindowTitle { get; init; } = string.Empty;
    public CompanionWindowBounds? Bounds { get; init; }
    public bool IsVisible { get; init; }
}

public sealed record CompanionSessionSnapshot
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public CompanionSessionStatus Status { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public TimeSpan Elapsed { get; init; }
    public int CapturedEventCount { get; init; }
    public int InferredStepCount { get; init; }
    public int SuppressedEventCount { get; init; }
    public string LastEventSummary { get; init; } = "No events yet";
    public string LastStepSummary { get; init; } = "No steps inferred yet";
    public string? ErrorMessage { get; init; }
    public CompanionWindowBounds? WindowBounds { get; init; }
    public bool IsWindowVisible { get; init; }
    public bool OverlayEnabled { get; init; }
    public string? PreviewImageDataUrl { get; init; }
    public IReadOnlyList<InferredStep> InferredSteps { get; init; } = [];
}

public sealed record CompanionServiceSnapshot
{
    public bool IsAvailable { get; init; } = true;
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public IReadOnlyList<CompanionSessionSnapshot> Sessions { get; init; } = [];
}
