using System.Reflection;
using Bunit;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class RunInsightsPanelStepBreakdownTests : TestContext
{
    private StudioWorkspaceState CreateState()
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
    }

    [Fact]
    public void RunInsightsPanel_renders_step_breakdown_details_when_flaky_flow_has_step_data()
    {
        var state = CreateState();

        var insights = new StudioRunInsights
        {
            FlakyFlows =
            [
                new StudioFlowHealthItem
                {
                    FlowId = "login-flow",
                    Name = "Login flow",
                    FlakeScore = 45,
                    Trend = "P F P F",
                    RetryRecoveries = 1,
                    StepBreakdown =
                    [
                        new StepInstability { StepIndex = 0, StepName = "app.open",  FailCount = 0, TotalCount = 4, FlakeRate = 0.0 },
                        new StepInstability { StepIndex = 1, StepName = "app.login", FailCount = 2, TotalCount = 4, FlakeRate = 50.0 },
                        new StepInstability { StepIndex = 2, StepName = "app.close", FailCount = 0, TotalCount = 4, FlakeRate = 0.0 }
                    ]
                }
            ]
        };

        SetPrivate(state, "Snapshot", new StudioProjectSnapshot { RunInsights = insights });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RunInsightsPanel>();

        // The details element with the breakdown should be present
        Assert.Contains("flake-step-details", cut.Markup);
        Assert.Contains("flake-step-breakdown", cut.Markup);

        // Step names should appear in the breakdown table
        Assert.Contains("app.open", cut.Markup);
        Assert.Contains("app.login", cut.Markup);
        Assert.Contains("app.close", cut.Markup);

        // The high flake rate (50%) should have the high-rate CSS class
        Assert.Contains("flake-rate-high", cut.Markup);

        // Zero-fail steps should have the low-rate class
        Assert.Contains("flake-rate-low", cut.Markup);
    }

    [Fact]
    public void RunInsightsPanel_renders_plain_row_when_no_step_breakdown()
    {
        var state = CreateState();

        var insights = new StudioRunInsights
        {
            FlakyFlows =
            [
                new StudioFlowHealthItem
                {
                    FlowId = "flow-plain",
                    Name = "Plain flow",
                    FlakeScore = 25,
                    Trend = "P F",
                    RetryRecoveries = 0,
                    StepBreakdown = []
                }
            ]
        };

        SetPrivate(state, "Snapshot", new StudioProjectSnapshot { RunInsights = insights });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RunInsightsPanel>();

        // When no step breakdown, no <details> element should be rendered
        Assert.DoesNotContain("flake-step-details", cut.Markup);

        // But the flow name and row should still be present
        Assert.Contains("Plain flow", cut.Markup);
        Assert.Single(cut.FindAll("tbody tr"));
    }

    [Fact]
    public void RunInsightsPanel_renders_amber_class_for_medium_flake_rate()
    {
        var state = CreateState();

        var insights = new StudioRunInsights
        {
            FlakyFlows =
            [
                new StudioFlowHealthItem
                {
                    FlowId = "checkout",
                    Name = "Checkout",
                    FlakeScore = 30,
                    Trend = "P F P",
                    RetryRecoveries = 0,
                    StepBreakdown =
                    [
                        new StepInstability { StepIndex = 0, StepName = "step.pay", FailCount = 1, TotalCount = 4, FlakeRate = 25.0 }
                    ]
                }
            ]
        };

        SetPrivate(state, "Snapshot", new StudioProjectSnapshot { RunInsights = insights });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RunInsightsPanel>();

        // 25% flake rate is >20% but ≤50%, so medium (amber) class
        Assert.Contains("flake-rate-medium", cut.Markup);
        Assert.DoesNotContain("flake-rate-high", cut.Markup);
    }
}
