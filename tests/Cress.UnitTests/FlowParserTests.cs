using Cress.Specs;

namespace Cress.UnitTests;

public sealed class FlowParserTests
{
    [Fact]
    public void Parse_ParsesFlowYaml()
    {
        const string content = """
version: 1
id: example-flow
name: Example flow
capability: example
when:
  - step: app.open
then:
  - expect: app.is_visible
""";

        var parser = new FlowParser();

        var result = parser.Parse(content, "flows\\example.flow.yaml");

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("example-flow", result.Value!.Id);
        Assert.Equal("example", result.Value.CapabilityId);
        Assert.Single(result.Value.When);
        Assert.Single(result.Value.Then);
    }

    [Fact]
    public void Parse_reports_invalid_yaml()
    {
        var parser = new FlowParser();

        var result = parser.Parse("when: [", "flows\\broken.flow.yaml");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("FLW001", diagnostic.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Parse_collects_required_field_and_fixture_diagnostics()
    {
        const string content = """
        version: 0
        fixtures:
          "":
            use: shared.fixture
          invalid:
            {}
        when:
          - step:
        then:
          - expect:
        """;

        var parser = new FlowParser();

        var result = parser.Parse(content, "flows\\invalid.flow.yaml");

        Assert.NotNull(result.Value);
        Assert.Collection(
            result.Diagnostics.OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal),
            diagnostic => Assert.Equal("FLW002", diagnostic.Code),
            diagnostic => Assert.Equal("FLW003", diagnostic.Code),
            diagnostic => Assert.Equal("FLW004", diagnostic.Code),
            diagnostic => Assert.Equal("FLW007", diagnostic.Code),
            diagnostic => Assert.Equal("FLW008", diagnostic.Code),
            diagnostic => Assert.Equal("FLW009", diagnostic.Code),
            diagnostic => Assert.Equal("FLW010", diagnostic.Code));
    }

    [Fact]
    public void Parse_requires_actions_and_expectations()
    {
        const string content = """
        version: 1
        id: no-steps
        name: No steps
        """;

        var parser = new FlowParser();

        var result = parser.Parse(content, "flows\\empty.flow.yaml");

        Assert.Collection(
            result.Diagnostics.OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal),
            diagnostic => Assert.Equal("FLW005", diagnostic.Code),
            diagnostic => Assert.Equal("FLW006", diagnostic.Code));
    }
}
