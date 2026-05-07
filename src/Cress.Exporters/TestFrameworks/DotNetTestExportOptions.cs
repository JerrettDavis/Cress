namespace Cress.Exporters.TestFrameworks;

public sealed record DotNetTestExportOptions
{
    public string Namespace { get; init; } = "Cress.Generated";
    public string? ClassName { get; init; }
    public string? Profile { get; init; }
    public string ProjectPath { get; init; } = ".";
}
