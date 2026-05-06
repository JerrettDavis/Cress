namespace Cress.Core.Models;

public record Diagnostic
{
    public DiagnosticSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? File { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string? Details { get; init; }
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public record OperationResult<T>
{
    public T? Value { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error) && Value is not null;
}

public record ValidationResult
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
