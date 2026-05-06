using Cress.Core.Models;
using Cress.Exporters.Cypress;

namespace Cress.Exporters.Tests;

public sealed class CypressExporterTests
{
    private readonly CypressExporter _sut = new();

    // -------------------------------------------------------------------------
    // describe / it wrapper
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_EmptyFlow_ProducesDescribeItWrapper()
    {
        var flow = MakeFlow("my-test", []);
        var output = _sut.Export(flow);

        Assert.Contains("describe('my-test'", output);
        Assert.Contains("it('my-test'", output);
        Assert.Contains("});", output);
    }

    // -------------------------------------------------------------------------
    // browser.navigate
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_BrowserNavigate_EmitsCyVisit()
    {
        var flow = MakeFlow("nav", [
            new FlowAction { Step = "browser.navigate", With = With("url", "https://example.com/app") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.visit('https://example.com/app')", output);
    }

    // -------------------------------------------------------------------------
    // ui.click — locator strategies
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_ClickByTestId_EmitsFindByTestId()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("testId", "submit-btn") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.findByTestId('submit-btn').click()", output);
    }

    [Fact]
    public void Export_ClickByRoleAndLabel_EmitsFindByRole()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("role", "button", "label", "Sign in") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.findByRole('button', { name: 'Sign in' }).click()", output);
    }

    [Fact]
    public void Export_ClickByRoleOnly_EmitsFindByRoleNoName()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("role", "link") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.findByRole('link').click()", output);
    }

    [Fact]
    public void Export_ClickByText_EmitsCyContains()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("text", "Continue") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.contains('Continue').click()", output);
    }

    [Fact]
    public void Export_ClickByCssSelector_EmitsCyGet()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("cssSelector", ".btn-primary") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.get('.btn-primary').click()", output);
    }

    [Fact]
    public void Export_ClickByXpath_EmitsCyXpathWithComment()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("xpath", "//button[@id='go']") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.xpath", output);
        Assert.Contains("requires cypress-xpath", output);
    }

    [Fact]
    public void Export_ClickByAutomationId_EmitsGetWithIdAttribute()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click", With = With("automationId", "clearButton") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("[id=\"clearButton\"]", output);
        Assert.Contains("automationId is a desktop concept", output);
    }

    // -------------------------------------------------------------------------
    // ui.fill
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_FillByLabel_EmitsFindByLabelTextType()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.fill", With = With("label", "Email", "value", "user@example.com") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.findByLabelText('Email').type('user@example.com')", output);
    }

    [Fact]
    public void Export_FillByCssSelector_EmitsCyGetType()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.fill", With = With("cssSelector", "#search", "value", "hello") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.get('#search').type('hello')", output);
    }

    // -------------------------------------------------------------------------
    // HTTP steps
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_HttpGet_EmitsCyRequest()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "http.get", With = With("url", "https://api.example.com/items") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.request(", output);
        Assert.Contains("method: 'GET'", output);
        Assert.Contains("https://api.example.com/items", output);
    }

    // -------------------------------------------------------------------------
    // Expectations
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_AssertText_EmitsShouldHaveText()
    {
        var flow = MakeFlow("f", [],
            expectations: [
                new FlowExpectation { Expect = "ui.assert-text", With = With("cssSelector", "#result", "expected", "4") }
            ]);
        var output = _sut.Export(flow);

        Assert.Contains(".should('have.text', '4')", output);
    }

    [Fact]
    public void Export_AssertVisible_EmitsShouldBeVisible()
    {
        var flow = MakeFlow("f", [],
            expectations: [
                new FlowExpectation { Expect = "ui.assert-visible", With = With("testId", "success-msg") }
            ]);
        var output = _sut.Export(flow);

        Assert.Contains(".should('be.visible')", output);
    }

    [Fact]
    public void Export_AssertUrl_EmitsCyUrlShould()
    {
        var flow = MakeFlow("f", [],
            expectations: [
                new FlowExpectation { Expect = "browser.assert-url", With = With("expected", "/dashboard") }
            ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.url().should('include', '/dashboard')", output);
    }

    // -------------------------------------------------------------------------
    // End-to-end: httpbin GET flow
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_HttpbinGetFlow_ProducesValidCyFile()
    {
        var flow = new CressFlow
        {
            Version = 1,
            Id = "httpbin-get-smoke",
            Name = "GET /get returns 200 with request metadata",
            When =
            [
                new FlowAction { Step = "http.get", With = With("url", "https://httpbin.org/get") }
            ],
            Then =
            [
                new FlowExpectation { Expect = "http.assert-status", With = With("status", "200") }
            ]
        };

        var output = _sut.Export(flow);

        Assert.Contains("describe('GET /get returns 200 with request metadata'", output);
        Assert.Contains("cy.request(", output);
        Assert.Contains("httpbin.org/get", output);
    }

    // -------------------------------------------------------------------------
    // Header comment is always present
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_AlwaysIncludesGeneratedHeader()
    {
        var flow = MakeFlow("f", []);
        var output = _sut.Export(flow);

        Assert.Contains("Generated from Cress flow", output);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CressFlow MakeFlow(
        string name,
        List<FlowAction> actions,
        List<FlowExpectation>? expectations = null) =>
        new()
        {
            Version = 1,
            Id = name,
            Name = name,
            When = actions,
            Then = expectations ?? []
        };

    private static Dictionary<string, string> With(string k1, string v1) =>
        new(StringComparer.OrdinalIgnoreCase) { [k1] = v1 };

    private static Dictionary<string, string> With(string k1, string v1, string k2, string v2) =>
        new(StringComparer.OrdinalIgnoreCase) { [k1] = v1, [k2] = v2 };
}
