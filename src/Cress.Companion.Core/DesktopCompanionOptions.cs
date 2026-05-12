namespace Cress.Companion;

public sealed record DesktopCompanionOptions
{
    public int MaxRetainedEvents { get; init; } = 256;
    public string? AssertionTargetAutomationId { get; init; }
}
