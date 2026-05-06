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
}
