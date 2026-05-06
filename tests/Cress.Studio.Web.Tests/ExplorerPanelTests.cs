using System.Reflection;
using Bunit;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class ExplorerPanelTests : TestContext
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
    public void ExplorerPanel_renders_empty_state_when_no_snapshot_loaded()
    {
        CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        Assert.Contains("Explorer", cut.Markup);
        Assert.Contains("Load a Cress workspace", cut.Markup);
        Assert.DoesNotContain("explorer-group", cut.Markup);
    }

    [Fact]
    public void ExplorerPanel_renders_both_flow_names_when_snapshot_has_two_flows()
    {
        var state = CreateState();

        var snapshot = new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows =
                [
                    new NormalizedFlow { FlowId = "flow-alpha", Name = "Alpha flow", SourceFile = @"C:\workspace\flows\alpha.flow.yaml" },
                    new NormalizedFlow { FlowId = "flow-beta",  Name = "Beta flow",  SourceFile = @"C:\workspace\flows\beta.flow.yaml" }
                ]
            }
        };

        SetPrivate(state, "Snapshot", snapshot);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        Assert.Contains("Alpha flow", cut.Markup);
        Assert.Contains("Beta flow", cut.Markup);
    }

    [Fact]
    public void ExplorerPanel_renders_runs_section_when_runs_exist()
    {
        var state = CreateState();

        var snapshot = new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows = []
            }
        };

        SetPrivate(state, "Snapshot", snapshot);

        var run = new StoredRunResult
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-xyz",
                    Profile = "local",
                    StartedAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero)
                },
                Flows = []
            }
        };

        state.Runs.Add(run);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        Assert.Contains("Runs", cut.Markup);
        Assert.Contains("run-xyz", cut.Markup);
    }

    [Fact]
    public void ExplorerPanel_filter_input_is_bound_to_state_explorer_filter()
    {
        var state = CreateState();

        var snapshot = new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows = []
            }
        };

        SetPrivate(state, "Snapshot", snapshot);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        var filterInput = cut.Find("#explorerFilter");
        Assert.NotNull(filterInput);

        filterInput.Input("my-filter");

        Assert.Equal("my-filter", state.ExplorerFilter);
    }
}
