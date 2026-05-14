using Cress.Specs;

namespace Cress.UnitTests;

public sealed class CapabilityParserTests
{
    [Fact]
    public void Parse_ParsesCapabilityMarkdown()
    {
        const string content = """
---
version: 1
id: example
owner: Platform
risk: low
tags:
  - example
---

# Capability: Example capability

## Rules

- System must be available.

## Acceptance Criteria

### EXAMPLE-AC1

Given the system is available, when a user opens the app, then the app is visible.
""";

        var parser = new CapabilityParser();

        var result = parser.Parse(content, "capabilities\\example.md");

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("example", result.Value!.Id);
        Assert.Equal("Example capability", result.Value.Name);
        Assert.Single(result.Value.Rules!);
        Assert.Single(result.Value.AcceptanceCriteria!);
        Assert.Equal("EXAMPLE-AC1", result.Value.AcceptanceCriteria![0].Id);
    }

    [Fact]
    public void Parse_reports_missing_front_matter()
    {
        var parser = new CapabilityParser();

        var result = parser.Parse("# Capability: Missing front matter", "capabilities\\broken.md");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CAP001", diagnostic.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Parse_reports_invalid_yaml_front_matter()
    {
        const string content = """
        ---
        version: [
        ---

        # Capability: Broken
        """;

        var parser = new CapabilityParser();

        var result = parser.Parse(content, "capabilities\\broken.md");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CAP002", diagnostic.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Parse_collects_required_field_diagnostics_and_combines_acceptance_paragraphs()
    {
        const string content = """
        ---
        version: 0
        ---

        ## Rules

        - Top level rule
          - Nested detail

        ## Acceptance Criteria

        ### AC-1

        First sentence.

        Second sentence.
        """;

        var parser = new CapabilityParser();

        var result = parser.Parse(content, "capabilities\\incomplete.md");

        Assert.NotNull(result.Value);
        Assert.Collection(
            result.Diagnostics.OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal),
            diagnostic => Assert.Equal("CAP003", diagnostic.Code),
            diagnostic => Assert.Equal("CAP004", diagnostic.Code),
            diagnostic => Assert.Equal("CAP005", diagnostic.Code));
        Assert.Equal("Top level rule Nested detail", Assert.Single(result.Value!.Rules!));
        Assert.Equal("First sentence. Second sentence.", Assert.Single(result.Value.AcceptanceCriteria!).Description);
    }
}
