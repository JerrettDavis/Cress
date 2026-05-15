using System.Reflection;
using Bunit;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class SuiteEditorTests : TestContext
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
    public void SuiteEditor_renders_empty_panel_when_no_suite_selected()
    {
        CreateState();

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.SuiteEditor>();

        Assert.Contains("Select or create a suite", cut.Markup);
        Assert.DoesNotContain("suite-editor", cut.Markup);
    }

    [Fact]
    public void SuiteEditor_renders_suite_name_input_with_selected_suite_name()
    {
        var state = CreateState();

        var suite = new StudioSuiteEditorModel
        {
            FilePath = @"C:\workspace\suites\smoke.suite.yaml",
            Id = "smoke",
            Name = "Smoke suite"
        };

        SetPrivate(state, "SelectedSuite", suite);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.SuiteEditor>();

        var inputs = cut.FindAll("input.text-input");
        Assert.Contains(inputs, input => input.GetAttribute("value") == "Smoke suite");
    }

    [Fact]
    public void SuiteEditor_renders_flow_checkboxes_for_each_available_flow()
    {
        var state = CreateState();

        var suite = new StudioSuiteEditorModel
        {
            FilePath = @"C:\workspace\suites\full.suite.yaml",
            Id = "full",
            Name = "Full suite"
        };

        SetPrivate(state, "SelectedSuite", suite);

        var snapshot = new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows =
                [
                    new NormalizedFlow { FlowId = "flow-a", Name = "Flow A" },
                    new NormalizedFlow { FlowId = "flow-b", Name = "Flow B" }
                ]
            }
        };

        SetPrivate(state, "Snapshot", snapshot);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.SuiteEditor>();

        var checkboxes = cut.FindAll(".flow-checklist input[type=checkbox]");
        Assert.Equal(2, checkboxes.Count);
        Assert.Contains("Flow A", cut.Markup);
        Assert.Contains("Flow B", cut.Markup);
    }

    [Fact]
    public void SuiteEditor_filters_flows_by_search_text()
    {
        var state = CreateState();

        var suite = new StudioSuiteEditorModel
        {
            FilePath = @"C:\workspace\suites\filtered.suite.yaml",
            Id = "filtered",
            Name = "Filtered suite"
        };

        SetPrivate(state, "SelectedSuite", suite);
        SetPrivate(state, "Snapshot", new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows =
                [
                    new NormalizedFlow { FlowId = "flow-auth", Name = "Auth login", CapabilityId = "auth" },
                    new NormalizedFlow { FlowId = "flow-search", Name = "Search catalog", CapabilityId = "search" }
                ]
            }
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.SuiteEditor>();

        cut.Find("[data-testid='suite-flow-filter']").Change("auth");

        Assert.Contains("Auth login", cut.Markup);
        Assert.DoesNotContain("Search catalog", cut.Markup);
        Assert.Contains("1 shown", cut.Find("[data-testid='suite-flow-summary']").TextContent);
    }

    [Fact]
    public void SuiteEditor_can_show_selected_flows_only()
    {
        var state = CreateState();

        var suite = new StudioSuiteEditorModel
        {
            FilePath = @"C:\workspace\suites\selected.suite.yaml",
            Id = "selected",
            Name = "Selected suite"
        };
        suite.FlowIds.Add("flow-b");

        SetPrivate(state, "SelectedSuite", suite);
        SetPrivate(state, "Snapshot", new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows =
                [
                    new NormalizedFlow { FlowId = "flow-a", Name = "Flow A" },
                    new NormalizedFlow { FlowId = "flow-b", Name = "Flow B" }
                ]
            }
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.SuiteEditor>();

        cut.Find("[data-testid='suite-selected-only']").Change(true);

        Assert.DoesNotContain("Flow A", cut.Markup);
        Assert.Contains("Flow B", cut.Markup);
        Assert.Contains("Flow B", cut.Find("[data-testid='suite-selected-flow-chips']").TextContent);
    }

    [Fact]
    public void SuiteEditor_shows_empty_message_when_filter_matches_nothing()
    {
        var state = CreateState();

        var suite = new StudioSuiteEditorModel
        {
            FilePath = @"C:\workspace\suites\filtered.suite.yaml",
            Id = "filtered",
            Name = "Filtered suite"
        };

        SetPrivate(state, "SelectedSuite", suite);
        SetPrivate(state, "Snapshot", new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows =
                [
                    new NormalizedFlow { FlowId = "flow-auth", Name = "Auth login", CapabilityId = "auth" }
                ]
            }
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.SuiteEditor>();

        cut.Find("[data-testid='suite-flow-filter']").Change("missing");

        Assert.Contains("No flows match the current filter", cut.Find("[data-testid='suite-flow-filter-empty']").TextContent);
    }

    [Fact]
    public void SuiteEditor_select_all_and_none_buttons_update_selection()
    {
        var state = CreateState();

        var suite = new StudioSuiteEditorModel
        {
            FilePath = @"C:\workspace\suites\bulk.suite.yaml",
            Id = "bulk",
            Name = "Bulk suite"
        };

        SetPrivate(state, "SelectedSuite", suite);
        SetPrivate(state, "Snapshot", new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows =
                [
                    new NormalizedFlow { FlowId = "flow-a", Name = "Flow A" },
                    new NormalizedFlow { FlowId = "flow-b", Name = "Flow B" }
                ]
            }
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.SuiteEditor>();

        cut.FindAll(".checklist-toolbar-link")[0].Click();
        Assert.Equal(2, state.SelectedSuite!.FlowIds.Count);
        Assert.Contains("2 / 2 selected", cut.Markup);

        cut.FindAll(".checklist-toolbar-link")[1].Click();
        Assert.Empty(state.SelectedSuite!.FlowIds);
        Assert.Contains("0 / 2 selected", cut.Markup);
    }

    [Fact]
    public void SuiteEditor_checkbox_toggle_and_filter_summary_cover_capability_and_overflow_chips()
    {
        var state = CreateState();

        var suite = new StudioSuiteEditorModel
        {
            FilePath = @"C:\workspace\suites\overflow.suite.yaml",
            Id = "overflow",
            Name = "Overflow suite"
        };
        suite.FlowIds.UnionWith(["flow-1", "flow-2", "flow-3", "flow-4", "flow-5", "flow-6", "flow-7"]);

        SetPrivate(state, "SelectedSuite", suite);
        SetPrivate(state, "Snapshot", new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows =
                [
                    new NormalizedFlow { FlowId = "flow-1", Name = "Alpha auth", CapabilityId = "cap.auth" },
                    new NormalizedFlow { FlowId = "flow-2", Name = "Beta browse" },
                    new NormalizedFlow { FlowId = "flow-3", Name = "Gamma browse" },
                    new NormalizedFlow { FlowId = "flow-4", Name = "Delta browse" },
                    new NormalizedFlow { FlowId = "flow-5", Name = "Epsilon browse" },
                    new NormalizedFlow { FlowId = "flow-6", Name = "Zeta browse" },
                    new NormalizedFlow { FlowId = "flow-7", Name = "Eta browse" }
                ]
            }
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.SuiteEditor>();

        Assert.Contains("+1 more", cut.Find("[data-testid='suite-selected-flow-chips']").TextContent, StringComparison.Ordinal);
        Assert.Contains("cap.auth", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid='suite-flow-filter']").Change("cap.auth");
        Assert.Contains("Filter: cap.auth", cut.Find("[data-testid='suite-flow-summary']").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll(".flow-checklist .check-item"));

        cut.Find(".flow-checklist input[type=checkbox]").Change(false);
        Assert.DoesNotContain("flow-1", state.SelectedSuite!.FlowIds);
    }
}
