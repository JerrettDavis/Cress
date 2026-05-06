using Cress.Recorder;
using Cress.Recorder.Inference;
using Cress.Recorder.Serialization;

namespace Cress.Studio.Services;

/// <summary>
/// Testability seam for the studio-side recording service.
/// The real implementation wraps <see cref="RecordingSession"/>; tests substitute a mock.
/// </summary>
public interface IStudioRecorderService
{
    /// <summary>True while a recording session is active.</summary>
    bool IsRecording { get; }

    /// <summary>The target that is currently being (or was last) recorded.</summary>
    RecordingTargetInfo? CurrentTarget { get; }

    /// <summary>Number of events captured in the active session so far.</summary>
    int CapturedEventCount { get; }

    /// <summary>Elapsed time since recording started.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>
    /// The raw events captured so far in the active session.
    /// Empty when not recording. Thread-safe snapshot on each access.
    /// </summary>
    IReadOnlyList<RecordedEvent> CurrentEvents { get; }

    /// <summary>
    /// Live inference applied to <see cref="CurrentEvents"/>.
    /// Recomputed on each <see cref="StateChanged"/> tick (~250 ms debounce).
    /// Empty when not recording.
    /// </summary>
    IReadOnlyList<InferredStep> CurrentInferredSteps { get; }

    /// <summary>
    /// Fires when recording state changes (start/stop/new event).
    /// Debounced to ~250 ms so rapid UIA event streams don't spam Blazor renders.
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// Returns running processes that have a visible main window and can be attached to.
    /// Silently skips elevated/protected processes.
    /// </summary>
    Task<IReadOnlyList<RecordingTargetInfo>> ListAvailableTargetsAsync();

    /// <summary>Attaches to <paramref name="processId"/> and starts capturing events.</summary>
    Task StartRecordingAsync(int processId);

    /// <summary>
    /// Spawns the Node.js web recorder, opens a browser at <paramref name="url"/>,
    /// and starts streaming web events.
    /// </summary>
    /// <param name="url">Initial URL to navigate to (must be a valid absolute URL).</param>
    /// <param name="browserType">Browser to launch: "chromium", "firefox", or "webkit".</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task StartWebRecordingAsync(string url, string browserType, CancellationToken ct = default);

    /// <summary>
    /// Stops the active session and returns the inferred steps plus raw events.
    /// </summary>
    Task<RecordingResult> StopRecordingAsync();

    /// <summary>
    /// Loads the flow at <paramref name="flowFilePath"/> and executes it in-process using the
    /// project rooted at <paramref name="projectPath"/>.  Returns pass/fail + step summary.
    /// Never spawns an external process.
    /// </summary>
    Task<RecordingReplayResult> ReplayRecordedFlowAsync(string flowFilePath, string projectPath);
}

/// <summary>Information about a process that can be recorded.</summary>
public sealed record RecordingTargetInfo
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string MainWindowTitle { get; init; } = string.Empty;
    public string? MainModuleFileName { get; init; }
    public bool IsAttachable { get; init; } = true;
}

/// <summary>The output of a completed recording session.</summary>
public sealed record RecordingResult
{
    public IReadOnlyList<RecordedEvent> Events { get; init; } = [];
    public IReadOnlyList<InferredStep> Steps { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public string? ProcessName { get; init; }
}

/// <summary>The outcome of replaying a previously recorded flow file in-process.</summary>
public sealed record RecordingReplayResult
{
    /// <summary>True when all steps in the flow passed.</summary>
    public bool Passed { get; init; }

    /// <summary>One-line human-readable summary (e.g. "Passed (7 steps in 3s)").</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Per-step outcomes in execution order.</summary>
    public IReadOnlyList<string> StepResults { get; init; } = [];

    /// <summary>Wall-clock time from start of replay to completion.</summary>
    public TimeSpan Duration { get; init; }
}
