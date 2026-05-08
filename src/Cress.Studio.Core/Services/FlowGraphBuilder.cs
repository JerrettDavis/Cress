using Cress.Studio.ViewModels;

namespace Cress.Studio.Services;

public static class FlowGraphBuilder
{
    public static FlowGraphModel Build(FlowDocumentViewModel? document)
    {
        if (document is null)
        {
            return new FlowGraphModel();
        }

        var nodes = new List<FlowGraphNode>();
        var edges = new List<FlowGraphEdge>();
        var sequence = 0;

        AddNode(nodes, sequence++, "flow-start", FlowGraphNodeKind.Start, document.Name, document.Id, "Flow entry");

        foreach (var fixture in document.Fixtures
                     .Where(item => !string.IsNullOrWhiteSpace(item.Alias) || !string.IsNullOrWhiteSpace(item.Use) || !string.IsNullOrWhiteSpace(item.Source)))
        {
            var title = string.IsNullOrWhiteSpace(fixture.Alias) ? "Fixture" : fixture.Alias;
            var subtitle = string.IsNullOrWhiteSpace(fixture.Use) ? fixture.Source ?? "Data binding" : fixture.Use;
            var detail = string.IsNullOrWhiteSpace(fixture.For) ? "Setup" : $"For {fixture.For}";
            AddNode(nodes, sequence++, $"fixture-{nodes.Count}", FlowGraphNodeKind.Fixture, title, subtitle, detail);
        }

        foreach (var action in document.Actions.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            AddNode(nodes, sequence++, $"action-{nodes.Count}", FlowGraphNodeKind.Action, action.Name, SummarizeInputs(action.InputsText), "Exercise");
        }

        foreach (var expectation in document.Expectations.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            AddNode(nodes, sequence++, $"expectation-{nodes.Count}", FlowGraphNodeKind.Expectation, expectation.Name, SummarizeInputs(expectation.InputsText), "Assert");
        }

        AddNode(nodes, sequence, "flow-end", FlowGraphNodeKind.End, "Complete", document.Status ?? "ready", "Flow exit");

        for (var index = 0; index < nodes.Count - 1; index++)
        {
            edges.Add(new FlowGraphEdge(
                Id: $"edge-{nodes[index].Id}-{nodes[index + 1].Id}",
                SourceId: nodes[index].Id,
                TargetId: nodes[index + 1].Id,
                Label: index == nodes.Count - 2 ? "complete" : "next"));
        }

        return new FlowGraphModel
        {
            Nodes = nodes,
            Edges = edges
        };
    }

    private static void AddNode(
        ICollection<FlowGraphNode> nodes,
        int column,
        string id,
        FlowGraphNodeKind kind,
        string title,
        string? subtitle,
        string? detail)
        => nodes.Add(new FlowGraphNode(
            Id: id,
            Kind: kind,
            Title: title,
            Subtitle: subtitle,
            Detail: detail,
            Column: column));

    private static string? SummarizeInputs(string? inputsText)
    {
        if (string.IsNullOrWhiteSpace(inputsText))
        {
            return "No inputs";
        }

        var lines = inputsText
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
        {
            return "No inputs";
        }

        return lines.Length == 1
            ? lines[0]
            : $"{lines[0]} +{lines.Length - 1} more";
    }
}

public sealed record FlowGraphModel
{
    public IReadOnlyList<FlowGraphNode> Nodes { get; init; } = [];
    public IReadOnlyList<FlowGraphEdge> Edges { get; init; } = [];
}

public sealed record FlowGraphNode(
    string Id,
    FlowGraphNodeKind Kind,
    string Title,
    string? Subtitle,
    string? Detail,
    int Column);

public sealed record FlowGraphEdge(
    string Id,
    string SourceId,
    string TargetId,
    string Label);

public enum FlowGraphNodeKind
{
    Start,
    Fixture,
    Action,
    Decision,
    Scenario,
    Expectation,
    End
}
