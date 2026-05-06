using Cress.Recorder;
using Cress.Recorder.Inference;
using Cress.Studio.Services;

namespace Cress.Studio.Web.Tests;

/// <summary>
/// A minimal in-memory implementation of <see cref="IStudioRecorderService"/> for use in
/// bunit component tests. Never touches the OS process list or UIA subsystem.
/// </summary>
internal sealed class FakeStudioRecorderService : IStudioRecorderService
{
    /// <summary>Targets returned by <see cref="ListAvailableTargetsAsync"/>.</summary>
    public List<RecordingTargetInfo> Targets { get; set; } = [];

    public bool IsRecording { get; set; }
    public RecordingTargetInfo? CurrentTarget { get; set; }
    public int CapturedEventCount { get; set; }
    public TimeSpan Elapsed { get; set; }

    // Live event / step lists — tests can pre-populate or call SimulateEvent.
    private readonly List<RecordedEvent> _events = [];
    private readonly List<InferredStep> _steps = [];

    public IReadOnlyList<RecordedEvent> CurrentEvents => _events;
    public IReadOnlyList<InferredStep> CurrentInferredSteps => _steps;

    public event Action? StateChanged;

    // ── Web recording tracking ─────────────────────────────────────────────────

    /// <summary>URL passed to the last <see cref="StartWebRecordingAsync"/> call.</summary>
    public string? LastWebUrl { get; private set; }

    /// <summary>Browser type passed to the last <see cref="StartWebRecordingAsync"/> call.</summary>
    public string? LastWebBrowserType { get; private set; }

    /// <summary>Number of times <see cref="StartWebRecordingAsync"/> was called.</summary>
    public int WebRecordingStartCount { get; private set; }

    public Task<IReadOnlyList<RecordingTargetInfo>> ListAvailableTargetsAsync()
        => Task.FromResult<IReadOnlyList<RecordingTargetInfo>>(Targets);

    public Task StartRecordingAsync(int processId)
    {
        IsRecording = true;
        CurrentTarget = Targets.FirstOrDefault(t => t.ProcessId == processId)
                        ?? new RecordingTargetInfo { ProcessId = processId, ProcessName = "test", MainWindowTitle = "Test" };
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task StartWebRecordingAsync(string url, string browserType, CancellationToken ct = default)
    {
        LastWebUrl = url;
        LastWebBrowserType = browserType;
        WebRecordingStartCount++;
        IsRecording = true;
        CurrentTarget = new RecordingTargetInfo
        {
            ProcessId = 0,
            ProcessName = "browser",
            MainWindowTitle = $"{browserType} — {url}",
            IsAttachable = true
        };
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopRecordingAsync()
    {
        IsRecording = false;
        var result = new RecordingResult
        {
            Events = _events.ToList(),
            Steps = _steps.ToList(),
            Duration = Elapsed,
            ProcessName = CurrentTarget?.ProcessName
        };

        StateChanged?.Invoke();
        return Task.FromResult(result);
    }

    /// <summary>Push a new event into the live stream and increment the counter.</summary>
    public void SimulateEvent(RecordedEvent evt)
    {
        _events.Add(evt);
        CapturedEventCount = _events.Count;
        StateChanged?.Invoke();
    }

    /// <summary>Pre-populate the live step list for tests that need inferred steps visible.</summary>
    public void AddStep(InferredStep step)
    {
        _steps.Add(step);
    }

    /// <summary>Manually fire StateChanged so tests can verify subscriber behavior.</summary>
    public void SimulateStateChange() => StateChanged?.Invoke();

    // -------------------------------------------------------------------------
    // Replay support
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures the result returned by <see cref="ReplayRecordedFlowAsync"/>.
    /// Defaults to a passing result with one step.
    /// </summary>
    public RecordingReplayResult ReplayResult { get; set; } = new RecordingReplayResult
    {
        Passed = true,
        Summary = "Passed (1 steps in 0s)",
        StepResults = ["step1: Passed"],
        Duration = TimeSpan.FromSeconds(1)
    };

    public Task<RecordingReplayResult> ReplayRecordedFlowAsync(string flowFilePath, string projectPath)
        => Task.FromResult(ReplayResult);
}
