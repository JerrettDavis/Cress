using Cress.Recorder;
using Cress.Recorder.Inference;
using Cress.Recorder.Serialization;
using Cress.Specs;

namespace Cress.Recorder.Tests.Inference;

/// <summary>
/// Tests for the Web inference domain added in V4.
/// Covers locator priority, fill debounce, ValueChanged→SetValue (never AssertText),
/// Navigate serialization, and round-trip YAML output.
/// </summary>
public class WebDomainInferenceTests
{
    private static readonly StepInferenceEngine Engine = new();
    private static readonly RecordedFlowSerializer Serializer = new();
    private static readonly FlowParser Parser = new();

    private static InferenceOptions WebOptions => new()
    {
        Domain = InferenceDomain.Web,
        TargetProcessId = null,
        IgnoreFocusEvents = true,
        DebounceWindow = TimeSpan.FromMilliseconds(50),
        WebDebounceWindow = TimeSpan.FromMilliseconds(250),
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RecordedEvent MakeWebInvoke(
        ElementInfo element,
        DateTimeOffset? timestamp = null)
        => new()
        {
            Sequence = 1,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Kind = EventKind.Invoke,
            Element = element,
        };

    private static RecordedEvent MakeWebValueChanged(
        ElementInfo element,
        string value,
        DateTimeOffset? timestamp = null)
        => new()
        {
            Sequence = 1,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Kind = EventKind.ValueChanged,
            Value = value,
            Element = element,
        };

    private static RecordedEvent MakeWebNavigate(string url, DateTimeOffset? timestamp = null)
        => new()
        {
            Sequence = 1,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Kind = EventKind.Navigate,
            Element = new ElementInfo(),
            Url = url,
        };

    // ── Locator priority ──────────────────────────────────────────────────────

    [Fact]
    public void Engine_web_domain_maps_invoke_to_click_with_testId_locator()
    {
        var element = new ElementInfo
        {
            TestId = "submit-btn",
            Role = "button",
            Label = "Submit",
            Text = "Submit",
        };

        var steps = Engine.Infer([MakeWebInvoke(element)], WebOptions);

        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.Click, step.Kind);
        Assert.Equal("submit-btn", step.Locator!.TestId);
        // Role and Label should NOT be emitted when testId is available
        Assert.Null(step.Locator.Role);
        Assert.Null(step.Locator.Label);
    }

    [Fact]
    public void Engine_web_domain_prefers_testId_over_role()
    {
        // TestId wins over everything — even when role is present
        var element = new ElementInfo
        {
            TestId = "my-id",
            Role = "link",
            CssSelector = "a.nav-link",
        };

        var steps = Engine.Infer([MakeWebInvoke(element)], WebOptions);

        Assert.Single(steps);
        Assert.Equal("my-id", steps[0].Locator!.TestId);
        Assert.Null(steps[0].Locator.Role);
        Assert.Null(steps[0].Locator.CssSelector);
    }

    [Fact]
    public void Engine_web_domain_prefers_role_plus_label_over_label_alone()
    {
        // When testId is absent, role+label combination wins over label alone
        var element = new ElementInfo
        {
            Role = "button",
            Label = "Save document",
            Text = "Save",
            CssSelector = "button.save",
        };

        var steps = Engine.Infer([MakeWebInvoke(element)], WebOptions);

        Assert.Single(steps);
        var locator = steps[0].Locator!;
        Assert.Equal("button", locator.Role);
        Assert.Equal("Save document", locator.Label);
        // Text and CSS must not bleed in when role+label is chosen
        Assert.Null(locator.Text);
        Assert.Null(locator.CssSelector);
    }

    [Fact]
    public void Engine_web_domain_falls_back_to_cssSelector_when_no_semantic_locator()
    {
        // No testId, no role, no label, no text — fall back to CSS
        var element = new ElementInfo
        {
            CssSelector = "#footer > ul > li:nth-child(2) > a",
        };

        var steps = Engine.Infer([MakeWebInvoke(element)], WebOptions);

        Assert.Single(steps);
        var locator = steps[0].Locator!;
        Assert.Equal("#footer > ul > li:nth-child(2) > a", locator.CssSelector);
        Assert.Null(locator.Role);
        Assert.Null(locator.TestId);
    }

    [Fact]
    public void Engine_web_domain_role_alone_when_no_label()
    {
        // role present, label absent → role alone (not role+label)
        var element = new ElementInfo
        {
            Role = "checkbox",
        };

        var steps = Engine.Infer([MakeWebInvoke(element)], WebOptions);

        Assert.Single(steps);
        var locator = steps[0].Locator!;
        Assert.Equal("checkbox", locator.Role);
        Assert.Null(locator.Label);
    }

    // ── ValueChanged always SetValue in web domain ────────────────────────────

    [Fact]
    public void Engine_web_domain_value_changed_does_not_become_assertion()
    {
        // In web domain, ValueChanged on any element (even if it matches a named target)
        // must ALWAYS produce SetValue, never AssertText.
        var element = new ElementInfo
        {
            CssSelector = "#result-display",
            TestId = "result",
        };

        // Deliberately set AssertionTargetAutomationId — it must be ignored for Web domain
        var options = WebOptions with { AssertionTargetAutomationId = "result" };
        var steps = Engine.Infer([MakeWebValueChanged(element, "42")], options);

        Assert.Single(steps);
        Assert.Equal(StepKind.SetValue, steps[0].Kind);
    }

    // ── Fill debounce (keep last) ─────────────────────────────────────────────

    [Fact]
    public void Engine_web_domain_debounces_consecutive_fill_events_keeping_last_value()
    {
        // Simulate a user typing "hello" into a search box — each keystroke emits
        // a ValueChanged with the accumulating value.
        var t0 = DateTimeOffset.UtcNow;
        var element = new ElementInfo { CssSelector = "#search", TestId = "search-input" };

        var events = new[]
        {
            MakeWebValueChanged(element, "h",     t0),
            MakeWebValueChanged(element, "he",    t0.AddMilliseconds(50)),
            MakeWebValueChanged(element, "hel",   t0.AddMilliseconds(100)),
            MakeWebValueChanged(element, "hell",  t0.AddMilliseconds(150)),
            MakeWebValueChanged(element, "hello", t0.AddMilliseconds(200)),
        };

        var options = WebOptions with { WebDebounceWindow = TimeSpan.FromMilliseconds(250) };
        var steps = Engine.Infer(events, options);

        // Should collapse to a single fill with the final value
        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.SetValue, step.Kind);
        Assert.Equal("hello", step.Value);
    }

    [Fact]
    public void Engine_web_domain_does_not_debounce_value_changes_outside_window()
    {
        // Two fill events separated by more than the WebDebounceWindow should NOT be collapsed
        var t0 = DateTimeOffset.UtcNow;
        var element = new ElementInfo { CssSelector = "#search" };

        var events = new[]
        {
            MakeWebValueChanged(element, "first",  t0),
            MakeWebValueChanged(element, "second", t0.AddMilliseconds(400)), // 400ms > 250ms window
        };

        var options = WebOptions with { WebDebounceWindow = TimeSpan.FromMilliseconds(250) };
        var steps = Engine.Infer(events, options);

        Assert.Equal(2, steps.Count);
        Assert.Equal("first", steps[0].Value);
        Assert.Equal("second", steps[1].Value);
    }

    [Fact]
    public void Engine_web_domain_fill_debounce_does_not_collapse_different_elements()
    {
        // Fills on different elements must not be collapsed even if within the window
        var t0 = DateTimeOffset.UtcNow;
        var email = new ElementInfo { CssSelector = "#email" };
        var password = new ElementInfo { CssSelector = "#password" };

        var events = new[]
        {
            MakeWebValueChanged(email,    "user@example.com", t0),
            MakeWebValueChanged(password, "s3cr3t",           t0.AddMilliseconds(50)),
        };

        var options = WebOptions with { WebDebounceWindow = TimeSpan.FromMilliseconds(250) };
        var steps = Engine.Infer(events, options);

        Assert.Equal(2, steps.Count);
        Assert.Equal("user@example.com", steps[0].Value);
        Assert.Equal("s3cr3t", steps[1].Value);
    }

    // ── Navigate serialization ────────────────────────────────────────────────

    [Fact]
    public void Engine_navigate_serializes_to_browser_navigate_step()
    {
        var step = new InferredStep
        {
            Kind = StepKind.Navigate,
            NavigateUrl = "https://app.example.com/dashboard",
            SourceTimestamp = DateTime.UtcNow,
        };
        var assertStep = new InferredStep
        {
            Kind = StepKind.AssertText,
            Locator = new Locator { TestId = "page-title" },
            Value = "Dashboard",
            SourceTimestamp = DateTime.UtcNow.AddMilliseconds(100),
        };

        var yaml = Serializer.Serialize(
            [step, assertStep],
            new RecordedFlowSerializer.RecordedFlowMetadata
            {
                Id = "nav-test",
                Name = "Navigate Test",
            });

        Assert.Contains("browser.navigate", yaml);
        Assert.Contains("url: https://app.example.com/dashboard", yaml);
    }

    [Fact]
    public void Engine_navigate_serialized_yaml_parses_without_errors()
    {
        var step = new InferredStep
        {
            Kind = StepKind.Navigate,
            NavigateUrl = "https://example.com/login",
            SourceTimestamp = DateTime.UtcNow,
        };
        var assertStep = new InferredStep
        {
            Kind = StepKind.AssertText,
            Locator = new Locator { TestId = "headline" },
            Value = "Welcome",
            SourceTimestamp = DateTime.UtcNow.AddMilliseconds(100),
        };

        var yaml = Serializer.Serialize(
            [step, assertStep],
            new RecordedFlowSerializer.RecordedFlowMetadata
            {
                Id = "nav-parse-test",
                Name = "Navigate Parse Test",
            });

        var result = Parser.Parse(yaml);
        Assert.True(result.Success, $"Parser errors: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");

        var navigateAction = result.Value!.When.First(a => a.Step == "browser.navigate");
        Assert.NotNull(navigateAction);
        Assert.Equal("https://example.com/login", navigateAction.With!["url"]);
    }

    // ── Round-trip: 10-event web stream ──────────────────────────────────────

    [Fact]
    public void Engine_round_trip_web_events_produce_valid_yaml()
    {
        // Synthetic 10-event web stream:
        // navigate, testId click, role+label click, fill (3 keystrokes → 1 step),
        // navigate (SPA), role-only click, cssSelector click, keydown
        var t0 = DateTimeOffset.UtcNow;

        var events = new[]
        {
            // 1. Initial navigation
            MakeWebNavigate("https://app.example.com/login", t0),

            // 2. Click submit button by testId
            MakeWebInvoke(new ElementInfo { TestId = "submit-btn", Role = "button", Label = "Sign in" },
                t0.AddMilliseconds(200)),

            // 3. Click a nav link by role+label
            MakeWebInvoke(new ElementInfo { Role = "link", Label = "Dashboard" },
                t0.AddMilliseconds(500)),

            // 4-6. Typing "hello" in a search input (3 keystrokes, collapses to 1)
            MakeWebValueChanged(new ElementInfo { TestId = "search-input" }, "h",    t0.AddMilliseconds(800)),
            MakeWebValueChanged(new ElementInfo { TestId = "search-input" }, "he",   t0.AddMilliseconds(850)),
            MakeWebValueChanged(new ElementInfo { TestId = "search-input" }, "hello", t0.AddMilliseconds(900)),

            // 7. SPA navigation after search
            MakeWebNavigate("https://app.example.com/results?q=hello", t0.AddSeconds(1)),

            // 8. Click a result item by role alone
            MakeWebInvoke(new ElementInfo { Role = "listitem" }, t0.AddSeconds(1.5)),

            // 9. Click a fallback element by CSS selector
            MakeWebInvoke(new ElementInfo { CssSelector = ".pagination .next" }, t0.AddSeconds(2)),

            // 10. KeyDown (Enter)
            new RecordedEvent
            {
                Sequence = 10,
                Timestamp = t0.AddSeconds(2.5),
                Kind = EventKind.KeyDown,
                Key = "Enter",
                Element = new ElementInfo(),
            },
        };

        var steps = Engine.Infer(events, WebOptions with { WebDebounceWindow = TimeSpan.FromMilliseconds(100) });

        // 10 events, but 3 fill keystrokes collapse to 1 → 8 steps
        Assert.Equal(8, steps.Count);

        // Check ordering
        Assert.Equal(StepKind.Navigate, steps[0].Kind);
        Assert.Equal("https://app.example.com/login", steps[0].NavigateUrl);

        Assert.Equal(StepKind.Click, steps[1].Kind);
        Assert.Equal("submit-btn", steps[1].Locator!.TestId);

        Assert.Equal(StepKind.Click, steps[2].Kind);
        Assert.Equal("link", steps[2].Locator!.Role);
        Assert.Equal("Dashboard", steps[2].Locator!.Label);

        Assert.Equal(StepKind.SetValue, steps[3].Kind);
        Assert.Equal("hello", steps[3].Value); // last value wins

        Assert.Equal(StepKind.Navigate, steps[4].Kind);
        Assert.Contains("results", steps[4].NavigateUrl!);

        Assert.Equal(StepKind.Click, steps[5].Kind);
        Assert.Equal("listitem", steps[5].Locator!.Role);

        Assert.Equal(StepKind.Click, steps[6].Kind);
        Assert.Equal(".pagination .next", steps[6].Locator!.CssSelector);

        Assert.Equal(StepKind.PressKey, steps[7].Kind);
        Assert.Equal("Enter", steps[7].Key);

        // Serialize and parse back
        var meta = new RecordedFlowSerializer.RecordedFlowMetadata
        {
            Id = "web.round-trip",
            Name = "Web Round-Trip Test",
            Summary = "Synthetic 8-step web flow.",
        };

        // Add an assertion step so serializer has a `then` block
        var stepsWithAssertion = new List<InferredStep>(steps)
        {
            new()
            {
                Kind = StepKind.AssertText,
                Locator = new Locator { TestId = "page-title" },
                Value = "Results",
                SourceTimestamp = t0.AddSeconds(3).UtcDateTime,
            }
        };

        var yaml = Serializer.Serialize(stepsWithAssertion, meta);
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, $"Parse errors: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");

        // Verify step count in YAML: 7 actions (navigate + click×4 + fill + press-key) + 2 navigates = 7 when items
        // (AssertText goes to then, so 8 action-steps minus 0 asserts = 8 when steps — but fill is in when)
        // actions = navigate(1) + click(4) + fill(1) + navigate(1) + press-key(1) = 8 when actions
        Assert.Equal(8, result.Value!.When.Count);
        Assert.Single(result.Value!.Then);

        // Verify testId locator survives round-trip
        var submitAction = result.Value!.When.First(a => a.With?.ContainsKey("testId") == true);
        Assert.Equal("submit-btn", submitAction.With!["testId"]);

        // Verify browser.navigate is present
        var navActions = result.Value!.When.Where(a => a.Step == "browser.navigate").ToList();
        Assert.Equal(2, navActions.Count);
    }

    // ── Desktop domain unchanged ──────────────────────────────────────────────

    [Fact]
    public void Engine_desktop_domain_value_changed_can_still_become_assertion()
    {
        // Verify the Domain.Desktop path is not affected by the web changes
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.ValueChanged,
            Value = "Display is 4",
            Element = new ElementInfo
            {
                AutomationId = "CalculatorResults",
                ProcessId = 0,
                RuntimeId = [1, 2],
            }
        };

        var desktopOptions = new InferenceOptions
        {
            Domain = InferenceDomain.Desktop,
            TargetProcessId = null,
            AssertionTargetAutomationId = "CalculatorResults",
        };

        var steps = Engine.Infer([evt], desktopOptions);

        Assert.Single(steps);
        Assert.Equal(StepKind.AssertText, steps[0].Kind);
    }
}
