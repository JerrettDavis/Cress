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

    [Fact]
    public void Export_NormalizedFlow_ProducesEquivalentCyFile()
    {
        var flow = new NormalizedFlow
        {
            Version = 1,
            FlowId = "normalized",
            Name = "Normalized",
            Summary = "summary",
            Tags = ["smoke"],
            Actions =
            [
                new NormalizedExecutable
                {
                    Name = "browser.wait-for-url",
                    Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["url"] = "/dashboard"
                    }
                }
            ],
            Expectations =
            [
                new NormalizedExecutable
                {
                    Name = "ui.assert-window-title",
                    Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["title"] = "Dashboard"
                    }
                }
            ]
        };

        var output = _sut.Export(flow);

        Assert.Contains("cy.url().should('include', '/dashboard')", output);
        Assert.Contains("cy.title().should('include', 'Dashboard')", output);
    }

    [Fact]
    public void Export_BrowserWaitForUrl_EmitsCyUrlAssertion()
    {
        var flow = MakeFlow("wait", [
            new FlowAction { Step = "browser.wait-for-url", With = With("url", "/dashboard") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.url().should('include', '/dashboard')", output);
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

    [Theory]
    [InlineData("label", "Email", "cy.findByLabelText('Email').click()")]
    [InlineData("name", "email", "cy.get('[name=\"email\"]').click()")]
    public void Export_ClickByAdditionalLocator_EmitsExpectedCommand(string key, string value, string expected)
    {
        var flow = MakeFlow("f", [
            new FlowAction
            {
                Step = "ui.click",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = value
                }
            }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains(expected, output);
    }

    [Fact]
    public void Export_ClickWithoutLocator_EmitsTodoSelector()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.click" }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.get('/* TODO: add selector */').click()", output);
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

    [Theory]
    [InlineData("placeholder", "Search", "cy.findByPlaceholderText('Search').type('hello')")]
    [InlineData("testId", "search-box", "cy.findByTestId('search-box').type('hello')")]
    [InlineData("name", "query", "cy.get('[name=\"query\"]').type('hello')")]
    public void Export_FillByAdditionalLocator_EmitsExpectedCommand(string key, string locator, string expected)
    {
        var flow = MakeFlow("f", [
            new FlowAction
            {
                Step = "ui.fill",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = locator,
                    ["value"] = "hello"
                }
            }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains(expected, output);
    }

    [Fact]
    public void Export_FillByXpath_EmitsXpathCommentAndCommand()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.fill", With = With("xpath", "//input[@id='search']", "value", "hello") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.xpath('//input[@id=\\'search\\']').type('hello')", output);
        Assert.Contains("requires cypress-xpath plugin", output);
    }

    [Fact]
    public void Export_FillWithoutLocator_EmitsTodoComment()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.fill", With = With("value", "hello") }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("TODO: no supported locator for ui.fill", output);
    }

    [Theory]
    [InlineData("ENTER", "cy.focused().type('{ENTER}')")]
    [InlineData("Tab", "cy.focused().type('{Tab}')")]
    public void Export_PressKey_EmitsFocusedType(string key, string expected)
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.press-key", With = With("key", key) }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains(expected, output);
    }

    [Fact]
    public void Export_SelectCheckUncheckScreenshotUnknownAndUnmapped_AreRendered()
    {
        var flow = MakeFlow("f", [
            new FlowAction { Step = "ui.select", With = With("testId", "country", "value", "Canada") },
            new FlowAction { Step = "ui.check", With = With("testId", "terms") },
            new FlowAction { Step = "ui.uncheck", With = With("testId", "terms") },
            new FlowAction { Step = "ui.screenshot" },
            new FlowAction { Step = "unknown", With = With("comment", "custom comment") },
            new FlowAction { Step = "desktop.magic" }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.findByTestId('country').select('Canada')", output);
        Assert.Contains("cy.findByTestId('terms').check()", output);
        Assert.Contains("cy.findByTestId('terms').uncheck()", output);
        Assert.Contains("// cy.screenshot(); // ui.screenshot", output);
        Assert.Contains("// custom comment", output);
        Assert.Contains("// TODO: unmapped step 'desktop.magic'", output);
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

    [Fact]
    public void Export_HttpPost_IncludesHeadersAndBody()
    {
        var flow = MakeFlow("f", [
            new FlowAction
            {
                Step = "http.post",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["url"] = "https://api.example.com/items",
                    ["headers.Authorization"] = "Bearer token",
                    ["body"] = "{ foo: 'bar' }"
                }
            }
        ]);
        var output = _sut.Export(flow);

        Assert.Contains("method: 'POST'", output);
        Assert.Contains("'Authorization': 'Bearer token'", output);
        Assert.Contains("body: { foo: 'bar' }", output);
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

    [Fact]
    public void Export_AdditionalExpectations_EmitExpectedCommentsAndAssertions()
    {
        var flow = MakeFlow("f", [],
            expectations: [
                new FlowExpectation { Expect = "http.assert-url", With = With("url", "/api/orders") },
                new FlowExpectation { Expect = "http.assert-status", With = With("status", "201") },
                new FlowExpectation { Expect = "http.assert-json", With = With("path", "$.data.id") },
                new FlowExpectation { Expect = "ui.assert-window-title", With = With("title", "Dashboard") },
                new FlowExpectation { Expect = "desktop.assert-magic" }
            ]);
        var output = _sut.Export(flow);

        Assert.Contains("cy.url().should('include', '/api/orders')", output);
        Assert.Contains("response status should be 201", output);
        Assert.Contains("$.data.id", output);
        Assert.Contains("cy.title().should('include', 'Dashboard')", output);
        Assert.Contains("TODO: unmapped expectation 'desktop.assert-magic'", output);
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
