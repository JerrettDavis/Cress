using Cress.Core.Models;

namespace Cress.Execution;

public sealed class PlanGenerator
{
    public PlanCollection Generate(ProjectCatalog catalog, IEnumerable<NormalizedFlow> flows)
    {
        var diagnostics = new List<Diagnostic>();
        var plans = new List<ExecutionPlan>();

        foreach (var flow in flows)
        {
            var planDiagnostics = new List<Diagnostic>();
            var actions = new List<PlanAction>();
            var requiredDrivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolvedFixtures = ResolveFixtures(catalog, flow, planDiagnostics);

            foreach (var fixture in resolvedFixtures)
            {
                var setupInputs = new Dictionary<string, string>(fixture.Bindings, StringComparer.OrdinalIgnoreCase)
                {
                    ["alias"] = fixture.Alias,
                    ["fixture"] = fixture.DefinitionName
                };
                actions.Add(new PlanAction
                {
                    Kind = "setup",
                    Name = "fixture.create",
                    Fixture = fixture.DefinitionName,
                    Plugin = fixture.Definition.Provider?.Plugin,
                    Operation = fixture.Definition.Provider?.Operation,
                    Inputs = setupInputs
                });
            }

            foreach (var executable in flow.Actions)
            {
                var action = CreateStepAction("action", executable, catalog, requiredDrivers, planDiagnostics);
                if (action is not null)
                {
                    actions.Add(action);
                }
            }

            foreach (var executable in flow.Expectations)
            {
                var action = CreateStepAction("expectation", executable, catalog, requiredDrivers, planDiagnostics);
                if (action is not null)
                {
                    actions.Add(action);
                }
            }

            foreach (var fixture in resolvedFixtures.Where(fixture => !fixture.Definition.Cleanup.Equals("never", StringComparison.OrdinalIgnoreCase)))
            {
                var cleanupInputs = new Dictionary<string, string>(fixture.Bindings, StringComparer.OrdinalIgnoreCase)
                {
                    ["alias"] = fixture.Alias,
                    ["fixture"] = fixture.DefinitionName,
                    ["cleanupPolicy"] = fixture.Definition.Cleanup
                };
                actions.Add(new PlanAction
                {
                    Kind = "cleanup",
                    Name = "fixture.cleanup",
                    Fixture = fixture.DefinitionName,
                    Plugin = fixture.Definition.Provider?.Plugin,
                    Operation = fixture.Definition.Provider?.Operation,
                    Inputs = cleanupInputs
                });
            }

            diagnostics.AddRange(planDiagnostics);
            plans.Add(new ExecutionPlan
            {
                FlowId = flow.FlowId,
                Name = flow.Name,
                CapabilityId = flow.CapabilityId,
                SourceFile = flow.SourceFile,
                Traceability = flow.Traceability,
                Actions = actions,
                RequiredDrivers = requiredDrivers.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        return new PlanCollection
        {
            Plans = plans,
            Diagnostics = diagnostics
        };
    }

    private static List<ResolvedFixture> ResolveFixtures(ProjectCatalog catalog, NormalizedFlow flow, List<Diagnostic> diagnostics)
    {
        var resolved = new List<ResolvedFixture>();
        foreach (var fixture in flow.Fixtures)
        {
            if (string.IsNullOrWhiteSpace(fixture.Use))
            {
                if (string.IsNullOrWhiteSpace(fixture.Source))
                {
                    diagnostics.Add(CreateDiagnostic("PLN001", $"Fixture '{fixture.Name}' could not be resolved.", flow.SourceFile));
                }

                continue;
            }

            if (!catalog.FixtureDefinitions.TryGetValue(fixture.Use, out var definition))
            {
                diagnostics.Add(CreateDiagnostic("PLN002", $"Fixture definition '{fixture.Use}' was not found.", flow.SourceFile));
                continue;
            }

            resolved.Add(new ResolvedFixture
            {
                Alias = fixture.Name,
                DefinitionName = fixture.Use,
                Definition = definition,
                Bindings = fixture.Bindings
            });
        }

        return resolved;
    }

    private static PlanAction? CreateStepAction(
        string kind,
        NormalizedExecutable executable,
        ProjectCatalog catalog,
        HashSet<string> requiredDrivers,
        List<Diagnostic> diagnostics)
    {
        if (!catalog.StepRegistry.TryResolve(executable.Name, out var definition))
        {
            diagnostics.Add(CreateDiagnostic("PLN003", $"Step '{executable.Name}' was not found in the step registry.", executable.Source.SourceFile));
            return null;
        }

        if (definition.Inputs is not null)
        {
            foreach (var input in definition.Inputs.Where(entry => entry.Value.Required))
            {
                if (!executable.Inputs.ContainsKey(input.Key))
                {
                    diagnostics.Add(CreateDiagnostic("PLN004", $"Step '{definition.Name}' is missing required input '{input.Key}'.", executable.Source.SourceFile));
                }
            }
        }

        string? driver = null;
        if (definition.Drivers.Count > 0)
        {
            driver = definition.Drivers.FirstOrDefault(candidate =>
                catalog.EffectiveConfig.Config.Drivers.TryGetValue(candidate, out var config) && config.Enabled);

            if (driver is null)
            {
                diagnostics.Add(CreateDiagnostic("PLN005", $"Step '{definition.Name}' requires one of [{string.Join(", ", definition.Drivers)}], but no enabled driver was found.", executable.Source.SourceFile));
            }
            else
            {
                requiredDrivers.Add(driver);
            }
        }

        return new PlanAction
        {
            Kind = kind,
            Name = definition.Name,
            Step = definition.Name,
            Driver = driver,
            Plugin = definition.Implementation?.Plugin,
            Operation = definition.Implementation?.Operation,
            Owner = definition.Owner,
            RetrySafe = definition.RetrySafe,
            Inputs = executable.Inputs
        };
    }

    private static Diagnostic CreateDiagnostic(string code, string message, string? file)
        => new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = code,
            Message = message,
            File = file
        };
}
