using Cress.Recorder;

namespace Cress.Recorder.Tests;

public class RecordedEventTests
{
    [Fact]
    public void RecordedEvent_RoundTrips_Through_Constructor()
    {
        var element = new ElementInfo
        {
            Name = "Plus",
            AutomationId = "plusButton",
            ControlType = "Button",
            ClassName = "Button",
            FrameworkId = "XAML",
            ProcessId = 1234,
            RuntimeId = [1, 2, 3]
        };

        var evt = new RecordedEvent
        {
            Sequence = 7,
            Timestamp = new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero),
            Kind = EventKind.Invoke,
            Element = element,
            Value = null,
            Key = null
        };

        Assert.Equal(7, evt.Sequence);
        Assert.Equal(EventKind.Invoke, evt.Kind);
        Assert.Equal("plusButton", evt.Element.AutomationId);
        Assert.Equal("Plus", evt.Element.Name);
        Assert.Equal("Button", evt.Element.ControlType);
        Assert.Equal(1234, evt.Element.ProcessId);
        Assert.Equal([1, 2, 3], evt.Element.RuntimeId);
        Assert.Null(evt.Value);
        Assert.Null(evt.Key);
    }

    [Fact]
    public void ElementInfo_ToString_Prefers_AutomationId()
    {
        var element = new ElementInfo
        {
            Name = "Plus",
            AutomationId = "plusButton",
            ControlType = "Button"
        };

        Assert.Contains("plusButton", element.ToString());
    }

    [Fact]
    public void ElementInfo_ToString_Falls_Back_To_Name_When_NoAutomationId()
    {
        var element = new ElementInfo
        {
            Name = "Plus",
            AutomationId = string.Empty,
            ControlType = "Button"
        };

        var str = element.ToString();
        Assert.Contains("Plus", str);
        Assert.DoesNotContain("plusButton", str);
    }

    [Theory]
    [InlineData(EventKind.Invoke)]
    [InlineData(EventKind.ValueChanged)]
    [InlineData(EventKind.FocusChanged)]
    [InlineData(EventKind.KeyDown)]
    [InlineData(EventKind.WindowOpened)]
    [InlineData(EventKind.Navigate)]
    public void EventKind_AllValues_Constructable(EventKind kind)
    {
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = kind,
            Element = new ElementInfo()
        };

        Assert.Equal(kind, evt.Kind);
    }

    [Fact]
    public void RecordedEvent_ValueChanged_Carries_Value()
    {
        var evt = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.ValueChanged,
            Element = new ElementInfo { AutomationId = "inputBox", ControlType = "Edit" },
            Value = "hello"
        };

        Assert.Equal("hello", evt.Value);
    }

    [Fact]
    public void RecordedEvent_With_Produces_New_Instance()
    {
        var original = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Invoke,
            Element = new ElementInfo()
        };

        var modified = original with { Sequence = 2 };

        Assert.Equal(1, original.Sequence);
        Assert.Equal(2, modified.Sequence);
        Assert.NotSame(original, modified);
    }
}
