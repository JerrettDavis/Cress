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

        var checkboxes = cut.FindAll("input[type=checkbox]");
        Assert.Equal(2, checkboxes.Count);
        Assert.Contains("Flow A", cut.Markup);
        Assert.Contains("Flow B", cut.Markup);
    }
}
