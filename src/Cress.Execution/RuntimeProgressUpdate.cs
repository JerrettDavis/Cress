using Cress.Core.Models;

namespace Cress.Execution;

public enum RuntimeProgressKind
{
    RunStarted,
    FlowStarted,
    StepStarted,
    StepCompleted,
    Log,
    FlowCompleted,
    RunCompleted
}

public sealed record RuntimeProgressUpdate
{
    public RuntimeProgressKind Kind { get; init; }
    public string? RunId { get; init; }
    public string? FlowId { get; init; }
    public string? FlowName { get; init; }
    public string? Message { get; init; }
    public string? LogLevel { get; init; }
    public int? FlowIndex { get; init; }
    public int? FlowCount { get; init; }
    public int? StepIndex { get; init; }
    public int? StepCount { get; init; }
    public StepRunResult? Step { get; init; }
    public FlowRunResult? Flow { get; init; }
    public RunResult? Run { get; init; }
}
