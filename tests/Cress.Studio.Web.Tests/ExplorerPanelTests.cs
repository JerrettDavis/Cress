using System.Reflection;
using Bunit;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class ExplorerPanelTests : TestContext
{
    private static StudioProjectSnapshot CreateSnapshot()
        => new()
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows =
                [
                    new NormalizedFlow { FlowId = "flow-alpha", Name = "Alpha flow", SourceFile = @"C:\workspace\flows\alpha.flow.yaml" },
                    new NormalizedFlow { FlowId = "flow-beta", Name = "Beta flow", SourceFile = @"C:\workspace\flows\beta.flow.yaml" }
                ],
                Capabilities =
                [
                    new CressCapability { Id = "cap-auth", Name = "Authentication" }
                ],
                FixtureDefinitions = new Dictionary<string, FixtureDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["browser"] = new FixtureDefinition { Name = "browser", Type = "playwright.browser" }
                },
                StepRegistry = new StepRegistrySnapshot(
                    new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["log-in"] = new StepDefinition
                        {
                            Name = "Log in",
                            Implementation = new StepImplementationBinding { Plugin = "builtin", Operation = "login" }
                        }
                    },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            }
        };

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
        var snapshot = CreateSnapshot();

        SetPrivate(state, "Snapshot", snapshot);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        Assert.Contains("Alpha flow", cut.Markup);
        Assert.Contains("Beta flow", cut.Markup);
    }

    [Fact]
    public void ExplorerPanel_renders_runs_section_when_runs_exist()
    {
        var state = CreateState();
        var snapshot = CreateSnapshot();

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
        var snapshot = CreateSnapshot();

        SetPrivate(state, "Snapshot", snapshot);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        var filterInput = cut.Find("#explorerFilter");
        Assert.NotNull(filterInput);

        filterInput.Input("my-filter");

        Assert.Equal("my-filter", state.ExplorerFilter);
    }

    [Fact]
    public void ExplorerPanel_filter_summary_reports_match_counts()
    {
        var state = CreateState();
        SetPrivate(state, "Snapshot", CreateSnapshot());
        state.Suites.Add(new StudioSuiteDocument { Id = "smoke", Name = "Smoke suite", FilePath = @"C:\workspace\suites\smoke.suite.yaml" });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        cut.Find("#explorerFilter").Input("auth");

        var summary = cut.Find("[data-testid='explorer-filter-summary']").TextContent;
        Assert.Contains("1 matches", summary);
        Assert.Contains("1 capabilities", summary);
        Assert.Contains("0 flows", summary);
    }

    [Fact]
    public void ExplorerPanel_filter_opens_matching_collapsed_sections()
    {
        var state = CreateState();
        SetPrivate(state, "Snapshot", CreateSnapshot());

        var run = new StoredRunResult
        {
            Result = new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "nightly-auth-run",
                    Profile = "local",
                    StartedAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero)
                },
                Flows = []
            }
        };

        state.Runs.Add(run);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        cut.Find("#explorerFilter").Input("nightly");

        Assert.True(cut.Find("[data-testid='explorer-runs-section']").HasAttribute("open"));
        Assert.Contains("nightly-auth-run", cut.Markup);
    }

    [Fact]
    public void ExplorerPanel_shows_global_empty_state_when_filter_matches_nothing()
    {
        var state = CreateState();
        SetPrivate(state, "Snapshot", CreateSnapshot());

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        cut.Find("#explorerFilter").Input("does-not-exist");

        Assert.Contains("No explorer items match the current filter", cut.Markup);
        Assert.True(cut.Find("[data-testid='explorer-filter-summary']").TextContent.Contains("0 matches", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplorerPanel_clear_filter_button_restores_full_workspace_tree()
    {
        var state = CreateState();
        SetPrivate(state, "Snapshot", CreateSnapshot());

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        cut.Find("#explorerFilter").Input("alpha");
        Assert.Equal("alpha", state.ExplorerFilter);

        cut.Find("[aria-label='Clear filter']").Click();

        Assert.Equal(string.Empty, state.ExplorerFilter);
        Assert.Contains("Alpha flow", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Beta flow", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplorerPanel_flow_selected_state_and_suite_button_select_items()
    {
        var state = CreateState();
        var snapshot = CreateSnapshot();
        SetPrivate(state, "Snapshot", snapshot);
        SetPrivate(state, nameof(StudioWorkspaceState.SelectedFlow), new FlowDocumentViewModel
        {
            FilePath = @"C:\workspace\flows\alpha.flow.yaml",
            Name = "Alpha flow"
        });

        var suite = new StudioSuiteDocument { Id = "smoke", Name = "Smoke suite", FilePath = @"C:\workspace\suites\smoke.suite.yaml" };
        state.Suites.Add(suite);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        Assert.Contains("selected", cut.Find("[data-testid='explorer-flow-flow-alpha']").GetAttribute("class"));

        cut.Find("[data-testid='explorer-suite-smoke']").Click();
        Assert.Equal(suite.FilePath, state.SelectedSuite?.FilePath);
        Assert.Contains("selected", cut.Find("[data-testid='explorer-suite-smoke']").GetAttribute("class"));
    }

    [Fact]
    public void ExplorerPanel_capabilities_fixtures_and_steps_show_defined_empty_messages_without_items()
    {
        var state = CreateState();
        SetPrivate(state, "Snapshot", new StudioProjectSnapshot
        {
            Catalog = new ProjectCatalog
            {
                NormalizedFlows = [],
                Capabilities = [],
                FixtureDefinitions = new Dictionary<string, FixtureDefinition>(StringComparer.OrdinalIgnoreCase),
                StepRegistry = new StepRegistrySnapshot(
                    new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            }
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

        Assert.Contains("No capabilities defined.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No fixtures defined.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No steps defined.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No runs yet — click Run all to start.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplorerPanel_filter_mismatch_messages_render_for_each_section()
    {
        var state = CreateState();
        SetPrivate(state, "Snapshot", CreateSnapshot());
        state.Suites.Add(new StudioSuiteDocument { Id = "smoke", Name = "Smoke suite", FilePath = @"C:\workspace\suites\smoke.suite.yaml" });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();
        cut.Find("#explorerFilter").Input("smoke");

        Assert.Contains("No flows match the filter.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No suites match the filter.", cut.Markup);

        cut.Find("#explorerFilter").Input("fixture-miss");

        Assert.Contains("No suites match the filter.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No capabilities match the filter.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No fixtures match the filter.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No steps match the filter.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplorerPanel_buttons_select_flow_capability_fixture_step_and_run()
    {
        var state = CreateState();
        var tempRoot = Path.Combine(Path.GetTempPath(), "cress-explorer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var flowPath = Path.Combine(tempRoot, "alpha.flow.yaml");
            File.WriteAllText(flowPath, """
            version: 1
            id: flow-alpha
            name: Alpha flow
            when:
              - step: http.get
                with:
                  url: https://example.test
            """);

            SetPrivate(state, "Snapshot", new StudioProjectSnapshot
            {
                Catalog = new ProjectCatalog
                {
                    NormalizedFlows =
                    [
                        new NormalizedFlow { FlowId = "flow-alpha", Name = "Alpha flow", SourceFile = flowPath },
                        new NormalizedFlow { FlowId = "flow-beta", Name = "Beta flow", SourceFile = Path.Combine(tempRoot, "beta.flow.yaml") }
                    ],
                    Capabilities =
                    [
                        new CressCapability { Id = "cap-auth", Name = "Authentication", SourceFile = Path.Combine(tempRoot, "auth.md") }
                    ],
                    FixtureDefinitions = new Dictionary<string, FixtureDefinition>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["browser"] = new FixtureDefinition { Name = "browser", Type = "playwright.browser", SourceFile = Path.Combine(tempRoot, "fixtures.yaml") }
                    },
                    StepRegistry = new StepRegistrySnapshot(
                        new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Log in"] = new()
                            {
                                Name = "Log in",
                                SourceFile = Path.Combine(tempRoot, "steps.yaml"),
                                Implementation = new StepImplementationBinding { Plugin = "builtin", Operation = "login" }
                            }
                        },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                }
            });

            var run = new StoredRunResult
            {
                Result = new RunResult
                {
                    Metadata = new RunMetadata
                    {
                        RunId = "run-select",
                        Profile = "local",
                        StartedAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero)
                    },
                    Flows = []
                }
            };
            state.Runs.Add(run);

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ExplorerPanel>();

            cut.Find("[data-testid='explorer-flow-flow-alpha']").Click();
            Assert.Equal(flowPath, state.SelectedFlow?.FilePath);

            cut.FindAll("[data-testid='explorer-capabilities-section'] .explorer-button")
                .Single(button => button.TextContent.Contains("Authentication", StringComparison.Ordinal))
                .Click();
            Assert.Equal("Authentication", state.SelectionHeadline);

            cut.FindAll("[data-testid='explorer-fixtures-section'] .explorer-button")
                .Single(button => button.TextContent.Contains("browser", StringComparison.Ordinal))
                .Click();
            Assert.Equal("browser", state.SelectionHeadline);

            cut.FindAll("[data-testid='explorer-steps-section'] .explorer-button")
                .Single(button => button.TextContent.Contains("Log in", StringComparison.Ordinal))
                .Click();
            Assert.Equal("Log in", state.SelectionHeadline);

            cut.FindAll("[data-testid='explorer-runs-section'] .explorer-button")
                .Single(button => button.TextContent.Contains("run-select", StringComparison.Ordinal))
                .Click();
            Assert.Equal("run-select", state.SelectedRun?.Result.Metadata.RunId);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void ExplorerPanel_private_helpers_cover_empty_test_ids_and_button_classes()
    {
        var toTestIdSegment = typeof(Cress.Studio.Web.Components.Studio.ExplorerPanel)
            .GetMethod("ToTestIdSegment", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ToTestIdSegment was not found.");
        var getExplorerButtonClass = typeof(Cress.Studio.Web.Components.Studio.ExplorerPanel)
            .GetMethod("GetExplorerButtonClass", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GetExplorerButtonClass was not found.");

        Assert.Equal("empty", Assert.IsType<string>(toTestIdSegment.Invoke(null, [null])));
        Assert.Equal("flow-alpha-beta", Assert.IsType<string>(toTestIdSegment.Invoke(null, ["Flow Alpha/Beta"])));
        Assert.Equal("explorer-button selected", Assert.IsType<string>(getExplorerButtonClass.Invoke(null, [true])));
        Assert.Equal("explorer-button", Assert.IsType<string>(getExplorerButtonClass.Invoke(null, [false])));
    }
}
