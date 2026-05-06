using System.Text;
using System.Text.RegularExpressions;
using Cress.Core.Models;

namespace Cress.Importers.Playwright;

/// <summary>
/// Converts a Playwright codegen <c>.ts</c> or <c>.js</c> file into a <see cref="CressFlow"/>.
/// Uses lightweight regex matching — not a full TypeScript/JavaScript parser.
/// Handles the predictable shape produced by <c>playwright codegen</c>.
/// </summary>
public sealed class PlaywrightCodegenImporter
{
    // -------------------------------------------------------------------------
    // Regex patterns — one per recognised Playwright codegen call
    // -------------------------------------------------------------------------

    // page.goto('url') / page.goto("url")
    private static readonly Regex RxGoto = new(
        @"await\s+page\.goto\(\s*['""](?<url>[^'""]+)['""]\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.getByRole('role', { name: 'label' }).click()
    private static readonly Regex RxGetByRoleClick = new(
        @"await\s+page\.getByRole\(\s*['""](?<role>[^'""]+)['""]\s*,\s*\{\s*name\s*:\s*['""](?<label>[^'""]+)['""]\s*\}\s*\)\.click\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.getByRole('role').click()  (no name)
    private static readonly Regex RxGetByRoleClickNoName = new(
        @"await\s+page\.getByRole\(\s*['""](?<role>[^'""]+)['""]\s*\)\.click\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.getByTestId('id').click()
    private static readonly Regex RxGetByTestIdClick = new(
        @"await\s+page\.getByTestId\(\s*['""](?<testId>[^'""]+)['""]\s*\)\.click\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.getByLabel('label').fill('value')
    private static readonly Regex RxGetByLabelFill = new(
        @"await\s+page\.getByLabel\(\s*['""](?<label>[^'""]+)['""]\s*\)\.fill\(\s*['""](?<value>[^'""]*)['""\s*]\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.getByLabel('label').click()
    private static readonly Regex RxGetByLabelClick = new(
        @"await\s+page\.getByLabel\(\s*['""](?<label>[^'""]+)['""]\s*\)\.click\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.getByText('text').click()
    private static readonly Regex RxGetByTextClick = new(
        @"await\s+page\.getByText\(\s*['""](?<text>[^'""]+)['""]\s*\)\.click\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.getByPlaceholder('placeholder').fill('value')
    private static readonly Regex RxGetByPlaceholderFill = new(
        @"await\s+page\.getByPlaceholder\(\s*['""](?<placeholder>[^'""]+)['""]\s*\)\.fill\(\s*['""](?<value>[^'""]*)['""\s*]\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.locator('xpath=...').click()
    // Use lookahead to properly capture xpath that may contain the opposite quote character
    private static readonly Regex RxLocatorXpathClickSingle = new(
        @"await\s+page\.locator\(\s*'xpath=(?<xpath>[^']+)'\s*\)\.click\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxLocatorXpathClickDouble = new(
        @"await\s+page\.locator\(\s*""xpath=(?<xpath>[^""]+)""\s*\)\.click\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.locator('css-selector').click()  (non-xpath)
    private static readonly Regex RxLocatorCssClick = new(
        @"await\s+page\.locator\(\s*['""](?<cssSelector>[^'""]+)['""]\s*\)\.click\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.locator('...').fill('value')
    private static readonly Regex RxLocatorFill = new(
        @"await\s+page\.locator\(\s*['""](?<cssSelector>[^'""]+)['""]\s*\)\.fill\(\s*['""](?<value>[^'""]*)['""\s*]\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.keyboard.press('key')
    private static readonly Regex RxKeyboardPress = new(
        @"await\s+page\.keyboard\.press\(\s*['""](?<key>[^'""]+)['""]\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // page.waitForURL('url')
    private static readonly Regex RxWaitForUrl = new(
        @"await\s+page\.waitForURL\(\s*['""](?<url>[^'""]+)['""]\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // expect(page.getByX(...)).toBeVisible()
    private static readonly Regex RxExpectVisible = new(
        @"await\s+expect\(\s*page\.(?<locatorExpr>[^)]+(?:\([^)]*\))*[^)]*)\s*\)\.toBeVisible\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // expect(page.getByX(...)).toHaveText('text')
    private static readonly Regex RxExpectHaveText = new(
        @"await\s+expect\(\s*page\.(?<locatorExpr>[^)]+(?:\([^)]*\))*[^)]*)\s*\)\.toHaveText\(\s*['""](?<expected>[^'""]+)['""]\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Sub-patterns to parse the locator expression inside expect(page.XXX())
    private static readonly Regex RxLocExprGetByRole = new(
        @"getByRole\(\s*['""](?<role>[^'""]+)['""]\s*(?:,\s*\{\s*name\s*:\s*['""](?<label>[^'""]+)['""]\s*\})?\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxLocExprGetByLabel = new(
        @"getByLabel\(\s*['""](?<label>[^'""]+)['""]\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxLocExprGetByText = new(
        @"getByText\(\s*['""](?<text>[^'""]+)['""]\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxLocExprGetByTestId = new(
        @"getByTestId\(\s*['""](?<testId>[^'""]+)['""]\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Lines that are imports, TypeScript boilerplate, or the test() wrapper
    private static readonly Regex RxSkipLine = new(
        @"^\s*(?:import\s|export\s|\/\/|\/\*|\*|test\s*\(|describe\s*\(|beforeAll\s*\(|afterAll\s*\(|afterEach\s*\(|beforeEach\s*\(|\}\s*\);\s*$|\}\s*$|const\s+\{|async\s*\(\s*\{)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parse <paramref name="codegenText"/> (Playwright codegen .ts/.js file content)
    /// and return a <see cref="CressFlow"/> ready for serialization.
    /// </summary>
    /// <param name="codegenText">Full file contents.</param>
    /// <param name="flowName">Optional override for the flow name; derived from test name otherwise.</param>
    public CressFlow Import(string codegenText, string? flowName = null)
    {
        var lines = codegenText.ReplaceLineEndings("\n").Split('\n');

        // Try to extract the test name from the test('name', ...) signature
        var derivedName = TryExtractTestName(codegenText) ?? flowName ?? "imported-flow";

        var whenActions = new List<FlowAction>();
        var thenExpectations = new List<FlowExpectation>();

        foreach (var rawLine in lines)
        {
            // Trim both ends so leading indentation does not interfere with regex anchors
            var line = rawLine.Trim();

            // Skip structural/boilerplate lines
            if (ShouldSkipLine(line))
            {
                continue;
            }

            // Must contain "await" — otherwise not an action line
            if (!line.Contains("await", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryMapLine(line, out var action, out var expectation))
            {
                if (action is not null)
                {
                    whenActions.Add(action);
                }

                if (expectation is not null)
                {
                    thenExpectations.Add(expectation);
                }
            }
            else
            {
                // Unrecognized call — emit a TODO comment-step
                var unknown = ExtractUnknownCallSnippet(line);
                whenActions.Add(new FlowAction
                {
                    Step = "unknown",
                    With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["comment"] = $"TODO: {unknown}"
                    }
                });
            }
        }

        var name = flowName ?? derivedName;
        return new CressFlow
        {
            Version = 1,
            Id = SlugifyName(name),
            Name = name,
            Summary = $"Imported from Playwright codegen on {DateTimeOffset.UtcNow:yyyy-MM-dd}",
            Tags = ["playwright-codegen", "imported"],
            When = whenActions,
            Then = thenExpectations
        };
    }

    // -------------------------------------------------------------------------
    // Line mapping
    // -------------------------------------------------------------------------

    private bool TryMapLine(
        string line,
        out FlowAction? action,
        out FlowExpectation? expectation)
    {
        action = null;
        expectation = null;

        // --- expect(...).toBeVisible() ---
        var mVisible = RxExpectVisible.Match(line);
        if (mVisible.Success)
        {
            var with = ParseLocatorExpression(mVisible.Groups["locatorExpr"].Value);
            expectation = new FlowExpectation
            {
                Expect = "ui.assert-visible",
                With = with.Count > 0 ? with : null
            };
            return true;
        }

        // --- expect(...).toHaveText(...) ---
        var mHaveText = RxExpectHaveText.Match(line);
        if (mHaveText.Success)
        {
            var with = ParseLocatorExpression(mHaveText.Groups["locatorExpr"].Value);
            with["expected"] = mHaveText.Groups["expected"].Value;
            expectation = new FlowExpectation
            {
                Expect = "ui.assert-text",
                With = with.Count > 0 ? with : null
            };
            return true;
        }

        // --- page.goto(url) ---
        var mGoto = RxGoto.Match(line);
        if (mGoto.Success)
        {
            action = new FlowAction
            {
                Step = "browser.navigate",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["url"] = mGoto.Groups["url"].Value
                }
            };
            return true;
        }

        // --- page.waitForURL(url) ---
        var mWaitUrl = RxWaitForUrl.Match(line);
        if (mWaitUrl.Success)
        {
            action = new FlowAction
            {
                Step = "browser.wait-for-url",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["url"] = mWaitUrl.Groups["url"].Value
                }
            };
            return true;
        }

        // --- page.keyboard.press(key) ---
        var mKey = RxKeyboardPress.Match(line);
        if (mKey.Success)
        {
            action = new FlowAction
            {
                Step = "ui.press-key",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["key"] = mKey.Groups["key"].Value
                }
            };
            return true;
        }

        // --- page.getByRole(role, { name }).click() ---
        var mRoleClick = RxGetByRoleClick.Match(line);
        if (mRoleClick.Success)
        {
            var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["role"] = mRoleClick.Groups["role"].Value,
                ["label"] = mRoleClick.Groups["label"].Value
            };
            action = new FlowAction { Step = "ui.click", With = with };
            return true;
        }

        // --- page.getByRole(role).click() (no name) ---
        var mRoleClickNoName = RxGetByRoleClickNoName.Match(line);
        if (mRoleClickNoName.Success)
        {
            action = new FlowAction
            {
                Step = "ui.click",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["role"] = mRoleClickNoName.Groups["role"].Value
                }
            };
            return true;
        }

        // --- page.getByTestId(id).click() ---
        var mTestIdClick = RxGetByTestIdClick.Match(line);
        if (mTestIdClick.Success)
        {
            action = new FlowAction
            {
                Step = "ui.click",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["testId"] = mTestIdClick.Groups["testId"].Value
                }
            };
            return true;
        }

        // --- page.getByLabel(label).fill(value) ---
        var mLabelFill = RxGetByLabelFill.Match(line);
        if (mLabelFill.Success)
        {
            action = new FlowAction
            {
                Step = "ui.fill",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["label"] = mLabelFill.Groups["label"].Value,
                    ["value"] = mLabelFill.Groups["value"].Value
                }
            };
            return true;
        }

        // --- page.getByLabel(label).click() ---
        var mLabelClick = RxGetByLabelClick.Match(line);
        if (mLabelClick.Success)
        {
            action = new FlowAction
            {
                Step = "ui.click",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["label"] = mLabelClick.Groups["label"].Value
                }
            };
            return true;
        }

        // --- page.getByText(text).click() ---
        var mTextClick = RxGetByTextClick.Match(line);
        if (mTextClick.Success)
        {
            action = new FlowAction
            {
                Step = "ui.click",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["text"] = mTextClick.Groups["text"].Value
                }
            };
            return true;
        }

        // --- page.getByPlaceholder(placeholder).fill(value) ---
        var mPlaceholderFill = RxGetByPlaceholderFill.Match(line);
        if (mPlaceholderFill.Success)
        {
            action = new FlowAction
            {
                Step = "ui.fill",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["placeholder"] = mPlaceholderFill.Groups["placeholder"].Value,
                    ["value"] = mPlaceholderFill.Groups["value"].Value
                }
            };
            return true;
        }

        // --- page.locator('xpath=...').click() or page.locator("xpath=...").click() ---
        var mXpathClick = RxLocatorXpathClickSingle.Match(line);
        if (!mXpathClick.Success)
        {
            mXpathClick = RxLocatorXpathClickDouble.Match(line);
        }

        if (mXpathClick.Success)
        {
            action = new FlowAction
            {
                Step = "ui.click",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["xpath"] = mXpathClick.Groups["xpath"].Value
                }
            };
            return true;
        }

        // --- page.locator('css').click() (fallback after xpath check) ---
        var mCssClick = RxLocatorCssClick.Match(line);
        if (mCssClick.Success)
        {
            action = new FlowAction
            {
                Step = "ui.click",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cssSelector"] = mCssClick.Groups["cssSelector"].Value
                }
            };
            return true;
        }

        // --- page.locator('...').fill(value) ---
        var mLocFill = RxLocatorFill.Match(line);
        if (mLocFill.Success)
        {
            action = new FlowAction
            {
                Step = "ui.fill",
                With = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cssSelector"] = mLocFill.Groups["cssSelector"].Value,
                    ["value"] = mLocFill.Groups["value"].Value
                }
            };
            return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Locator expression parser (for expect() inner expressions)
    // -------------------------------------------------------------------------

    private static Dictionary<string, string> ParseLocatorExpression(string expr)
    {
        var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var mRole = RxLocExprGetByRole.Match(expr);
        if (mRole.Success)
        {
            with["role"] = mRole.Groups["role"].Value;
            if (mRole.Groups["label"].Success && !string.IsNullOrEmpty(mRole.Groups["label"].Value))
            {
                with["label"] = mRole.Groups["label"].Value;
            }

            return with;
        }

        var mLabel = RxLocExprGetByLabel.Match(expr);
        if (mLabel.Success)
        {
            with["label"] = mLabel.Groups["label"].Value;
            return with;
        }

        var mText = RxLocExprGetByText.Match(expr);
        if (mText.Success)
        {
            with["text"] = mText.Groups["text"].Value;
            return with;
        }

        var mTestId = RxLocExprGetByTestId.Match(expr);
        if (mTestId.Success)
        {
            with["testId"] = mTestId.Groups["testId"].Value;
            return with;
        }

        // Fallback: store raw expression
        if (!string.IsNullOrWhiteSpace(expr))
        {
            with["locatorExpr"] = expr.Trim();
        }

        return with;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool ShouldSkipLine(string line)
    {
        var trimmed = line.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return true;
        }

        return RxSkipLine.IsMatch(trimmed);
    }

    private static readonly Regex RxTestName = new(
        @"test\s*\(\s*['""](?<name>[^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string? TryExtractTestName(string code)
    {
        var m = RxTestName.Match(code);
        return m.Success ? m.Groups["name"].Value : null;
    }

    private static string ExtractUnknownCallSnippet(string line)
    {
        // Trim leading whitespace and "await " prefix for readability
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("await ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["await ".Length..];
        }

        // Limit length
        return trimmed.Length > 120 ? trimmed[..120] + "..." : trimmed;
    }

    /// <summary>Convert a human-readable name to a kebab/dot-slug.</summary>
    private static string SlugifyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "imported-flow";
        }

        var slug = Regex.Replace(name.ToLowerInvariant(), @"[:\s]+", "-")
                        .Replace(",", string.Empty)
                        .Replace("'", string.Empty)
                        .Replace("\"", string.Empty);
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);
        slug = Regex.Replace(slug, @"-{2,}", "-");
        slug = slug.Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "imported-flow" : slug;
    }
}
