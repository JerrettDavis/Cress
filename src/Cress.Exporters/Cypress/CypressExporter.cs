using System.Text;
using Cress.Core.Models;

namespace Cress.Exporters.Cypress;

/// <summary>
/// Exports a <see cref="CressFlow"/> as a Cypress <c>.cy.ts</c> test file.
/// This is a lossy one-way conversion — some locator strategies require Cypress
/// Testing Library plugins (findByRole, findByLabelText, findByTestId).
/// </summary>
/// <remarks>
/// <para><b>Plugin requirements:</b></para>
/// <list type="bullet">
///   <item><term>testId / role / label</term><description>Requires @testing-library/cypress</description></item>
///   <item><term>xpath</term><description>Requires cypress-xpath</description></item>
/// </list>
/// <para>Locator priority for <c>ui.click</c> and <c>ui.fill</c>:</para>
/// <list type="number">
///   <item>testId</item>
///   <item>role + label</item>
///   <item>label (alone)</item>
///   <item>text</item>
///   <item>cssSelector</item>
///   <item>xpath</item>
///   <item>automationId (mapped to [id="..."] with comment)</item>
/// </list>
/// </remarks>
public sealed class CypressExporter
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Convert a <see cref="NormalizedFlow"/> to a <c>.cy.ts</c> file and return the text.
    /// </summary>
    public string Export(NormalizedFlow flow)
    {
        // Convert NormalizedFlow to CressFlow for export
        var raw = new CressFlow
        {
            Version = flow.Version,
            Id = flow.FlowId,
            Name = flow.Name,
            Summary = flow.Summary,
            Tags = flow.Tags,
            When = flow.Actions
                .Select(a => new FlowAction { Step = a.Name, With = a.Inputs.Count > 0 ? new Dictionary<string, string>(a.Inputs) : null })
                .ToList(),
            Then = flow.Expectations
                .Select(e => new FlowExpectation { Expect = e.Name, With = e.Inputs.Count > 0 ? new Dictionary<string, string>(e.Inputs) : null })
                .ToList()
        };
        return Export(raw);
    }

    /// <summary>
    /// Convert <paramref name="flow"/> to a <c>.cy.ts</c> file and return the text.
    /// </summary>
    public string Export(CressFlow flow)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("// Generated from Cress flow. Some locators may need adjustment for your Cypress setup.");
        sb.AppendLine("// Plugin notes:");
        sb.AppendLine("//   - testId / role / label locators require @testing-library/cypress");
        sb.AppendLine("//   - xpath locators require cypress-xpath");
        sb.AppendLine();

        var escapedName = EscapeString(flow.Name);
        sb.AppendLine($"describe('{escapedName}', () => {{");
        sb.AppendLine($"  it('{escapedName}', () => {{");

        // When actions
        foreach (var action in flow.When)
        {
            var line = MapAction(action);
            if (line is not null)
            {
                sb.AppendLine($"    {line}");
            }
        }

        // Then expectations
        foreach (var expectation in flow.Then)
        {
            var line = MapExpectation(expectation);
            if (line is not null)
            {
                sb.AppendLine($"    {line}");
            }
        }

        sb.AppendLine("  });");
        sb.AppendLine("});");

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Action mapping
    // -------------------------------------------------------------------------

    private static string? MapAction(FlowAction action)
    {
        var with = action.With ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return action.Step switch
        {
            "browser.navigate" => MapNavigate(with),
            "browser.wait-for-url" => MapWaitForUrl(with),
            "ui.click" => MapClick(with),
            "ui.fill" => MapFill(with),
            "ui.press-key" => MapPressKey(with),
            "ui.select" => MapSelect(with),
            "ui.check" => MapCheck(with, true),
            "ui.uncheck" => MapCheck(with, false),
            "http.get" => MapHttpRequest("GET", with),
            "http.post" => MapHttpRequest("POST", with),
            "http.put" => MapHttpRequest("PUT", with),
            "http.delete" => MapHttpRequest("DELETE", with),
            "http.patch" => MapHttpRequest("PATCH", with),
            "ui.screenshot" => "// cy.screenshot(); // ui.screenshot",
            "unknown" => MapUnknown(with),
            _ => $"// TODO: unmapped step '{action.Step}'"
        };
    }

    private static string MapNavigate(Dictionary<string, string> with)
    {
        var url = with.TryGetValue("url", out var u) ? u : "/";
        return $"cy.visit('{EscapeString(url)}');";
    }

    private static string MapWaitForUrl(Dictionary<string, string> with)
    {
        var url = with.TryGetValue("url", out var u) ? u : "";
        return $"cy.url().should('include', '{EscapeString(url)}');";
    }

    private static string MapClick(Dictionary<string, string> with)
    {
        var locator = ResolveLocator(with);
        return $"{locator}.click();";
    }

    private static string MapFill(Dictionary<string, string> with)
    {
        var value = with.TryGetValue("value", out var v) ? v : "";
        var escaped = EscapeString(value);

        // label is the primary key for fill
        if (with.TryGetValue("label", out var label))
        {
            return $"cy.findByLabelText('{EscapeString(label)}').type('{escaped}');";
        }

        if (with.TryGetValue("placeholder", out var placeholder))
        {
            return $"cy.findByPlaceholderText('{EscapeString(placeholder)}').type('{escaped}');";
        }

        if (with.TryGetValue("testId", out var testId))
        {
            return $"cy.findByTestId('{EscapeString(testId)}').type('{escaped}');";
        }

        if (with.TryGetValue("cssSelector", out var css))
        {
            return $"cy.get('{EscapeString(css)}').type('{escaped}');";
        }

        if (with.TryGetValue("xpath", out var xpath))
        {
            return $"// cy.xpath requires cypress-xpath plugin\n    cy.xpath('{EscapeString(xpath)}').type('{escaped}');";
        }

        if (with.TryGetValue("name", out var name))
        {
            return $"cy.get('[name=\"{EscapeString(name)}\"]').type('{escaped}');";
        }

        return $"// TODO: no supported locator for ui.fill — value: '{escaped}'";
    }

    private static string MapPressKey(Dictionary<string, string> with)
    {
        var key = with.TryGetValue("key", out var k) ? k : "";
        return $"cy.focused().type('{{{EscapeString(key)}}}');";
    }

    private static string MapSelect(Dictionary<string, string> with)
    {
        var value = with.TryGetValue("value", out var v) ? v : "";
        var locator = ResolveLocator(with);
        return $"{locator}.select('{EscapeString(value)}');";
    }

    private static string MapCheck(Dictionary<string, string> with, bool check)
    {
        var locator = ResolveLocator(with);
        return check ? $"{locator}.check();" : $"{locator}.uncheck();";
    }

    private static string MapHttpRequest(string method, Dictionary<string, string> with)
    {
        var url = with.TryGetValue("url", out var u) ? u : "";

        // Build headers and body
        var headers = with
            .Where(kv => kv.Key.StartsWith("headers.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                kv => kv.Key["headers.".Length..],
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);

        var body = with.TryGetValue("body", out var b) ? b : null;

        var sb = new StringBuilder();
        sb.Append($"cy.request({{ method: '{method}', url: '{EscapeString(url)}'");

        if (headers.Count > 0)
        {
            sb.Append(", headers: { ");
            sb.Append(string.Join(", ", headers.Select(h => $"'{EscapeString(h.Key)}': '{EscapeString(h.Value)}'")));
            sb.Append(" }");
        }

        if (body is not null)
        {
            sb.Append($", body: {body}");
        }

        sb.Append(" });");
        return sb.ToString();
    }

    private static string MapUnknown(Dictionary<string, string> with)
    {
        var comment = with.TryGetValue("comment", out var c) ? c : "unknown step";
        return $"// {comment}";
    }

    // -------------------------------------------------------------------------
    // Expectation mapping
    // -------------------------------------------------------------------------

    private static string? MapExpectation(FlowExpectation expectation)
    {
        var with = expectation.With ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return expectation.Expect switch
        {
            "ui.assert-text" => MapAssertText(with),
            "ui.assert-visible" => MapAssertVisible(with),
            "browser.assert-url" or "http.assert-url" => MapAssertUrl(with),
            "http.assert-status" => MapAssertStatus(with),
            "http.assert-json" => $"// cy.request result assertion for path '{(with.TryGetValue("path", out var p) ? p : "")}' — use cy.request().its()",
            "ui.assert-window-title" => $"// cy.title().should('include', '{EscapeString(with.TryGetValue("title", out var t) ? t : "")}');",
            _ => $"// TODO: unmapped expectation '{expectation.Expect}'"
        };
    }

    private static string MapAssertText(Dictionary<string, string> with)
    {
        var expected = with.TryGetValue("expected", out var e) ? e : "";
        var locator = ResolveLocator(with);
        return $"{locator}.should('have.text', '{EscapeString(expected)}');";
    }

    private static string MapAssertVisible(Dictionary<string, string> with)
    {
        var locator = ResolveLocator(with);
        return $"{locator}.should('be.visible');";
    }

    private static string MapAssertUrl(Dictionary<string, string> with)
    {
        var expected = with.TryGetValue("expected", out var e) ? e :
                       with.TryGetValue("url", out var u) ? u : "";
        return $"cy.url().should('include', '{EscapeString(expected)}');";
    }

    private static string MapAssertStatus(Dictionary<string, string> with)
    {
        var status = with.TryGetValue("status", out var s) ? s : "200";
        return $"// cy.request response status should be {status} — check cy.request alias";
    }

    // -------------------------------------------------------------------------
    // Locator resolution (shared by click, assert-text, assert-visible)
    // -------------------------------------------------------------------------

    private static string ResolveLocator(Dictionary<string, string> with)
    {
        // testId (highest priority for web)
        if (with.TryGetValue("testId", out var testId))
        {
            return $"cy.findByTestId('{EscapeString(testId)}')";
        }

        // role + label
        if (with.TryGetValue("role", out var role))
        {
            if (with.TryGetValue("label", out var label))
            {
                return $"cy.findByRole('{EscapeString(role)}', {{ name: '{EscapeString(label)}' }})";
            }

            return $"cy.findByRole('{EscapeString(role)}')";
        }

        // label alone
        if (with.TryGetValue("label", out var labelOnly))
        {
            return $"cy.findByLabelText('{EscapeString(labelOnly)}')";
        }

        // text
        if (with.TryGetValue("text", out var text))
        {
            return $"cy.contains('{EscapeString(text)}')";
        }

        // cssSelector
        if (with.TryGetValue("cssSelector", out var css))
        {
            return $"cy.get('{EscapeString(css)}')";
        }

        // xpath — requires plugin
        if (with.TryGetValue("xpath", out var xpath))
        {
            return $"cy.xpath('{EscapeString(xpath)}') /* requires cypress-xpath */";
        }

        // automationId — desktop concept, map to [id="..."] with comment
        if (with.TryGetValue("automationId", out var automationId))
        {
            return $"cy.get('[id=\"{EscapeString(automationId)}\"]') /* automationId is a desktop concept — adjust as needed */";
        }

        // name attribute fallback
        if (with.TryGetValue("name", out var name))
        {
            return $"cy.get('[name=\"{EscapeString(name)}\"]')";
        }

        return "cy.get('/* TODO: add selector */')";
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("'", "\\'");
}
