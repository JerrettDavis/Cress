using Cress.Core.Models;

namespace Cress.Specs;

public sealed class FlowNormalizer
{
    public OperationResult<NormalizedFlow> Normalize(CressFlow flow)
    {
        var normalized = new NormalizedFlow
        {
            Version = flow.Version <= 0 ? 1 : flow.Version,
            FlowId = flow.Id,
            Name = flow.Name,
            CapabilityId = flow.CapabilityId,
            Summary = flow.Summary,
            Tags = [.. flow.Tags],
            Traceability = flow.Traceability,
            Personas = flow.Personas is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(flow.Personas, StringComparer.OrdinalIgnoreCase),
            Fixtures = NormalizeFixtures(flow),
            Actions = flow.When.Select((action, index) => new NormalizedExecutable
            {
                Kind = "step",
                Name = action.Step,
                Inputs = action.With is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(action.With, StringComparer.OrdinalIgnoreCase),
                Source = new SourceReference
                {
                    Section = "when",
                    Index = index,
                    SourceFile = flow.SourceFile
                }
            }).ToList(),
            Expectations = flow.Then.Select((expectation, index) => new NormalizedExecutable
            {
                Kind = "expectation",
                Name = expectation.Expect,
                Inputs = expectation.With is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(expectation.With, StringComparer.OrdinalIgnoreCase),
                Source = new SourceReference
                {
                    Section = "then",
                    Index = index,
                    SourceFile = flow.SourceFile
                }
            }).ToList(),
            Status = flow.Status,
            SourceFile = flow.SourceFile
        };

        return new OperationResult<NormalizedFlow>
        {
            Value = normalized
        };
    }

    private static List<NormalizedFixture> NormalizeFixtures(CressFlow flow)
        => flow.Fixtures is null
            ? []
            : flow.Fixtures.Select(fixture => new NormalizedFixture
            {
                Name = fixture.Key,
                Use = fixture.Value.Use,
                Source = fixture.Value.Source,
                Bindings = ToBindings(fixture.Value)
            }).ToList();

    private static Dictionary<string, string> ToBindings(FlowFixtureRef fixture)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(fixture.For))
        {
            bindings["for"] = fixture.For;
        }

        return bindings;
    }
}
