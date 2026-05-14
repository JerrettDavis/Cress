using Bunit;
using Cress.Studio.Services;

namespace Cress.Studio.Web.Tests;

public sealed class FlowGraphCanvasTests : TestContext
{
    [Fact]
    public void FlowGraphCanvas_shows_empty_state_when_graph_has_no_nodes()
    {
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowGraphCanvas>(parameters => parameters
            .Add(component => component.Graph, new FlowGraphModel()));

        Assert.Contains("No visual flow map yet", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='flow-graph']"));
    }

    [Fact]
    public void FlowGraphCanvas_renders_all_known_node_kinds_and_fallback_kind()
    {
        var graph = new FlowGraphModel
        {
            Nodes =
            [
                new FlowGraphNode("start", FlowGraphNodeKind.Start, "Start", "entry", "begin", 0),
                new FlowGraphNode("fixture", FlowGraphNodeKind.Fixture, "Fixture", "seed.customer", "setup", 1),
                new FlowGraphNode("action", FlowGraphNodeKind.Action, "Action", "path=/orders", "exercise", 2),
                new FlowGraphNode("decision", FlowGraphNodeKind.Decision, "Decision", "status?", "branch", 3),
                new FlowGraphNode("scenario", FlowGraphNodeKind.Scenario, "Scenario", "checkout", "scenario", 4),
                new FlowGraphNode("expectation", FlowGraphNodeKind.Expectation, "Expectation", "status=200", "assert", 5),
                new FlowGraphNode("end", FlowGraphNodeKind.End, "End", "ready", "complete", 6),
                new FlowGraphNode("unknown", (FlowGraphNodeKind)999, "Unknown", "mystery", "fallback", 7)
            ],
            Edges =
            [
                new FlowGraphEdge("edge-1", "start", "fixture", "next"),
                new FlowGraphEdge("edge-2", "fixture", "action", "next"),
                new FlowGraphEdge("edge-3", "action", "decision", "next"),
                new FlowGraphEdge("edge-4", "decision", "scenario", "next"),
                new FlowGraphEdge("edge-5", "scenario", "expectation", "next"),
                new FlowGraphEdge("edge-6", "expectation", "end", "complete"),
                new FlowGraphEdge("edge-7", "end", "unknown", "fallback")
            ]
        };

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowGraphCanvas>(parameters => parameters
            .Add(component => component.Graph, graph));

        Assert.Contains("2 executable nodes", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Branch-ready model", cut.Markup, StringComparison.Ordinal);

        Assert.Contains("flow-graph-node--start", cut.Find("[data-testid='flow-graph-node-start']").GetAttribute("class"));
        Assert.Contains("Start", cut.Find("[data-testid='flow-graph-node-start']").TextContent, StringComparison.Ordinal);
        Assert.Contains("flow-graph-node--fixture", cut.Find("[data-testid='flow-graph-node-fixture']").GetAttribute("class"));
        Assert.Contains("Fixture", cut.Find("[data-testid='flow-graph-node-fixture']").TextContent, StringComparison.Ordinal);
        Assert.Contains("flow-graph-node--action", cut.Find("[data-testid='flow-graph-node-action']").GetAttribute("class"));
        Assert.Contains("Action", cut.Find("[data-testid='flow-graph-node-action']").TextContent, StringComparison.Ordinal);
        Assert.Contains("flow-graph-node--decision", cut.Find("[data-testid='flow-graph-node-decision']").GetAttribute("class"));
        Assert.Contains("Decision", cut.Find("[data-testid='flow-graph-node-decision']").TextContent, StringComparison.Ordinal);
        Assert.Contains("flow-graph-node--scenario", cut.Find("[data-testid='flow-graph-node-scenario']").GetAttribute("class"));
        Assert.Contains("Scenario", cut.Find("[data-testid='flow-graph-node-scenario']").TextContent, StringComparison.Ordinal);
        Assert.Contains("flow-graph-node--expectation", cut.Find("[data-testid='flow-graph-node-expectation']").GetAttribute("class"));
        Assert.Contains("Expectation", cut.Find("[data-testid='flow-graph-node-expectation']").TextContent, StringComparison.Ordinal);
        Assert.Contains("flow-graph-node--end", cut.Find("[data-testid='flow-graph-node-end']").GetAttribute("class"));
        Assert.Contains("End", cut.Find("[data-testid='flow-graph-node-end']").TextContent, StringComparison.Ordinal);
        Assert.Equal("flow-graph-node ", cut.Find("[data-testid='flow-graph-node-unknown']").GetAttribute("class"));
        Assert.Contains("Node", cut.Find("[data-testid='flow-graph-node-unknown']").TextContent, StringComparison.Ordinal);
        Assert.Contains("fallback", cut.Find("[data-testid='flow-graph-edge-edge-7']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void FlowGraphCanvas_invokes_add_callbacks()
    {
        var fixtureClicks = 0;
        var actionClicks = 0;
        var expectationClicks = 0;
        var graph = new FlowGraphModel
        {
            Nodes = [new FlowGraphNode("start", FlowGraphNodeKind.Start, "Start", null, null, 0)],
            Edges = []
        };

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowGraphCanvas>(parameters => parameters
            .Add(component => component.Graph, graph)
            .Add(component => component.OnAddFixture, () => fixtureClicks++)
            .Add(component => component.OnAddAction, () => actionClicks++)
            .Add(component => component.OnAddExpectation, () => expectationClicks++));

        cut.Find("[data-testid='flow-graph-add-fixture']").Click();
        cut.Find("[data-testid='flow-graph-add-action']").Click();
        cut.Find("[data-testid='flow-graph-add-expectation']").Click();

        Assert.Equal(1, fixtureClicks);
        Assert.Equal(1, actionClicks);
        Assert.Equal(1, expectationClicks);
    }
}
