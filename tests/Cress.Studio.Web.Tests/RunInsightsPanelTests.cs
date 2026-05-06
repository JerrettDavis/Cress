using System.Reflection;
using Bunit;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class RunInsightsPanelTests : TestContext
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
    public void RunInsightsPanel_renders_empty_state_with_no_activity()
    {
        CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RunInsightsPanel>();

        Assert.Contains("Recent activity", cut.Markup);
        Assert.Contains("Flake watch", cut.Markup);
        Assert.Contains("Latest comparison", cut.Markup);
        Assert.Empty(cut.FindAll(".stack-list .explorer-button"));
        Assert.Empty(cut.FindAll("tbody tr"));
    }

    [Fact]
    public void RunInsightsPanel_renders_recent_activity_items()
    {
        var state = CreateState();

        var insights = new StudioRunInsights
        {
            RecentActivity =
            [
                new StudioRecentActivityItem { Headline = "run-001 • 3/3 passed", Detail = "local • 0 recovered after retry" },
                new StudioRecentActivityItem { Headline = "run-002 • 2/3 passed", Detail = "ci • 1 recovered after retry" }
            ]
        };

        SetPrivate(state, "Snapshot", new StudioProjectSnapshot { RunInsights = insights });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RunInsightsPanel>();

        Assert.Contains("run-001 • 3/3 passed", cut.Markup);
        Assert.Contains("run-002 • 2/3 passed", cut.Markup);
        Assert.Equal(2, cut.FindAll(".stack-list .explorer-button").Count);
    }

    [Fact]
    public void RunInsightsPanel_renders_flake_watch_row_for_flaky_flow()
    {
        var state = CreateState();

        var insights = new StudioRunInsights
        {
            FlakyFlows =
            [
                new StudioFlowHealthItem
                {
                    FlowId = "flow-flaky",
                    Name = "Login flow",
                    FlakeScore = 45,
                    Trend = "P F P F",
                    RetryRecoveries = 2
                }
            ]
        };

        SetPrivate(state, "Snapshot", new StudioProjectSnapshot { RunInsights = insights });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RunInsightsPanel>();

        var rows = cut.FindAll("tbody tr");
        Assert.Single(rows);
        Assert.Contains("Login flow", cut.Markup);
        Assert.Contains("45", cut.Markup);
        Assert.Contains("P F P F", cut.Markup);
    }
}
