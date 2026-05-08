namespace Cress.Recorder.Inference;

/// <summary>
/// Pure, stateless inference engine that converts a sequence of <see cref="RecordedEvent"/>
/// objects into <see cref="InferredStep"/> objects using deterministic rules.
///
/// Design notes:
/// - No desktop-driver dependency — safe to unit-test without launching any real application.
/// - No mutation of input events.
/// - Thread-safe: all methods are pure transformations on value types.
/// - Single engine for both Desktop and Web domains — branching is via
///   <see cref="InferenceOptions.Domain"/>. Desktop path is unchanged from R1-R6.
/// </summary>
public sealed class StepInferenceEngine
{
    /// <summary>
    /// Applies the full inference pipeline to <paramref name="events"/> and returns
    /// the resulting steps in <see cref="RecordedEvent.Timestamp"/> order.
    /// </summary>
    /// <param name="events">Raw events from the recorder. May be in any order.</param>
    /// <param name="options">Filtering, debounce, and assertion-target configuration.</param>
    public IReadOnlyList<InferredStep> Infer(IEnumerable<RecordedEvent> events, InferenceOptions options)
    {
        // Step 1 & 2: filter by process and focus
        var filtered = ApplyFilters(events, options);

        // Step 3: debounce (standard window for all event kinds)
        var debounced = Debounce(filtered, options.DebounceWindow);

        // Step 3b: web fill debounce — coalesce consecutive ValueChanged on same element,
        // keeping only the LAST value. Runs after the standard debounce so the two passes
        // each target different coalescence semantics.
        if (options.Domain == InferenceDomain.Web)
        {
            debounced = DebounceWebFills(debounced, options.WebDebounceWindow);
        }

        // Step 4 & 5: map to steps (drops unknown kinds silently)
        var steps = MapToSteps(debounced, options);

        // Step 6: order by timestamp
        return steps.OrderBy(s => s.SourceTimestamp).ToList();
    }

    // -------------------------------------------------------------------------
    // Pipeline stages
    // -------------------------------------------------------------------------

    private static IEnumerable<RecordedEvent> ApplyFilters(IEnumerable<RecordedEvent> events, InferenceOptions options)
    {
        foreach (var evt in events)
        {
            // Rule 1: drop by process id
            if (options.TargetProcessId.HasValue && evt.Element.ProcessId != options.TargetProcessId.Value)
            {
                continue;
            }

            // Rule 2: drop focus events when configured to do so
            if (options.IgnoreFocusEvents && evt.Kind == EventKind.FocusChanged)
            {
                continue;
            }

            yield return evt;
        }
    }

    private static List<RecordedEvent> Debounce(IEnumerable<RecordedEvent> events, TimeSpan window)
    {
        // Rule 3: coalesce consecutive identical events within the debounce window.
        // Identity = same EventKind + same element RuntimeId + same Value/Key.
        var result = new List<RecordedEvent>();
        RecordedEvent? last = null;

        foreach (var evt in events)
        {
            if (last is not null && IsIdentical(evt, last) && IsWithinWindow(evt, last, window))
            {
                // Collapse: keep the first event (already in result), skip this one.
                continue;
            }

            result.Add(evt);
            last = evt;
        }

        return result;
    }

    /// <summary>
    /// Web-domain fill debounce: for consecutive <see cref="EventKind.ValueChanged"/>
    /// events on the same element within the web debounce window, keep only the LAST
    /// event (as opposed to the standard debounce which keeps the first).
    /// This collapses a character-by-character keystroke stream (h→he→hel→hell→hello)
    /// into a single fill with the final value "hello".
    /// </summary>
    private static List<RecordedEvent> DebounceWebFills(List<RecordedEvent> events, TimeSpan window)
    {
        if (events.Count == 0)
        {
            return events;
        }

        // Sliding window: scan forward, replace a ValueChanged with the latest one
        // in the same run-of-consecutive-fills on the same element.
        var result = new List<RecordedEvent>(events.Count);

        int i = 0;
        while (i < events.Count)
        {
            var evt = events[i];

            // Only apply the fill-specific debounce to ValueChanged
            if (evt.Kind != EventKind.ValueChanged)
            {
                result.Add(evt);
                i++;
                continue;
            }

            // Find the end of this run of consecutive ValueChanged events on the
            // same element within the window, each adjacent pair within the window.
            int runEnd = i;
            for (int j = i + 1; j < events.Count; j++)
            {
                var next = events[j];
                if (next.Kind == EventKind.ValueChanged
                    && IsSameWebElement(next, events[runEnd])
                    && IsWithinWindow(next, events[runEnd], window))
                {
                    runEnd = j;
                }
                else
                {
                    break;
                }
            }

            // Emit only the LAST event in the run (the final typed value)
            result.Add(events[runEnd]);
            i = runEnd + 1;
        }

        return result;
    }

    private static IEnumerable<InferredStep> MapToSteps(IEnumerable<RecordedEvent> events, InferenceOptions options)
    {
        foreach (var evt in events)
        {
            var step = TryMapEvent(evt, options);
            if (step is not null)
            {
                yield return step;
            }
            // Unknown event kinds are silently dropped — no throw, no log entry needed at this layer.
            // R3 can add a diagnostic/warning channel if desired.
        }
    }

    // -------------------------------------------------------------------------
    // Mapping logic (Rule 4 & 5)
    // -------------------------------------------------------------------------

    private static InferredStep? TryMapEvent(RecordedEvent evt, InferenceOptions options)
    {
        return evt.Kind switch
        {
            // Invoke on any element (buttons, menu items, etc.) → Click.
            // The ControlType in the locator preserves the distinction for readability,
            // but the driver operation is the same regardless (invoke pattern first,
            // selection item pattern as fallback).
            EventKind.Invoke => new InferredStep
            {
                Kind = StepKind.Click,
                Locator = BuildLocator(evt.Element, options.Domain),
                SourceTimestamp = evt.Timestamp.UtcDateTime,
            },

            // ValueChanged: domain-specific mapping.
            // Desktop: if the element matches the assertion target → AssertText, else SetValue.
            // Web: always SetValue (fills are user input; assertions come from explicit checks).
            EventKind.ValueChanged => MapValueChanged(evt, options),

            // KeyDown → PressKey.
            EventKind.KeyDown when !string.IsNullOrWhiteSpace(evt.Key) => new InferredStep
            {
                Kind = StepKind.PressKey,
                Key = evt.Key,
                SourceTimestamp = evt.Timestamp.UtcDateTime,
            },

            // KeyDown with no key populated: drop.
            EventKind.KeyDown => null,

            // WindowOpened → WaitForWindow using the element's Name as the window title.
            EventKind.WindowOpened => new InferredStep
            {
                Kind = StepKind.WaitForWindow,
                WindowTitle = evt.Element.Name,
                SourceTimestamp = evt.Timestamp.UtcDateTime,
            },

            // Navigate (web recorder) → Navigate step with the destination URL.
            EventKind.Navigate when !string.IsNullOrWhiteSpace(evt.Url) => new InferredStep
            {
                Kind = StepKind.Navigate,
                NavigateUrl = evt.Url,
                SourceTimestamp = evt.Timestamp.UtcDateTime,
            },

            // Navigate with no URL: drop.
            EventKind.Navigate => null,

            // FocusChanged: should have been filtered already if IgnoreFocusEvents=true;
            // if we reach here (IgnoreFocusEvents=false) we still drop it — focus events
            // carry no actionable step equivalent in the current driver vocabulary.
            EventKind.FocusChanged => null,

            // Anything else is silently dropped.
            _ => null,
        };
    }

    private static InferredStep MapValueChanged(RecordedEvent evt, InferenceOptions options)
    {
        // Web domain: all fills are SetValue — no assertion inference from value changes.
        if (options.Domain == InferenceDomain.Web)
        {
            return new InferredStep
            {
                Kind = StepKind.SetValue,
                Locator = BuildLocator(evt.Element, options.Domain),
                Value = evt.Value,
                SourceTimestamp = evt.Timestamp.UtcDateTime,
            };
        }

        // Desktop domain: determine whether this value change is an assertion candidate
        // (display read-out) or an input action (user typed something).
        var isAssertionTarget =
            options.AssertionTargetAutomationId is not null
            && string.Equals(
                evt.Element.AutomationId,
                options.AssertionTargetAutomationId,
                StringComparison.Ordinal);

        return isAssertionTarget
            ? new InferredStep
            {
                Kind = StepKind.AssertText,
                Locator = BuildLocator(evt.Element, options.Domain),
                Value = evt.Value,
                SourceTimestamp = evt.Timestamp.UtcDateTime,
            }
            : new InferredStep
            {
                Kind = StepKind.SetValue,
                Locator = BuildLocator(evt.Element, options.Domain),
                Value = evt.Value,
                SourceTimestamp = evt.Timestamp.UtcDateTime,
            };
    }

    // -------------------------------------------------------------------------
    // Locator builders
    // -------------------------------------------------------------------------

    private static Locator BuildLocator(ElementInfo element, InferenceDomain domain)
        => domain == InferenceDomain.Web
            ? BuildWebLocator(element)
            : BuildDesktopLocator(element);

    /// <summary>
    /// Desktop locator: prefer AutomationId (stable across runs), fall back to Name.
    /// Always carry ControlType for disambiguation at replay time.
    /// </summary>
    private static Locator BuildDesktopLocator(ElementInfo element)
    {
        return new Locator
        {
            AutomationId = string.IsNullOrWhiteSpace(element.AutomationId) ? null : element.AutomationId,
            Name = string.IsNullOrWhiteSpace(element.AutomationId)
                ? (string.IsNullOrWhiteSpace(element.Name) ? null : element.Name)
                : null, // suppress Name when AutomationId is present to keep locator minimal
            ControlType = string.IsNullOrWhiteSpace(element.ControlType) ? null : element.ControlType,
            ClassName = string.IsNullOrWhiteSpace(element.ClassName) ? null : element.ClassName,
        };
    }

    /// <summary>
    /// Web locator priority order (most stable → least stable):
    ///   testId > role+label > role > label > text > placeholder > cssSelector > xpath > tagName
    ///
    /// Only the single highest-priority field is emitted (plus Role when using role+label combo),
    /// keeping the locator minimal and avoiding over-specification.
    ///
    /// Design decision: we output only ONE primary locator field (not a union) because the
    /// Playwright driver's locate() already implements its own priority chain. Emitting a single
    /// field makes the YAML readable and avoids ambiguous multi-field locators.
    /// </summary>
    private static Locator BuildWebLocator(ElementInfo element)
    {
        // Tier 1: testId — most stable, explicit developer intent
        if (!string.IsNullOrWhiteSpace(element.TestId))
        {
            return new Locator { TestId = element.TestId };
        }

        // Tier 2: role + label — semantically rich, resilient to DOM restructuring
        if (!string.IsNullOrWhiteSpace(element.Role) && !string.IsNullOrWhiteSpace(element.Label))
        {
            return new Locator { Role = element.Role, Label = element.Label };
        }

        // Tier 3: role alone
        if (!string.IsNullOrWhiteSpace(element.Role))
        {
            return new Locator { Role = element.Role };
        }

        // Tier 4: label alone
        if (!string.IsNullOrWhiteSpace(element.Label))
        {
            return new Locator { Label = element.Label };
        }

        // Tier 5: visible text
        if (!string.IsNullOrWhiteSpace(element.Text))
        {
            return new Locator { Text = element.Text };
        }

        // Tier 6: placeholder (inputs without labels)
        // Note: Locator uses Name field to carry placeholder text when no other
        // semantic locator is available. V5+ may add a dedicated Placeholder field.
        if (!string.IsNullOrWhiteSpace(element.Placeholder))
        {
            return new Locator { Text = element.Placeholder };
        }

        // Tier 7: CSS selector (synthesised, fragile but often available)
        if (!string.IsNullOrWhiteSpace(element.CssSelector))
        {
            return new Locator { CssSelector = element.CssSelector };
        }

        // Tier 8: XPath
        if (!string.IsNullOrWhiteSpace(element.XPath))
        {
            return new Locator { XPath = element.XPath };
        }

        // Tier 9: tag name (very fragile, last resort)
        if (!string.IsNullOrWhiteSpace(element.TagName))
        {
            return new Locator { Name = element.TagName };
        }

        // No useful locator data — return empty locator (engine caller will warn)
        return new Locator();
    }

    // -------------------------------------------------------------------------
    // Debounce helpers
    // -------------------------------------------------------------------------

    private static bool IsIdentical(RecordedEvent a, RecordedEvent b)
        => a.Kind == b.Kind
            && a.Element.RuntimeId.SequenceEqual(b.Element.RuntimeId)
            && string.Equals(a.Value, b.Value, StringComparison.Ordinal)
            && string.Equals(a.Key, b.Key, StringComparison.Ordinal);

    /// <summary>
    /// Same-element check for web fill debounce — uses CSS selector as a stable
    /// element identity when RuntimeId is empty (web events don't populate RuntimeId).
    /// Falls back to same-testId, same-label, same-role chains when no CSS selector.
    /// </summary>
    private static bool IsSameWebElement(RecordedEvent a, RecordedEvent b)
    {
        // Use CssSelector as primary identity for web elements
        if (!string.IsNullOrWhiteSpace(a.Element.CssSelector)
            && !string.IsNullOrWhiteSpace(b.Element.CssSelector))
        {
            return string.Equals(a.Element.CssSelector, b.Element.CssSelector, StringComparison.Ordinal);
        }

        // Fall back to testId
        if (!string.IsNullOrWhiteSpace(a.Element.TestId)
            && !string.IsNullOrWhiteSpace(b.Element.TestId))
        {
            return string.Equals(a.Element.TestId, b.Element.TestId, StringComparison.Ordinal);
        }

        // Fall back to runtime id (desktop elements have this populated)
        if (a.Element.RuntimeId.Length > 0 && b.Element.RuntimeId.Length > 0)
        {
            return a.Element.RuntimeId.SequenceEqual(b.Element.RuntimeId);
        }

        return false;
    }

    private static bool IsWithinWindow(RecordedEvent a, RecordedEvent b, TimeSpan window)
    {
        var delta = (a.Timestamp - b.Timestamp).Duration();
        return delta <= window;
    }
}
