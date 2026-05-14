using Cress.Core.Models;

namespace Cress.UnitTests;

public sealed class CoverageModelTests
{
    [Fact]
    public void PlanCollection_SuccessRequiresPlanAndNoErrors()
    {
        var noPlans = new PlanCollection();
        var withWarning = new PlanCollection
        {
            Plans =
            [
                new ExecutionPlan
                {
                    FlowId = "flow.search",
                    Name = "Search flow",
                    CapabilityId = "cap.search",
                    SourceFile = "flows\\search.flow.yaml",
                    Traceability = new TraceabilityInfo
                    {
                        Requirement = "REQ-1",
                        AcceptanceCriteria = ["Find existing records"],
                        Owner = "team-cress",
                        Risk = "medium"
                    },
                    RequiredDrivers = ["browser"],
                    Actions =
                    [
                        new PlanAction
                        {
                            Kind = "step",
                            Name = "browser.open",
                            Step = "browser.open",
                            Driver = "browser",
                            Plugin = "core",
                            Operation = "open",
                            Fixture = "default",
                            Owner = "team-cress",
                            RetrySafe = true,
                            Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["url"] = "https://example.test"
                            }
                        }
                    ]
                }
            ],
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "warn-1",
                    Message = "Non-blocking warning."
                }
            ]
        };
        var withError = withWarning with
        {
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "err-1",
                    Message = "Blocking error."
                }
            ]
        };

        Assert.False(noPlans.Success);
        Assert.True(withWarning.Success);
        Assert.False(withError.Success);
    }

}
