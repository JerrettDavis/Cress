namespace Cress.Studio.Web.Services;

public sealed record StudioLiveRunEntry(
    DateTimeOffset TimestampUtc,
    string Category,
    string Headline,
    string? Detail,
    string Status,
    string? FlowName,
    string? StepName,
    string? LogLevel);
