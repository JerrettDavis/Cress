using Cress.Recorder;
using Cress.Recorder.Inference;

namespace Cress.Recorder.Tests.Inference;

/// <summary>
/// Tests for EventKind.Navigate support in the inference engine and RecordedEvent model.
/// Covers the V3 web recorder addition: Navigate → StepKind.Navigate with a NavigateUrl.
/// </summary>
public class NavigateInferenceTests
{
    private static readonly StepInferenceEngine Engine = new();

    // ── RecordedEvent model ──────────────────────────────────────────────────

    [Fact]
    public void EventKind_Navigate_Is_Defined()
    {
        // Verify the enum value exists so that downstream consumers can switch on it.
        var kind = EventKind.Navigate;
        Assert.Equal(EventKind.Navigate, kind);
    }

    [Fact]
    public void RecordedEvent_Navigate_Carries_Url()
    {
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Navigate,
            Element = new ElementInfo(),
            Url = "https://example.com/dashboard"
        };

        Assert.Equal(EventKind.Navigate, evt.Kind);
        Assert.Equal("https://example.com/dashboard", evt.Url);
        Assert.Null(evt.Value);
        Assert.Null(evt.Key);
    }

    [Fact]
    public void RecordedEvent_NonNavigate_Has_Null_Url_By_Default()
    {
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Invoke,
            Element = new ElementInfo { AutomationId = "btn" }
        };

        // Non-navigate events should have no URL
        Assert.Null(evt.Url);
    }

    // ── Inference engine ─────────────────────────────────────────────────────

    [Fact]
    public void Engine_maps_navigate_event_to_navigate_step()
    {
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Navigate,
            Element = new ElementInfo(),
            Url = "https://app.example.com/login"
        };

        var steps = Engine.Infer([evt], new InferenceOptions { TargetProcessId = null });

        Assert.Single(steps);
        var step = steps[0];
        Assert.Equal(StepKind.Navigate, step.Kind);
        Assert.Equal("https://app.example.com/login", step.NavigateUrl);
        Assert.Null(step.Locator);
    }

    [Fact]
    public void Engine_drops_navigate_event_with_empty_url()
    {
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Navigate,
            Element = new ElementInfo(),
            Url = null
        };

        var steps = Engine.Infer([evt], new InferenceOptions { TargetProcessId = null });

        Assert.Empty(steps);
    }

    [Fact]
    public void Engine_orders_navigate_step_among_other_steps_by_timestamp()
    {
        var t0 = DateTimeOffset.UtcNow;

        var events = new[]
        {
            new RecordedEvent
            {
                Sequence = 1,
                Timestamp = t0,
                Kind = EventKind.Navigate,
                Element = new ElementInfo(),
                Url = "https://example.com/"
            },
            new RecordedEvent
            {
                Sequence = 2,
                Timestamp = t0.AddMilliseconds(500),
                Kind = EventKind.Invoke,
                Element = new ElementInfo
                {
                    AutomationId = "loginBtn",
                    ControlType = "Button",
                    ProcessId = 0,
                    RuntimeId = [1, 2]
                }
            },
            new RecordedEvent
            {
                Sequence = 3,
                Timestamp = t0.AddSeconds(1),
                Kind = EventKind.Navigate,
                Element = new ElementInfo(),
                Url = "https://example.com/dashboard"
            }
        };

        var steps = Engine.Infer(events, new InferenceOptions { TargetProcessId = null });

        Assert.Equal(3, steps.Count);
        Assert.Equal(StepKind.Navigate, steps[0].Kind);
        Assert.Equal("https://example.com/", steps[0].NavigateUrl);
        Assert.Equal(StepKind.Click, steps[1].Kind);
        Assert.Equal(StepKind.Navigate, steps[2].Kind);
        Assert.Equal("https://example.com/dashboard", steps[2].NavigateUrl);
    }

    [Fact]
    public void InferredStep_Navigate_ToString_Includes_Url()
    {
        var step = new InferredStep
        {
            Kind = StepKind.Navigate,
            NavigateUrl = "https://example.com/checkout",
            SourceTimestamp = DateTime.UtcNow
        };

        var str = step.ToString();
        Assert.Contains("Navigate", str);
        Assert.Contains("https://example.com/checkout", str);
    }
}
