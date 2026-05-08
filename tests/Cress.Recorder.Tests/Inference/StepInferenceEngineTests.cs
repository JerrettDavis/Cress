using Cress.Recorder;
using Cress.Recorder.Inference;

namespace Cress.Recorder.Tests.Inference;

/// <summary>
/// Unit tests for <see cref="StepInferenceEngine"/>.
/// No Flawright dependency — all events are constructed from plain C# value types.
/// </summary>
public class StepInferenceEngineTests
{
    private static readonly StepInferenceEngine Engine = new();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static RecordedEvent MakeInvoke(
        string automationId,
        string name = "",
        string controlType = "Button",
        int processId = 100,
        int[] runtimeId = null!,
        DateTimeOffset? timestamp = null)
        => new()
        {
            Sequence = 1,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Kind = EventKind.Invoke,
            Element = new ElementInfo
            {
                AutomationId = automationId,
                Name = name,
                ControlType = controlType,
                ProcessId = processId,
                RuntimeId = runtimeId ?? [1, 2, 3]
            }
        };

    private static RecordedEvent MakeFocus(int processId = 100, DateTimeOffset? timestamp = null)
        => new()
        {
            Sequence = 1,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Kind = EventKind.FocusChanged,
            Element = new ElementInfo { ProcessId = processId, RuntimeId = [9, 9] }
        };

    private static RecordedEvent MakeValueChanged(
        string automationId,
        string value,
        int processId = 100,
        int[] runtimeId = null!,
        DateTimeOffset? timestamp = null)
        => new()
        {
            Sequence = 1,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Kind = EventKind.ValueChanged,
            Value = value,
            Element = new ElementInfo
            {
                AutomationId = automationId,
                ControlType = "Text",
                ProcessId = processId,
                RuntimeId = runtimeId ?? [5, 5, 5]
            }
        };

    private static RecordedEvent MakeKeyDown(
        string key,
        int processId = 100,
        DateTimeOffset? timestamp = null)
        => new()
        {
            Sequence = 1,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Kind = EventKind.KeyDown,
            Key = key,
            Element = new ElementInfo { ProcessId = processId, RuntimeId = [7, 7] }
        };

    private static RecordedEvent MakeWindowOpened(
        string windowTitle,
        int processId = 100,
        DateTimeOffset? timestamp = null)
        => new()
        {
            Sequence = 1,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Kind = EventKind.WindowOpened,
            Element = new ElementInfo
            {
                Name = windowTitle,
                ControlType = "Window",
                ProcessId = processId,
                RuntimeId = [3, 3]
            }
        };

    private static InferenceOptions DefaultOptions(int? pid = 100)
        => new() { TargetProcessId = pid };

    // -------------------------------------------------------------------------
    // Filter: process id
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_drops_events_from_other_process_ids()
    {
        var events = new[]
        {
            MakeInvoke("btn1", processId: 100),
            MakeInvoke("btn2", processId: 999), // wrong pid
        };

        var steps = Engine.Infer(events, new InferenceOptions { TargetProcessId = 100 });

        Assert.Single(steps);
        Assert.Equal("btn1", steps[0].Locator!.AutomationId);
    }

    [Fact]
    public void Engine_keeps_all_events_when_no_target_process_id_set()
    {
        var t0 = DateTimeOffset.UtcNow;
        var events = new[]
        {
            MakeInvoke("btn1", processId: 100, runtimeId: [1, 1], timestamp: t0),
            MakeInvoke("btn2", processId: 999, runtimeId: [2, 2], timestamp: t0.AddMilliseconds(200)),
        };

        var steps = Engine.Infer(events, new InferenceOptions { TargetProcessId = null });

        Assert.Equal(2, steps.Count);
    }

    // -------------------------------------------------------------------------
    // Filter: focus events
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_drops_focus_events_when_IgnoreFocusEvents_true()
    {
        var events = new[]
        {
            MakeFocus(),
            MakeInvoke("btn1"),
            MakeFocus(),
        };

        var steps = Engine.Infer(events, new InferenceOptions { TargetProcessId = 100, IgnoreFocusEvents = true });

        Assert.Single(steps);
        Assert.Equal(StepKind.Click, steps[0].Kind);
    }

    [Fact]
    public void Engine_keeps_focus_events_when_IgnoreFocusEvents_false()
    {
        var events = new[]
        {
            MakeFocus(),
            MakeInvoke("btn1"),
        };

        // FocusChanged has no mapping → dropped at map stage, not at filter stage.
        // With IgnoreFocusEvents=false the filter lets it through, but the mapper
        // drops it (no FocusChanged → step equivalent in the current driver vocab).
        // The net result is: 1 Click step only.
        var steps = Engine.Infer(events, new InferenceOptions { TargetProcessId = 100, IgnoreFocusEvents = false });

        Assert.Single(steps);
        Assert.Equal(StepKind.Click, steps[0].Kind);
    }

    // -------------------------------------------------------------------------
    // Debounce
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_debounces_identical_consecutive_events_within_window()
    {
        var t0 = DateTimeOffset.UtcNow;
        var events = new[]
        {
            MakeInvoke("btn1", runtimeId: [1, 2], timestamp: t0),
            MakeInvoke("btn1", runtimeId: [1, 2], timestamp: t0.AddMilliseconds(10)), // within 50ms window
        };

        var steps = Engine.Infer(events, new InferenceOptions
        {
            TargetProcessId = 100,
            DebounceWindow = TimeSpan.FromMilliseconds(50)
        });

        // Should collapse to one click
        Assert.Single(steps);
    }

    [Fact]
    public void Engine_does_not_debounce_events_outside_window()
    {
        var t0 = DateTimeOffset.UtcNow;
        var events = new[]
        {
            MakeInvoke("btn1", runtimeId: [1, 2], timestamp: t0),
            MakeInvoke("btn1", runtimeId: [1, 2], timestamp: t0.AddMilliseconds(100)), // outside 50ms window
        };

        var steps = Engine.Infer(events, new InferenceOptions
        {
            TargetProcessId = 100,
            DebounceWindow = TimeSpan.FromMilliseconds(50)
        });

        Assert.Equal(2, steps.Count);
    }

    [Fact]
    public void Engine_does_not_debounce_different_elements()
    {
        var t0 = DateTimeOffset.UtcNow;
        var events = new[]
        {
            MakeInvoke("btn1", runtimeId: [1, 1], timestamp: t0),
            MakeInvoke("btn2", runtimeId: [2, 2], timestamp: t0.AddMilliseconds(5)), // different runtimeId
        };

        var steps = Engine.Infer(events, new InferenceOptions
        {
            TargetProcessId = 100,
            DebounceWindow = TimeSpan.FromMilliseconds(50)
        });

        Assert.Equal(2, steps.Count);
    }

    // -------------------------------------------------------------------------
    // Map: Invoke
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_maps_invoke_to_click_with_automationId_locator()
    {
        var events = new[]
        {
            MakeInvoke("plusButton", name: "Plus", controlType: "Button")
        };

        var steps = Engine.Infer(events, DefaultOptions());

        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.Click, step.Kind);
        Assert.Equal("plusButton", step.Locator!.AutomationId);
        Assert.Equal("Button", step.Locator.ControlType);
        // When AutomationId is set, Name should be suppressed (more stable locator)
        Assert.Null(step.Locator.Name);
    }

    [Fact]
    public void Engine_maps_invoke_to_click_with_name_locator_when_no_automationId()
    {
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Invoke,
            Element = new ElementInfo
            {
                AutomationId = string.Empty, // no automationId
                Name = "File",
                ControlType = "MenuItem",
                ProcessId = 100,
                RuntimeId = [1]
            }
        };

        var steps = Engine.Infer([evt], DefaultOptions());

        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.Click, step.Kind);
        Assert.Null(step.Locator!.AutomationId);
        Assert.Equal("File", step.Locator.Name);
        Assert.Equal("MenuItem", step.Locator.ControlType);
    }

    // -------------------------------------------------------------------------
    // Map: ValueChanged / AssertText
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_maps_property_changed_on_assertion_target_to_assert_text()
    {
        var events = new[]
        {
            MakeValueChanged("CalculatorResults", "Display is 4")
        };

        var steps = Engine.Infer(events, new InferenceOptions
        {
            TargetProcessId = 100,
            AssertionTargetAutomationId = "CalculatorResults"
        });

        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.AssertText, step.Kind);
        Assert.Equal("CalculatorResults", step.Locator!.AutomationId);
        Assert.Equal("Display is 4", step.Value);
    }

    [Fact]
    public void Engine_ignores_property_changed_on_non_assertion_target()
    {
        var events = new[]
        {
            MakeValueChanged("someOtherElement", "hello")
        };

        var steps = Engine.Infer(events, new InferenceOptions
        {
            TargetProcessId = 100,
            AssertionTargetAutomationId = "CalculatorResults" // different id
        });

        // Should produce SetValue, not AssertText
        Assert.Single(steps);
        Assert.Equal(StepKind.SetValue, steps[0].Kind);
    }

    [Fact]
    public void Engine_maps_value_changed_to_set_value_when_no_assertion_target_configured()
    {
        var events = new[]
        {
            MakeValueChanged("inputBox", "hello world")
        };

        // No AssertionTargetAutomationId → always SetValue
        var steps = Engine.Infer(events, DefaultOptions());

        Assert.Single(steps);
        Assert.Equal(StepKind.SetValue, steps[0].Kind);
        Assert.Equal("hello world", steps[0].Value);
    }

    // -------------------------------------------------------------------------
    // Map: KeyDown
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_maps_keydown_to_press_key()
    {
        var events = new[]
        {
            MakeKeyDown("Return")
        };

        var steps = Engine.Infer(events, DefaultOptions());

        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.PressKey, step.Kind);
        Assert.Equal("Return", step.Key);
        Assert.Null(step.Locator); // PressKey has no element locator
    }

    // -------------------------------------------------------------------------
    // Map: WindowOpened
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_maps_window_opened_to_wait_for_window()
    {
        var events = new[]
        {
            MakeWindowOpened("Calculator")
        };

        var steps = Engine.Infer(events, DefaultOptions());

        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.WaitForWindow, step.Kind);
        Assert.Equal("Calculator", step.WindowTitle);
    }

    // -------------------------------------------------------------------------
    // Map: unknown / unhandled event kinds
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_drops_unknown_event_kinds_gracefully()
    {
        // FocusChanged with IgnoreFocusEvents=false reaches the mapper and is dropped there.
        // Any unmapped event kind should produce zero steps without throwing.
        var events = new[]
        {
            MakeFocus(), // dropped by filter (default IgnoreFocusEvents=true)
            MakeInvoke("btn1"),
        };

        var steps = Engine.Infer(events, new InferenceOptions
        {
            TargetProcessId = 100,
            IgnoreFocusEvents = false // let focus through to the mapper
        });

        // Focus is dropped in mapper; only the click survives
        Assert.Single(steps);
    }

    [Fact]
    public void Engine_does_not_throw_on_empty_input()
    {
        var steps = Engine.Infer([], DefaultOptions());
        Assert.Empty(steps);
    }

    // -------------------------------------------------------------------------
    // Ordering
    // -------------------------------------------------------------------------

    [Fact]
    public void Engine_orders_steps_by_timestamp()
    {
        var t0 = DateTimeOffset.UtcNow;
        // Provide events out of order
        var events = new[]
        {
            MakeInvoke("btn3", runtimeId: [3], timestamp: t0.AddSeconds(2)),
            MakeInvoke("btn1", runtimeId: [1], timestamp: t0.AddSeconds(0)),
            MakeInvoke("btn2", runtimeId: [2], timestamp: t0.AddSeconds(1)),
        };

        var steps = Engine.Infer(events, DefaultOptions());

        Assert.Equal(3, steps.Count);
        Assert.Equal("btn1", steps[0].Locator!.AutomationId);
        Assert.Equal("btn2", steps[1].Locator!.AutomationId);
        Assert.Equal("btn3", steps[2].Locator!.AutomationId);
    }

    // -------------------------------------------------------------------------
    // Calculator canary — realistic scenario
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reproduces the exact 5-event sequence captured in the R1 PoC against
    /// the real Windows Calculator (2 + 2 = workflow, no assertion target configured).
    /// Expected: 5 Click steps in order.
    /// </summary>
    [Fact]
    public void Calculator_canary_5_clicks()
    {
        var t0 = DateTimeOffset.UtcNow;
        var events = new[]
        {
            BuildCalcButton("clearButton", "Clear",  t0.AddMilliseconds(0)),
            BuildCalcButton("num2Button",  "Two",    t0.AddMilliseconds(200)),
            BuildCalcButton("plusButton",  "Plus",   t0.AddMilliseconds(400)),
            BuildCalcButton("num2Button",  "Two",    t0.AddMilliseconds(600)),
            BuildCalcButton("equalButton", "Equals", t0.AddMilliseconds(800)),
        };

        var steps = Engine.Infer(events, new InferenceOptions
        {
            TargetProcessId = 42,
            DebounceWindow = TimeSpan.FromMilliseconds(50),
            IgnoreFocusEvents = true,
            AssertionTargetAutomationId = null // no assertion target — just actions
        });

        Assert.Equal(5, steps.Count);
        Assert.All(steps, s => Assert.Equal(StepKind.Click, s.Kind));

        Assert.Equal("clearButton", steps[0].Locator!.AutomationId);
        Assert.Equal("Button",      steps[0].Locator!.ControlType);

        Assert.Equal("num2Button",  steps[1].Locator!.AutomationId);
        Assert.Equal("plusButton",  steps[2].Locator!.AutomationId);
        Assert.Equal("num2Button",  steps[3].Locator!.AutomationId);
        Assert.Equal("equalButton", steps[4].Locator!.AutomationId);
    }

    /// <summary>
    /// Same 5 Calculator button events, plus a <see cref="EventKind.ValueChanged"/>
    /// on <c>CalculatorResults</c> with value "Display is 4".
    /// Expected: 5 Click steps + 1 AssertText step = 6 steps total.
    /// </summary>
    [Fact]
    public void Calculator_canary_with_display_assertion()
    {
        var t0 = DateTimeOffset.UtcNow;
        var events = new[]
        {
            BuildCalcButton("clearButton", "Clear",  t0.AddMilliseconds(0)),
            BuildCalcButton("num2Button",  "Two",    t0.AddMilliseconds(200)),
            BuildCalcButton("plusButton",  "Plus",   t0.AddMilliseconds(400)),
            BuildCalcButton("num2Button",  "Two",    t0.AddMilliseconds(600)),
            BuildCalcButton("equalButton", "Equals", t0.AddMilliseconds(800)),
            // The display updates shortly after = is pressed
            new RecordedEvent
            {
                Sequence = 6,
                Timestamp = t0.AddMilliseconds(900),
                Kind = EventKind.ValueChanged,
                Value = "Display is 4",
                Element = new ElementInfo
                {
                    AutomationId = "CalculatorResults",
                    ControlType = "Text",
                    ProcessId = 42,
                    RuntimeId = [8, 8, 8]
                }
            }
        };

        var steps = Engine.Infer(events, new InferenceOptions
        {
            TargetProcessId = 42,
            DebounceWindow = TimeSpan.FromMilliseconds(50),
            IgnoreFocusEvents = true,
            AssertionTargetAutomationId = "CalculatorResults"
        });

        Assert.Equal(6, steps.Count);

        // First 5 are clicks (in order)
        Assert.All(steps.Take(5), s => Assert.Equal(StepKind.Click, s.Kind));

        // 6th step is the display assertion
        var assertStep = steps[5];
        Assert.Equal(StepKind.AssertText, assertStep.Kind);
        Assert.Equal("CalculatorResults", assertStep.Locator!.AutomationId);
        Assert.Equal("Display is 4", assertStep.Value);
    }

    // -------------------------------------------------------------------------
    // Gap A — Name-property-based assertion capture (R6)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the UiaEventDispatcher emits a ValueChanged event whose Value was sourced from
    /// the element's Name property (Calculator display scenario), the inference engine must
    /// still produce an AssertText step — the engine does not distinguish how the dispatcher
    /// populated the Value field.
    /// </summary>
    [Fact]
    public void Engine_produces_assert_text_from_name_property_value_changed_event()
    {
        // Simulate what UiaEventDispatcher now emits for a Name-property change on the
        // Calculator display element: Kind=ValueChanged, Value = new Name string.
        var displayEvent = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.ValueChanged,
            Value = "Display is 4",   // sourced from element Name, not ValuePattern.Value
            Element = new ElementInfo
            {
                AutomationId = "CalculatorResults",
                Name = "Display is 4",
                ControlType = "Text",
                ProcessId = 42,
                RuntimeId = [8, 8, 8]
            }
        };

        var steps = Engine.Infer([displayEvent], new InferenceOptions
        {
            TargetProcessId = 42,
            IgnoreFocusEvents = true,
            AssertionTargetAutomationId = "CalculatorResults"
        });

        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.AssertText, step.Kind);
        Assert.Equal("CalculatorResults", step.Locator!.AutomationId);
        Assert.Equal("Display is 4", step.Value);
    }

    [Fact]
    public void Engine_produces_set_value_when_non_assertion_element_fires_name_change()
    {
        // A Name change on a non-assertion element should infer as SetValue, not AssertText.
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.ValueChanged,
            Value = "some text",
            Element = new ElementInfo
            {
                AutomationId = "inputField",
                Name = "some text",
                ControlType = "Edit",
                ProcessId = 42,
                RuntimeId = [7, 7, 7]
            }
        };

        var steps = Engine.Infer([evt], new InferenceOptions
        {
            TargetProcessId = 42,
            IgnoreFocusEvents = true,
            AssertionTargetAutomationId = "CalculatorResults"  // different element
        });

        Assert.Single(steps);
        Assert.Equal(StepKind.SetValue, steps[0].Kind);
    }

    // -------------------------------------------------------------------------
    // Private factory — Calculator button event
    // -------------------------------------------------------------------------

    private static RecordedEvent BuildCalcButton(
        string automationId,
        string name,
        DateTimeOffset timestamp)
        => new()
        {
            Sequence = 0,
            Timestamp = timestamp,
            Kind = EventKind.Invoke,
            Element = new ElementInfo
            {
                AutomationId = automationId,
                Name = name,
                ControlType = "Button",
                ClassName = "Button",
                FrameworkId = "XAML",
                ProcessId = 42,
                RuntimeId = [automationId.GetHashCode(), 1]
            }
        };
}
