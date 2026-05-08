using Bunit;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;

namespace Cress.Studio.Web.Tests;

public sealed class FlowDesignerTests : TestContext
{
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
}
