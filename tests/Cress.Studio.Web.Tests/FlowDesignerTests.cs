using Bunit;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;
using Microsoft.AspNetCore.Components;

namespace Cress.Studio.Web.Tests;

public sealed class FlowDesignerTests : TestContext
{
    [Fact]
    public void FlowDesigner_renders_empty_state_without_document()
    {
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowDesigner>();

        Assert.Contains("Select or create a flow", cut.Markup);
    }

    [Fact]
    public void FlowDesigner_renders_metadata_and_rows()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            FilePath = @"C:\workspace\flows\checkout.flow.yaml",
            Id = "checkout-flow",
            Name = "Checkout flow",
            CapabilityId = "orders",
            Summary = "Validates checkout.",
            Status = "ready",
            TagsText = "checkout, smoke",
            Fixtures =
            [
                new EditableFixture
                {
                    Alias = "customer",
                    Use = "persona.customer"
                }
            ],
            Actions =
            [
                new EditableExecutable
                {
                    Name = "order.start",
                    InputsText = "path=/checkout"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "order.completed",
                    InputsText = "status=200"
                }
            ]
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowDesigner>(parameters => parameters
            .Add(item => item.Document, document)
            .Add(item => item.CapabilityOptions, new[] { string.Empty, "orders" })
            .Add(item => item.Analysis, new FlowEditorAnalysis
            {
                Summary = "1 warning",
                Diagnostics =
                [
                    new FlowEditorDiagnostic("action:0", Cress.Core.Models.DiagnosticSeverity.Warning, "Missing input", "Add a path value.")
                ],
                QuickActions =
                [
                    new FlowQuickAction("template.web-smoke", "Web smoke template", "Adds a starter flow.", "Templates")
                ]
            }));

        Assert.Contains("Checkout flow", cut.Markup);
        Assert.Contains("customer", cut.Markup);
        Assert.Contains("order.start", cut.Markup);
        Assert.Contains("order.completed", cut.Markup);
        Assert.Contains("Web smoke template", cut.Markup);
        Assert.Contains("Missing input", cut.Markup);
        Assert.Contains("Flow map", cut.Markup);
        Assert.Contains("Gherkin preview", cut.Markup);
        Assert.Contains("Feature: Checkout flow", cut.Markup);
        Assert.Contains("flow-graph-node-flow-start", cut.Markup);
        Assert.Contains("flow-graph-node-flow-end", cut.Markup);
        Assert.Equal(3, cut.FindAll("table").Count);
    }

    [Fact]
    public void FlowDesigner_renders_human_friendly_action_translations()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "calc-flow",
            Name = "Calculator flow",
            Actions =
            [
                new EditableExecutable
                {
                    Name = "ui.invoke",
                    InputsText = "selector=#clearButton"
                }
            ],
            Expectations =
            [
                new EditableExecutable
                {
                    Name = "ui.assert-text",
                    InputsText = "selector=#CalculatorResults\ntext=Display is 4."
                }
            ]
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowDesigner>(parameters => parameters
            .Add(item => item.Document, document)
            .Add(item => item.Analysis, new FlowEditorAnalysis()));

        Assert.Contains("When the user clicks the clear button", cut.Find("[data-testid='action-translation-0']").TextContent);
        Assert.Contains("Then the Calculator Results accessibility text should display \"Display is 4.\"", cut.Find("[data-testid='expectation-translation-0']").TextContent);
    }

    [Theory]
    [InlineData("draft", "flow-status-pill--draft")]
    [InlineData("disabled", "flow-status-pill--disabled")]
    [InlineData("ready", "flow-status-pill--ready")]
    [InlineData("custom", "")]
    public void FlowDesigner_renders_status_pill_variants(string status, string expectedClass)
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "status-flow",
            Name = "Status flow",
            Status = status
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowDesigner>(parameters => parameters
            .Add(item => item.Document, document)
            .Add(item => item.Analysis, new FlowEditorAnalysis()));

        var statusPill = cut.Find(".flow-status-pill");
        Assert.Contains(status, statusPill.TextContent, StringComparison.Ordinal);
        if (string.IsNullOrEmpty(expectedClass))
        {
            Assert.DoesNotContain("flow-status-pill--", statusPill.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(expectedClass, statusPill.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FlowDesigner_hides_optional_metadata_when_not_supplied()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "bare-flow",
            Name = "Bare flow"
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowDesigner>(parameters => parameters
            .Add(item => item.Document, document)
            .Add(item => item.Analysis, new FlowEditorAnalysis()));

        Assert.DoesNotContain("check-item-badge", cut.Markup);
        Assert.DoesNotContain("aria-label=\"Flow tags\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No fixtures — add one", cut.Markup);
        Assert.Contains("No actions — add steps", cut.Markup);
        Assert.Contains("No expectations — add assertions", cut.Markup);
    }

    [Fact]
    public void FlowDesigner_renders_error_and_info_diagnostics()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "diag-flow",
            Name = "Diagnostic flow"
        });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowDesigner>(parameters => parameters
            .Add(item => item.Document, document)
            .Add(item => item.Analysis, new FlowEditorAnalysis
            {
                Diagnostics =
                [
                    new FlowEditorDiagnostic("flow", Cress.Core.Models.DiagnosticSeverity.Error, "Broken flow", null),
                    new FlowEditorDiagnostic("flow", Cress.Core.Models.DiagnosticSeverity.Info, "FYI", "Read the note.")
                ]
            }));

        Assert.Contains("diagnostic-item--error", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("diagnostic-item--info", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Broken flow", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Read the note.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FlowDesigner_invokes_quick_action_and_add_remove_callbacks()
    {
        var document = FlowDocumentViewModel.FromDocument(new FlowEditorDocument
        {
            Id = "callback-flow",
            Name = "Callback flow",
            Fixtures = [new EditableFixture()],
            Actions = [new EditableExecutable { Name = "step.one" }],
            Expectations = [new EditableExecutable { Name = "step.two" }]
        });

        string? quickActionId = null;
        var addFixtureCalls = 0;
        var removeFixtureIndex = -1;
        var addActionCalls = 0;
        var removeActionIndex = -1;
        var addExpectationCalls = 0;
        var removeExpectationIndex = -1;

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.FlowDesigner>(parameters => parameters
            .Add(item => item.Document, document)
            .Add(item => item.Analysis, new FlowEditorAnalysis
            {
                QuickActions = [new FlowQuickAction("qa-id", "Quick action", "desc", "group")]
            })
            .Add(item => item.OnQuickAction, EventCallback.Factory.Create<string>(this, id => quickActionId = id))
            .Add(item => item.OnAddFixture, EventCallback.Factory.Create(this, () => addFixtureCalls++))
            .Add(item => item.OnRemoveFixture, EventCallback.Factory.Create<int>(this, index => removeFixtureIndex = index))
            .Add(item => item.OnAddAction, EventCallback.Factory.Create(this, () => addActionCalls++))
            .Add(item => item.OnRemoveAction, EventCallback.Factory.Create<int>(this, index => removeActionIndex = index))
            .Add(item => item.OnAddExpectation, EventCallback.Factory.Create(this, () => addExpectationCalls++))
            .Add(item => item.OnRemoveExpectation, EventCallback.Factory.Create<int>(this, index => removeExpectationIndex = index)));

        cut.FindAll("button.action-button").First(button => button.TextContent.Contains("Quick action", StringComparison.Ordinal)).Click();
        cut.FindAll("button.action-button").First(button => button.TextContent.Contains("Add fixture", StringComparison.Ordinal)).Click();
        cut.FindAll("table")[0].QuerySelector("button.action-button.danger")!.Click();
        cut.FindAll("button.action-button").First(button => button.TextContent.Contains("Add action", StringComparison.Ordinal)).Click();
        cut.FindAll("table")[1].QuerySelector("button.action-button.danger")!.Click();
        cut.FindAll("button.action-button").First(button => button.TextContent.Contains("Add expectation", StringComparison.Ordinal)).Click();
        cut.FindAll("table")[2].QuerySelector("button.action-button.danger")!.Click();

        Assert.Equal("qa-id", quickActionId);
        Assert.Equal(1, addFixtureCalls);
        Assert.True(removeFixtureIndex >= 0);
        Assert.Equal(1, addActionCalls);
        Assert.True(removeActionIndex >= 0);
        Assert.Equal(1, addExpectationCalls);
        Assert.True(removeExpectationIndex >= 0);
    }
}
