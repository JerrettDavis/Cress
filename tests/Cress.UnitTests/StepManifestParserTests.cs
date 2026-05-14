using Cress.Specs;

namespace Cress.UnitTests;

public sealed class StepManifestParserTests
{
    [Fact]
    public void Parse_reports_invalid_yaml()
    {
        var parser = new StepManifestParser();

        var result = parser.Parse("steps: [", "steps\\broken.yaml");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP001", diagnostic.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Parse_collects_validation_diagnostics_and_sets_source_files()
    {
        const string content = """
        version: 0
        steps:
          - name:
            implementation:
              plugin: sample
          - name: duplicate.step
            implementation:
              plugin: sample
          - name: duplicate.step
            implementation:
              plugin: sample
          - name: missing.operation
            implementation:
              plugin: sample
        """;

        var parser = new StepManifestParser();

        var result = parser.Parse(content, "steps\\invalid.yaml");

        Assert.NotNull(result.Value);
        Assert.All(result.Value!.Steps, step => Assert.Equal("steps\\invalid.yaml", step.SourceFile));
        Assert.Collection(
            result.Diagnostics.OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal),
            diagnostic => Assert.Equal("STP002", diagnostic.Code),
            diagnostic => Assert.Equal("STP003", diagnostic.Code),
            diagnostic => Assert.Equal("STP004", diagnostic.Code),
            diagnostic => Assert.Equal("STP005", diagnostic.Code),
            diagnostic => Assert.Equal("STP005", diagnostic.Code),
            diagnostic => Assert.Equal("STP005", diagnostic.Code));
    }
}
