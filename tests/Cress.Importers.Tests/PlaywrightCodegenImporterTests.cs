using Cress.Core.Models;
using Cress.Importers.Playwright;

namespace Cress.Importers.Tests;

public class PlaywrightCodegenImporterTests
{
    private readonly PlaywrightCodegenImporter _importer = new();

    // -------------------------------------------------------------------------
    // Individual call mapping tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Goto_MapsTo_BrowserNavigate()
    {
        var code = "  await page.goto('https://example.com/');";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("browser.navigate", action.Step);
        Assert.Equal("https://example.com/", action.With!["url"]);
    }

    [Fact]
    public void GetByRoleClick_WithName_MapsTo_UiClick_WithRoleAndLabel()
    {
        var code = "  await page.getByRole('button', { name: 'Sign in' }).click();";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.click", action.Step);
        Assert.Equal("button", action.With!["role"]);
        Assert.Equal("Sign in", action.With["label"]);
    }

    [Fact]
    public void GetByRoleClick_WithoutName_MapsTo_UiClick_WithRole()
    {
        var code = "  await page.getByRole('link').click();";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.click", action.Step);
        Assert.Equal("link", action.With!["role"]);
        Assert.False(action.With.ContainsKey("label"));
    }

    [Fact]
    public void GetByTestId_Click_MapsTo_UiClick_WithTestId()
    {
        var code = "  await page.getByTestId('submit-btn').click();";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.click", action.Step);
        Assert.Equal("submit-btn", action.With!["testId"]);
    }

    [Fact]
    public void GetByLabel_Fill_MapsTo_UiFill_WithLabelAndValue()
    {
        var code = "  await page.getByLabel('Email').fill('user@example.com');";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.fill", action.Step);
        Assert.Equal("Email", action.With!["label"]);
        Assert.Equal("user@example.com", action.With["value"]);
    }

    [Fact]
    public void GetByLabel_Click_MapsTo_UiClick_WithLabel()
    {
        var code = "  await page.getByLabel('Email').click();";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.click", action.Step);
        Assert.Equal("Email", action.With!["label"]);
    }

    [Fact]
    public void GetByText_Click_MapsTo_UiClick_WithText()
    {
        var code = "  await page.getByText('Welcome').click();";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.click", action.Step);
        Assert.Equal("Welcome", action.With!["text"]);
    }

    [Fact]
    public void GetByPlaceholder_Fill_MapsTo_UiFill_WithPlaceholder()
    {
        var code = "  await page.getByPlaceholder('Search').fill('query');";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.fill", action.Step);
        Assert.Equal("Search", action.With!["placeholder"]);
        Assert.Equal("query", action.With["value"]);
    }

    [Fact]
    public void LocatorXpath_Click_MapsTo_UiClick_WithXpath()
    {
        var code = "  await page.locator('xpath=//button[@id=\"ok\"]').click();";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.click", action.Step);
        Assert.Equal("//button[@id=\"ok\"]", action.With!["xpath"]);
    }

    [Fact]
    public void LocatorXpath_Click_WithDoubleQuotedLocator_MapsTo_UiClick_WithXpath()
    {
        var code = "  await page.locator(\"xpath=//div[@data-id='ok']\").click();";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.click", action.Step);
        Assert.Equal("//div[@data-id='ok']", action.With!["xpath"]);
    }

    [Fact]
    public void LocatorCss_Click_MapsTo_UiClick_WithCssSelector()
    {
        var code = "  await page.locator('.submit-button').click();";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.click", action.Step);
        Assert.Equal(".submit-button", action.With!["cssSelector"]);
    }

    [Fact]
    public void LocatorFill_MapsTo_UiFill_WithCssSelector_AndValue()
    {
        var code = "  await page.locator('.search-box').fill('term');";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.fill", action.Step);
        Assert.Equal(".search-box", action.With!["cssSelector"]);
        Assert.Equal("term", action.With["value"]);
    }

    [Fact]
    public void KeyboardPress_MapsTo_UiPressKey()
    {
        var code = "  await page.keyboard.press('Enter');";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("ui.press-key", action.Step);
        Assert.Equal("Enter", action.With!["key"]);
    }

    [Fact]
    public void WaitForUrl_MapsTo_BrowserWaitForUrl()
    {
        var code = "  await page.waitForURL('https://example.com/dashboard');";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("browser.wait-for-url", action.Step);
        Assert.Equal("https://example.com/dashboard", action.With!["url"]);
    }

    [Fact]
    public void ExpectToBeVisible_MapsTo_UiAssertVisible_InThenSection()
    {
        var code = "  await expect(page.getByText('Welcome')).toBeVisible();";
        var flow = _importer.Import(code, "test");

        Assert.Empty(flow.When);
        var expectation = Assert.Single(flow.Then);
        Assert.Equal("ui.assert-visible", expectation.Expect);
        Assert.Equal("Welcome", expectation.With!["text"]);
    }

    [Fact]
    public void ExpectToHaveText_MapsTo_UiAssertText_WithExpected()
    {
        var code = "  await expect(page.getByRole('heading', { name: 'Dashboard' })).toHaveText('Dashboard');";
        var flow = _importer.Import(code, "test");

        Assert.Empty(flow.When);
        var expectation = Assert.Single(flow.Then);
        Assert.Equal("ui.assert-text", expectation.Expect);
        Assert.Equal("Dashboard", expectation.With!["expected"]);
    }

    [Fact]
    public void ExpectToBeVisible_WithTestId_MapsTo_TestIdLocator()
    {
        var code = "  await expect(page.getByTestId('dashboard')).toBeVisible();";
        var flow = _importer.Import(code, "test");

        var expectation = Assert.Single(flow.Then);
        Assert.Equal("ui.assert-visible", expectation.Expect);
        Assert.Equal("dashboard", expectation.With!["testId"]);
    }

    [Fact]
    public void ExpectToBeVisible_WithUnknownLocator_PreservesLocatorExpression()
    {
        var code = "  await expect(page.locator('.status-pill')).toBeVisible();";
        var flow = _importer.Import(code, "test");

        var expectation = Assert.Single(flow.Then);
        Assert.Equal("ui.assert-visible", expectation.Expect);
        Assert.Equal("locator('.status-pill')", expectation.With!["locatorExpr"]);
    }

    [Fact]
    public void UnrecognizedCall_EmitsUnknownStep_WithTodoComment()
    {
        var code = "  await page.hover('#menu-item');";
        var flow = _importer.Import(code, "test");

        var action = Assert.Single(flow.When);
        Assert.Equal("unknown", action.Step);
        Assert.True(action.With!.ContainsKey("comment"));
        Assert.Contains("TODO:", action.With["comment"]);
    }

    // -------------------------------------------------------------------------
    // TypeScript boilerplate stripping
    // -------------------------------------------------------------------------

    [Fact]
    public void TypeScriptImports_AreStripped()
    {
        var code = """
            import { test, expect } from '@playwright/test';

            test('my test', async ({ page }) => {
              await page.goto('https://example.com/');
            });
            """;
        var flow = _importer.Import(code);

        // Only the goto should produce a step — no "import" or "test(" steps
        Assert.Single(flow.When);
        Assert.Equal("browser.navigate", flow.When[0].Step);
    }

    [Fact]
    public void TestName_IsExtractedFrom_TestFunction()
    {
        var code = """
            import { test, expect } from '@playwright/test';

            test('user login flow', async ({ page }) => {
              await page.goto('https://example.com/');
            });
            """;
        var flow = _importer.Import(code);

        Assert.Equal("user login flow", flow.Name);
        Assert.Equal("user-login-flow", flow.Id);
    }

    [Fact]
    public void EmptyInput_FallsBackToImportedFlowName()
    {
        var flow = _importer.Import(string.Empty);

        Assert.Equal("imported-flow", flow.Name);
        Assert.Equal("imported-flow", flow.Id);
        Assert.Empty(flow.When);
        Assert.Empty(flow.Then);
    }

    // -------------------------------------------------------------------------
    // End-to-end: realistic 10-line codegen sample
    // -------------------------------------------------------------------------

    [Fact]
    public void EndToEnd_RealisticCodegenSample_ProducesExpectedFlow()
    {
        var code = """
            import { test, expect } from '@playwright/test';

            test('sign in and verify dashboard', async ({ page }) => {
              await page.goto('https://app.example.com/login');
              await page.getByRole('button', { name: 'Sign in' }).click();
              await page.getByLabel('Email').fill('user@example.com');
              await page.getByLabel('Password').fill('secret123');
              await page.getByRole('button', { name: 'Submit' }).click();
              await page.waitForURL('https://app.example.com/dashboard');
              await expect(page.getByText('Welcome, User')).toBeVisible();
              await expect(page.getByRole('heading', { name: 'Dashboard' })).toHaveText('Dashboard');
              await page.keyboard.press('Escape');
            });
            """;

        var flow = _importer.Import(code);

        Assert.Equal("sign in and verify dashboard", flow.Name);
        Assert.Equal(7, flow.When.Count);
        Assert.Equal(2, flow.Then.Count);

        Assert.Equal("browser.navigate", flow.When[0].Step);
        Assert.Equal("ui.click", flow.When[1].Step);
        Assert.Equal("ui.fill", flow.When[2].Step);
        Assert.Equal("ui.fill", flow.When[3].Step);
        Assert.Equal("ui.click", flow.When[4].Step);
        Assert.Equal("browser.wait-for-url", flow.When[5].Step);
        Assert.Equal("ui.press-key", flow.When[6].Step);

        Assert.Equal("ui.assert-visible", flow.Then[0].Expect);
        Assert.Equal("ui.assert-text", flow.Then[1].Expect);

        // Verify tags include playwright-codegen
        Assert.Contains("playwright-codegen", flow.Tags);
        Assert.Contains("imported", flow.Tags);
    }
}
