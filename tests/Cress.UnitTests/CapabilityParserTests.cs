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
}
