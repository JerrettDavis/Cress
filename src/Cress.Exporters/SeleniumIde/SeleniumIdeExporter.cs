using System.Text.Json;
using Cress.Core.Models;

namespace Cress.Exporters.SeleniumIde;

/// <summary>
/// Exports a <see cref="CressFlow"/> as a Selenium IDE v3 <c>.side</c> JSON file.
/// </summary>
/// <remarks>
/// <para>
/// The <c>.side</c> format is a JSON document understood by Selenium IDE 3.x.
/// HTTP steps (http.get, http.post, etc.) are emitted as <c>executeScript</c>
/// comment blocks because Selenium IDE has no native REST step — they require
/// manual wiring to an <c>executeScript</c> / fetch helper.
/// </para>
/// <para>Selenium locator strategy priority:</para>
/// <list type="number">
///   <item>automationId → <c>id=...</c></item>
///   <item>testId → <c>css=[data-testid="..."]</c></item>
///   <item>cssSelector → <c>css=...</c></item>
///   <item>xpath → <c>xpath=...</c></item>
///   <item>name → <c>name=...</c></item>
///   <item>text / role+label → <c>linkText=...</c> or <c>css=...</c></item>
///   <item>label → <c>css=[aria-label="..."]</c></item>
/// </list>
/// </remarks>
public sealed class SeleniumIdeExporter
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Convert a <see cref="NormalizedFlow"/> to a Selenium IDE <c>.side</c> JSON string.
    /// </summary>
    public string Export(NormalizedFlow flow)
    {
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
    /// Convert <paramref name="flow"/> to a Selenium IDE <c>.side</c> JSON string.
    /// </summary>
    public string Export(CressFlow flow)
    {
        var baseUrl = ExtractBaseUrl(flow);
        var testId = NewGuid();
        var suiteId = NewGuid();

        var commands = new List<object>();

        // When actions
        foreach (var action in flow.When)
        {
            var cmds = MapAction(action, baseUrl);
            commands.AddRange(cmds);
        }

        // Then expectations
        foreach (var expectation in flow.Then)
        {
            var cmds = MapExpectation(expectation);
            commands.AddRange(cmds);
        }

        var sideDocument = new
        {
            id = NewGuid(),
            version = "2.0",
            name = flow.Name,
            url = baseUrl,
            tests = new[]
            {
                new
                {
                    id = testId,
                    name = flow.Name,
                    commands
                }
            },
            suites = new[]
            {
                new
                {
                    id = suiteId,
                    name = "Default",
                    persistSession = false,
                    parallel = false,
                    timeout = 300,
                    tests = new[] { testId }
                }
            },
            urls = new[] { baseUrl },
            plugins = Array.Empty<object>()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(sideDocument, options);
    }

    // -------------------------------------------------------------------------
    // Action mapping
    // -------------------------------------------------------------------------

    private static IEnumerable<object> MapAction(FlowAction action, string baseUrl)
    {
        var with = action.With ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return action.Step switch
        {
            "browser.navigate" => [MapNavigate(with, baseUrl)],
            "browser.wait-for-url" => [MapWaitForUrl(with)],
            "ui.click" => [MapClick(with)],
            "ui.fill" => [MapFill(with)],
            "ui.press-key" => [MapPressKey(with)],
            "ui.select" => [MapSelect(with)],
            "ui.check" => [MapCheckUncheck(with, "check")],
            "ui.uncheck" => [MapCheckUncheck(with, "uncheck")],
            "http.get" => [MapHttpComment(action.Step, with)],
            "http.post" => [MapHttpComment(action.Step, with)],
            "http.put" => [MapHttpComment(action.Step, with)],
            "http.delete" => [MapHttpComment(action.Step, with)],
            "http.patch" => [MapHttpComment(action.Step, with)],
            "ui.screenshot" => [MakeCommand("storeScreenshot", "", "screenshot")],
            "unknown" => [MakeComment(with.TryGetValue("comment", out var c) ? c : action.Step)],
            _ => [MakeComment($"TODO: unmapped step '{action.Step}'")]
        };
    }

    private static object MapNavigate(Dictionary<string, string> with, string baseUrl)
    {
        var url = with.TryGetValue("url", out var u) ? u : "/";

        // Selenium IDE 'open' target is a path relative to the base URL, or a full URL
        string target;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Use path portion only if it matches the base URL, otherwise use full URL
            if (!string.IsNullOrEmpty(baseUrl) && url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
            {
                target = url[baseUrl.Length..];
                if (!target.StartsWith("/"))
                {
                    target = "/" + target;
                }
            }
            else
            {
                target = url;
            }
        }
        else
        {
            target = url;
        }

        return MakeCommand("open", target, "");
    }

    private static object MapWaitForUrl(Dictionary<string, string> with)
    {
        var url = with.TryGetValue("url", out var u) ? u : "";
        return MakeCommand("waitForElementPresent", $"css=body[data-url*=\"{url}\"]", "");
    }

    private static object MapClick(Dictionary<string, string> with)
    {
        var target = ResolveTarget(with);
        return MakeCommand("click", target, "");
    }

    private static object MapFill(Dictionary<string, string> with)
    {
        var value = with.TryGetValue("value", out var v) ? v : "";
        var target = ResolveTarget(with);
        return MakeCommand("type", target, value);
    }

    private static object MapPressKey(Dictionary<string, string> with)
    {
        var key = with.TryGetValue("key", out var k) ? k : "";
        // Selenium IDE uses sendKeys with ${KEY_*} notation
        var seleniumKey = MapKey(key);
        return MakeCommand("sendKeys", "css=body", seleniumKey);
    }

    private static object MapSelect(Dictionary<string, string> with)
    {
        var value = with.TryGetValue("value", out var v) ? v : "";
        var target = ResolveTarget(with);
        return MakeCommand("select", target, $"label={value}");
    }

    private static object MapCheckUncheck(Dictionary<string, string> with, string command)
    {
        var target = ResolveTarget(with);
        return MakeCommand(command, target, "");
    }

    private static object MapHttpComment(string step, Dictionary<string, string> with)
    {
        var url = with.TryGetValue("url", out var u) ? u : "";
        var method = step.Split('.')[1].ToUpperInvariant();
        return MakeComment($"HTTP {method} {url} — Selenium IDE has no native REST step; use executeScript with fetch() if needed");
    }

    // -------------------------------------------------------------------------
    // Expectation mapping
    // -------------------------------------------------------------------------

    private static IEnumerable<object> MapExpectation(FlowExpectation expectation)
    {
        var with = expectation.With ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return expectation.Expect switch
        {
            "ui.assert-text" => [MapAssertText(with)],
            "ui.assert-visible" => [MapAssertVisible(with)],
            "browser.assert-url" or "http.assert-url" => [MapAssertUrl(with)],
            "ui.assert-window-title" => [MapAssertTitle(with)],
            "http.assert-status" => [MakeComment($"HTTP status assertion — use storeJson/executeScript to check response status")],
            "http.assert-json" => [MakeComment($"HTTP JSON assertion for path '{(with.TryGetValue("path", out var p) ? p : "")}' — use executeScript")],
            _ => [MakeComment($"TODO: unmapped expectation '{expectation.Expect}'")]
        };
    }

    private static object MapAssertText(Dictionary<string, string> with)
    {
        var expected = with.TryGetValue("expected", out var e) ? e : "";
        var target = ResolveTarget(with);
        return MakeCommand("assertText", target, expected);
    }

    private static object MapAssertVisible(Dictionary<string, string> with)
    {
        var target = ResolveTarget(with);
        return MakeCommand("assertElementPresent", target, "");
    }

    private static object MapAssertUrl(Dictionary<string, string> with)
    {
        var expected = with.TryGetValue("expected", out var e) ? e :
                       with.TryGetValue("url", out var u) ? u : "";
        return MakeCommand("assertLocation", expected, "");
    }

    private static object MapAssertTitle(Dictionary<string, string> with)
    {
        var title = with.TryGetValue("title", out var t) ? t : "";
        return MakeCommand("assertTitle", title, "");
    }

    // -------------------------------------------------------------------------
    // Locator resolution
    // -------------------------------------------------------------------------

    private static string ResolveTarget(Dictionary<string, string> with)
    {
        // automationId → id= (highest precedence, desktop concept but usable on web too)
        if (with.TryGetValue("automationId", out var automationId))
        {
            return $"id={automationId}";
        }

        // testId → css=[data-testid="..."]
        if (with.TryGetValue("testId", out var testId))
        {
            return $"css=[data-testid=\"{testId}\"]";
        }

        // cssSelector → css=...
        if (with.TryGetValue("cssSelector", out var css))
        {
            return $"css={css}";
        }

        // xpath → xpath=...
        if (with.TryGetValue("xpath", out var xpath))
        {
            return $"xpath={xpath}";
        }

        // name → name=...
        if (with.TryGetValue("name", out var name))
        {
            return $"name={name}";
        }

        // label → css=[aria-label="..."]
        if (with.TryGetValue("label", out var label))
        {
            return $"css=[aria-label=\"{label}\"]";
        }

        // text → linkText= (link text) or partial link text
        if (with.TryGetValue("text", out var text))
        {
            return $"linkText={text}";
        }

        // role + label → css=[role="..."][aria-label="..."]
        if (with.TryGetValue("role", out var role))
        {
            if (with.TryGetValue("label", out var roleLabel))
            {
                return $"css=[role=\"{role}\"][aria-label=\"{roleLabel}\"]";
            }

            return $"css=[role=\"{role}\"]";
        }

        // placeholder → css=[placeholder="..."]
        if (with.TryGetValue("placeholder", out var placeholder))
        {
            return $"css=[placeholder=\"{placeholder}\"]";
        }

        return "css=/* TODO: add selector */";
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ExtractBaseUrl(CressFlow flow)
    {
        // Find the first browser.navigate step and use its URL as the base URL
        var firstNavigate = flow.When.FirstOrDefault(a =>
            a.Step == "browser.navigate" &&
            a.With is not null &&
            a.With.ContainsKey("url"));

        if (firstNavigate?.With?.TryGetValue("url", out var url) == true)
        {
            // Strip path to get just the origin
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Authority}";
            }

            return url;
        }

        // Fallback: look for any HTTP step URL
        var firstHttp = flow.When.FirstOrDefault(a =>
            a.Step.StartsWith("http.", StringComparison.OrdinalIgnoreCase) &&
            a.With is not null &&
            a.With.ContainsKey("url"));

        if (firstHttp?.With?.TryGetValue("url", out var httpUrl) == true)
        {
            if (Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Authority}";
            }
        }

        return "http://localhost";
    }

    private static object MakeCommand(string command, string target, string value) =>
        new
        {
            id = NewGuid(),
            command,
            target,
            value
        };

    private static object MakeComment(string text) =>
        new
        {
            id = NewGuid(),
            command = "//",
            target = text,
            value = ""
        };

    private static string MapKey(string key) => key.ToUpperInvariant() switch
    {
        "ENTER" => "${KEY_ENTER}",
        "TAB" => "${KEY_TAB}",
        "ESCAPE" or "ESC" => "${KEY_ESCAPE}",
        "BACKSPACE" => "${KEY_BACKSPACE}",
        "DELETE" => "${KEY_DELETE}",
        "ARROWUP" or "UP" => "${KEY_UP}",
        "ARROWDOWN" or "DOWN" => "${KEY_DOWN}",
        "ARROWLEFT" or "LEFT" => "${KEY_LEFT}",
        "ARROWRIGHT" or "RIGHT" => "${KEY_RIGHT}",
        "HOME" => "${KEY_HOME}",
        "END" => "${KEY_END}",
        "PAGEUP" => "${KEY_PAGE_UP}",
        "PAGEDOWN" => "${KEY_PAGE_DOWN}",
        "F1" => "${KEY_F1}",
        "F2" => "${KEY_F2}",
        "F3" => "${KEY_F3}",
        "F4" => "${KEY_F4}",
        "F5" => "${KEY_F5}",
        "F12" => "${KEY_F12}",
        _ => key
    };

    private static string NewGuid() => Guid.NewGuid().ToString("D");
}
