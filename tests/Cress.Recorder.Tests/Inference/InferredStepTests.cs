using Cress.Recorder.Inference;

namespace Cress.Recorder.Tests.Inference;

public sealed class InferredStepTests
{
    [Fact]
    public void ToString_FormatsClickUsingAutomationId()
    {
        var step = new InferredStep
        {
            Kind = StepKind.Click,
            Locator = new Locator { AutomationId = "save-button" },
            SourceTimestamp = DateTime.UtcNow,
        };

        Assert.Equal("Click(automationId=save-button)", step.ToString());
    }

    [Fact]
    public void ToString_FormatsAssertTextUsingNameLocator()
    {
        var step = new InferredStep
        {
            Kind = StepKind.AssertText,
            Locator = new Locator { Name = "Status" },
            Value = "Ready",
            SourceTimestamp = DateTime.UtcNow,
        };

        Assert.Equal("AssertText(name='Status', value='Ready')", step.ToString());
    }

    [Fact]
    public void ToString_FormatsSetValueWithoutUsefulLocator()
    {
        var step = new InferredStep
        {
            Kind = StepKind.SetValue,
            Locator = new Locator(),
            Value = "hello",
            SourceTimestamp = DateTime.UtcNow,
        };

        Assert.Equal("SetValue((locator), value='hello')", step.ToString());
    }

    [Fact]
    public void ToString_FormatsClickWithoutLocator()
    {
        var step = new InferredStep
        {
            Kind = StepKind.Click,
            SourceTimestamp = DateTime.UtcNow,
        };

        Assert.Equal("Click((no locator))", step.ToString());
    }

    [Fact]
    public void ToString_FormatsPressKeyWaitForWindowAndNavigate()
    {
        var pressKey = new InferredStep
        {
            Kind = StepKind.PressKey,
            Key = "Enter",
            SourceTimestamp = DateTime.UtcNow,
        };
        var waitForWindow = new InferredStep
        {
            Kind = StepKind.WaitForWindow,
            WindowTitle = "Calculator",
            SourceTimestamp = DateTime.UtcNow,
        };
        var navigate = new InferredStep
        {
            Kind = StepKind.Navigate,
            NavigateUrl = "https://example.test",
            SourceTimestamp = DateTime.UtcNow,
        };

        Assert.Equal("PressKey(Enter)", pressKey.ToString());
        Assert.Equal("WaitForWindow('Calculator')", waitForWindow.ToString());
        Assert.Equal("Navigate(url='https://example.test')", navigate.ToString());
    }
}
