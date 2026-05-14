using Cress.Studio.Services;
using Cress.Studio.ViewModels;

namespace Cress.Studio.Web.Tests;

public sealed class FlowGraphBuilderTests
{
    [Fact]
    public void Build_creates_visual_sequence_from_flow_document()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "checkout-flow",
            Name = "Checkout flow",
            Status = "draft",
            Fixtures =
            [
                new EditableFixture
                {
                    Alias = "customer",
                    Use = "persona.customer",
                    For = "web"
                }
            ],
            Actions =
            [
                new EditableExecutable
                {
                    Name = "order.start",
                    InputsText = "path=/checkout"
                },
                new EditableExecutable
                {
                    Name = "order.submit",
                    InputsText = "payment=visa\npostalCode=12345"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "order.completed",
                    InputsText = "status=200"
                }
            ]
        });

        var graph = FlowGraphBuilder.Build(document);

        Assert.Collection(
            graph.Nodes,
            node => Assert.Equal(FlowGraphNodeKind.Start, node.Kind),
            node => Assert.Equal(FlowGraphNodeKind.Fixture, node.Kind),
            node => Assert.Equal(FlowGraphNodeKind.Action, node.Kind),
            node => Assert.Equal(FlowGraphNodeKind.Action, node.Kind),
            node => Assert.Equal(FlowGraphNodeKind.Expectation, node.Kind),
            node => Assert.Equal(FlowGraphNodeKind.End, node.Kind));

        Assert.Equal(graph.Nodes.Count - 1, graph.Edges.Count);
        Assert.Equal("checkout-flow", graph.Nodes[0].Subtitle);
        Assert.Equal("persona.customer", graph.Nodes[1].Subtitle);
        Assert.Equal("payment=visa +1 more", graph.Nodes[3].Subtitle);
        Assert.Equal("draft", graph.Nodes[^1].Subtitle);
    }

    [Fact]
    public void Build_handles_null_and_summarizes_sparse_rows()
    {
        Assert.Empty(FlowGraphBuilder.Build(null).Nodes);

        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "sparse-flow",
            Name = "Sparse flow",
            Fixtures =
            [
                new EditableFixture
                {
                    Source = "data/orders.json"
                }
            ],
            Actions =
            [
                new EditableExecutable
                {
                    Name = "first.step",
                    InputsText = ""
                },
                new EditableExecutable()
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "check.result",
                    InputsText = "status=ok"
                }
            ]
        });

        var graph = FlowGraphBuilder.Build(document);
        var edge = new FlowGraphEdge("edge-a", "start", "end", "next");

        Assert.Equal("data/orders.json", graph.Nodes[1].Subtitle);
        Assert.Equal("No inputs", graph.Nodes[2].Subtitle);
        Assert.Equal("ready", graph.Nodes[^1].Subtitle);
        Assert.Equal("complete", graph.Edges[^1].Label);
        Assert.Equal(("edge-a", "start", "end", "next"), (edge.Id, edge.SourceId, edge.TargetId, edge.Label));
    }
}
