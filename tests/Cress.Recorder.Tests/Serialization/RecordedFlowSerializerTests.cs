using Cress.Recorder.Inference;
using Cress.Recorder.Serialization;
using Cress.Specs;

namespace Cress.Recorder.Tests.Serialization;

public sealed class RecordedFlowSerializerTests
{
    private static readonly RecordedFlowSerializer Serializer = new();
    private static readonly FlowParser Parser = new();

    private static RecordedFlowSerializer.RecordedFlowMetadata DefaultMeta(string id = "test-flow", string name = "Test Flow")
        => new()
        {
            Id = id,
            Name = name,
            Capability = "test-capability",
            Summary = "A serializer unit-test flow.",
        };

    // -------------------------------------------------------------------------
    // T1: status=draft is always written
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_AlwaysSetsStatusDraft()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("num2Button"),
            MakeAssertText("CalculatorResults", "Display is 4"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());

        Assert.Contains("status: draft", yaml);
    }

    // -------------------------------------------------------------------------
    // T2: round-trip — FlowParser can parse the output without errors
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_RoundTrip_FlowParserAcceptsOutput()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("num2Button"),
            MakeClick("plusButton"),
            MakeClick("num2Button"),
            MakeClick("equalButton"),
            MakeAssertText("CalculatorResults", "Display is 4"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta("calc.add", "Calculator: 2 + 2"));
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, $"FlowParser errors: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
    }

    // -------------------------------------------------------------------------
    // T3: step count in round-trip output matches actions (non-assert steps)
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_RoundTrip_StepCountMatchesActions()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("num2Button"),
            MakeClick("plusButton"),
            MakeClick("num2Button"),
            MakeClick("equalButton"),
            MakeAssertText("CalculatorResults", "Display is 4"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta("calc.add", "Calculator: 2 + 2"));
        var result = Parser.Parse(yaml);

        // 4 actions (clicks), 1 expectation (assert-text)
        Assert.Equal(4, result.Value!.When.Count);
        Assert.Single(result.Value!.Then);
    }

    // -------------------------------------------------------------------------
    // T4: click step serializes to ui.invoke with automationId
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_ClickStep_UsesUiInvokeWithAutomationId()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("num2Button"),
            MakeAssertText("CalculatorResults", "Display is 4"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());
        var result = Parser.Parse(yaml);

        Assert.True(result.Success);
        var action = result.Value!.When.Single();
        Assert.Equal("ui.invoke", action.Step);
        Assert.NotNull(action.With);
        Assert.Equal("num2Button", action.With["automationId"]);
    }

    // -------------------------------------------------------------------------
    // T5: assert-text step serializes to ui.assert-text expectation with correct inputs
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_AssertTextStep_UsesUiAssertTextWithAutomationIdAndText()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("clearButton"),
            MakeAssertText("CalculatorResults", "Display is 4"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());
        var result = Parser.Parse(yaml);

        Assert.True(result.Success);
        var expectation = result.Value!.Then.Single();
        Assert.Equal("ui.assert-text", expectation.Expect);
        Assert.NotNull(expectation.With);
        Assert.Equal("CalculatorResults", expectation.With["automationId"]);
        Assert.Equal("Display is 4", expectation.With["text"]);
    }

    // -------------------------------------------------------------------------
    // T6: when no assert-text steps, a placeholder expectation is injected
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_NoAssertSteps_InjectsPlaceholderExpectation()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("num2Button"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());
        var result = Parser.Parse(yaml);

        // Flow must still be valid (has at least one `then`)
        Assert.True(result.Success, $"Parser errors: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotEmpty(result.Value!.Then);
        // The placeholder uses ui.assert-window-title
        Assert.Equal("ui.assert-window-title", result.Value!.Then.First().Expect);
    }

    // -------------------------------------------------------------------------
    // T7: SetValue step serializes to ui.fill with value input
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_SetValueStep_UsesUiFillWithValueInput()
    {
        var setValueStep = new InferredStep
        {
            Kind = StepKind.SetValue,
            Locator = new Locator { AutomationId = "searchBox" },
            Value = "hello",
            SourceTimestamp = DateTime.UtcNow,
        };
        var steps = new List<InferredStep>
        {
            setValueStep,
            MakeAssertText("resultLabel", "hello"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());
        var result = Parser.Parse(yaml);

        Assert.True(result.Success);
        var action = result.Value!.When.Single();
        Assert.Equal("ui.fill", action.Step);
        Assert.NotNull(action.With);
        Assert.Equal("searchBox", action.With["automationId"]);
        Assert.Equal("hello", action.With["value"]);
    }

    [Fact]
    public void Serialize_PressKeyStep_UsesUiPressKey()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.PressKey, Key = "Enter", SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("resultLabel", "submitted"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());
        var result = Parser.Parse(yaml);

        Assert.True(result.Success);
        var action = result.Value!.When.Single();
        Assert.Equal("ui.press-key", action.Step);
        Assert.Equal("Enter", action.With!["key"]);
    }

    [Fact]
    public void Serialize_WaitForWindowStep_UsesUiWaitForWindow()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.WaitForWindow, WindowTitle = "Calculator", SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("resultLabel", "ready"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());
        var result = Parser.Parse(yaml);

        Assert.True(result.Success);
        var action = result.Value!.When.Single();
        Assert.Equal("ui.wait-for-window", action.Step);
        Assert.Equal("Calculator", action.With!["title"]);
    }

    [Fact]
    public void Serialize_NavigateStep_UsesBrowserNavigate()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.Navigate, NavigateUrl = "https://example.test/dashboard", SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("resultLabel", "loaded"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());
        var result = Parser.Parse(yaml);

        Assert.True(result.Success);
        var action = result.Value!.When.Single();
        Assert.Equal("browser.navigate", action.Step);
        Assert.Equal("https://example.test/dashboard", action.With!["url"]);
    }

    [Fact]
    public void Serialize_InvalidActionShapes_AreDroppedBeforeWritingFlow()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("keep-me"),
            new() { Kind = StepKind.Navigate, SourceTimestamp = DateTime.UtcNow },
            new() { Kind = StepKind.PressKey, Key = " ", SourceTimestamp = DateTime.UtcNow.AddMilliseconds(1) },
            new() { Kind = StepKind.WaitForWindow, WindowTitle = "", SourceTimestamp = DateTime.UtcNow.AddMilliseconds(2) },
            MakeAssertText("resultLabel", "fallback"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());
        var result = Parser.Parse(yaml);

        Assert.True(result.Success);
        var action = Assert.Single(result.Value!.When);
        Assert.Equal("ui.invoke", action.Step);
        Assert.Equal("keep-me", action.With!["automationId"]);
        Assert.Single(result.Value.Then);
    }

    [Fact]
    public void SaveToFile_CreatesParentDirectoryAndWritesSerializedYaml()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("okButton"),
            MakeAssertText("statusLabel", "Done"),
        };
        var tempRoot = Path.Combine(Path.GetTempPath(), "cress-recorder-serializer", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(tempRoot, "nested", "recorded.flow.yaml");

        try
        {
            Serializer.SaveToFile(steps, DefaultMeta("saved-flow", "Saved Flow"), filePath);

            Assert.True(File.Exists(filePath));
            var yaml = File.ReadAllText(filePath);
            Assert.Contains("id: saved-flow", yaml);
            Assert.Contains("name: Saved Flow", yaml);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    // -------------------------------------------------------------------------
    // T8: metadata fields are written correctly (id, name, capability)
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_MetadataFields_AreWrittenCorrectly()
    {
        var steps = new List<InferredStep>
        {
            MakeClick("okButton"),
            MakeAssertText("statusLabel", "Done"),
        };
        var meta = new RecordedFlowSerializer.RecordedFlowMetadata
        {
            Id = "my-flow-id",
            Name = "My Flow Name",
            Capability = "my-capability",
            Summary = "Flow summary.",
            Tags = ["recorded", "draft", "smoke"],
            Status = "draft",
        };

        var yaml = Serializer.Serialize(steps, meta);
        var result = Parser.Parse(yaml);

        Assert.True(result.Success);
        Assert.Equal("my-flow-id", result.Value!.Id);
        Assert.Equal("My Flow Name", result.Value!.Name);
        Assert.Equal("my-capability", result.Value!.CapabilityId);
        Assert.Equal("draft", result.Value!.Status);
    }

    // -------------------------------------------------------------------------
    // V1 locator fields — per-field serialization tests (T9–T14)
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_LocatorRole_IsEmittedInWithBlock()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.Click, Locator = new Locator { Role = "button" }, SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("result", "ok"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());

        Assert.Contains("role: button", yaml);
    }

    [Fact]
    public void Serialize_LocatorTestId_IsEmittedInWithBlock()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.Click, Locator = new Locator { TestId = "submit-btn" }, SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("result", "ok"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());

        Assert.Contains("testId: submit-btn", yaml);
    }

    [Fact]
    public void Serialize_LocatorLabel_IsEmittedInWithBlock()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.Click, Locator = new Locator { Label = "Submit" }, SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("result", "ok"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());

        Assert.Contains("label: Submit", yaml);
    }

    [Fact]
    public void Serialize_LocatorText_IsEmittedInWithBlock()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.Click, Locator = new Locator { Text = "Click me" }, SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("result", "ok"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());

        Assert.Contains("text: Click me", yaml);
    }

    [Fact]
    public void Serialize_LocatorCssSelector_IsEmittedInWithBlock()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.Click, Locator = new Locator { CssSelector = ".btn-primary" }, SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("result", "ok"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());

        Assert.Contains("cssSelector: .btn-primary", yaml);
    }

    [Fact]
    public void Serialize_LocatorXPath_IsEmittedInWithBlock()
    {
        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.Click, Locator = new Locator { XPath = "//button[@id='ok']" }, SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("result", "ok"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta());

        Assert.Contains("xpath:", yaml);
    }

    [Fact]
    public void Serialize_AllLocatorFields_RoundTrip_PreservesAllKeys()
    {
        // Populate every locator field and verify round-trip through FlowParser.
        var locator = new Locator
        {
            AutomationId = "btn1",
            Name = "OK",
            ControlType = "Button",
            ClassName = "MyButton",
            Role = "button",
            TestId = "ok-btn",
            Label = "OK button",
            Text = "OK",
            CssSelector = "button.ok",
            XPath = "//button[@id='ok']"
        };

        var steps = new List<InferredStep>
        {
            new() { Kind = StepKind.Click, Locator = locator, SourceTimestamp = DateTime.UtcNow },
            MakeAssertText("result", "Confirmed"),
        };

        var yaml = Serializer.Serialize(steps, DefaultMeta("all-locators", "All Locator Fields"));
        var result = Parser.Parse(yaml);

        Assert.True(result.Success, $"FlowParser errors: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");

        var with = result.Value!.When.First().With!;
        Assert.Equal("btn1",          with["automationId"]);
        Assert.Equal("OK",            with["name"]);
        Assert.Equal("Button",        with["controlType"]);
        Assert.Equal("MyButton",      with["className"]);
        Assert.Equal("button",        with["role"]);
        Assert.Equal("ok-btn",        with["testId"]);
        Assert.Equal("OK button",     with["label"]);
        Assert.Equal("OK",            with["text"]);
        Assert.Equal("button.ok",     with["cssSelector"]);
        Assert.True(with.ContainsKey("xpath"), "xpath key must be present");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static InferredStep MakeClick(string automationId) => new()
    {
        Kind = StepKind.Click,
        Locator = new Locator { AutomationId = automationId, ControlType = "Button" },
        SourceTimestamp = DateTime.UtcNow,
    };

    private static InferredStep MakeAssertText(string automationId, string text) => new()
    {
        Kind = StepKind.AssertText,
        Locator = new Locator { AutomationId = automationId },
        Value = text,
        SourceTimestamp = DateTime.UtcNow.AddMilliseconds(10),
    };
}
