using Cress.Core.Models;

namespace Cress.Execution;

public sealed class StepRegistry
{
    public OperationResult<StepRegistrySnapshot> Build(IEnumerable<StepManifest> manifests)
    {
        var diagnostics = new List<Diagnostic>();
        var definitions = new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in manifests)
        {
            foreach (var step in manifest.Steps)
            {
                if (!definitions.TryAdd(step.Name, step))
                {
                    diagnostics.Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "REG001",
                        Message = $"Duplicate step '{step.Name}' was found across manifests.",
                        File = step.SourceFile
                    });
                    continue;
                }

                foreach (var alias in step.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
                {
                    if (aliases.TryGetValue(alias, out var existing) && !existing.Equals(step.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Warning,
                            Code = "REG002",
                            Message = $"Alias '{alias}' is defined by multiple steps and will resolve to '{existing}'.",
                            File = step.SourceFile
                        });
                        continue;
                    }

                    aliases[alias] = step.Name;
                }
            }
        }

        return new OperationResult<StepRegistrySnapshot>
        {
            Value = new StepRegistrySnapshot(definitions, aliases),
            Diagnostics = diagnostics
        };
    }
}

public sealed class StepRegistrySnapshot
{
    public static StepRegistrySnapshot Empty { get; } = new(
        new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public StepRegistrySnapshot(
        IReadOnlyDictionary<string, StepDefinition> definitions,
        IReadOnlyDictionary<string, string> aliases)
    {
        Definitions = definitions;
        Aliases = aliases;
    }

    public IReadOnlyDictionary<string, StepDefinition> Definitions { get; }
    public IReadOnlyDictionary<string, string> Aliases { get; }

    public bool TryResolve(string name, out StepDefinition definition)
    {
        if (Definitions.TryGetValue(name, out definition!))
        {
            return true;
        }

        if (Aliases.TryGetValue(name, out var aliasTarget) && Definitions.TryGetValue(aliasTarget, out definition!))
        {
            return true;
        }

        definition = null!;
        return false;
    }
}
