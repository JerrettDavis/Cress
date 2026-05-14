using System.Reflection;
using System.Diagnostics;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Execution.Drivers;
using Flawright;

namespace Cress.UnitTests;

/// <summary>
/// Unit tests for the Flawright desktop driver locator wiring.
///
/// Priority order under test:
///   1. automationId — direct UIA AutomationId (highest precision)
///   2. testId       — first-class alias for automationId
///   3. name + controlType — AND-combined UIA match
///   4. name alone
///   5. label        — LabeledBy relation / Name fallback
///   6. role alone   — wide ControlType match
///   7. text         — visible content fallback
///   8. cssSelector / xpath — web-only; diagnostic error when no desktop locator present
///
/// Tests that require a real window are limited to the error / diagnostic surface
/// (CheckWebOnlyLocators) which fires before any window lookup. Full element-find
/// integration is covered by FlawrightRuntimeDriverTests.RuntimeOrchestrator_RunsRealFlawrightFlow.
/// </summary>
public sealed class FlawrightLocatorWiringTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<IDriverSession> CreateSessionAsync(
        string tempRoot,
        EffectiveConfig? config = null,
        FlawrightRuntimeDriver? driver = null)
    {
        driver ??= new FlawrightRuntimeDriver();
        var store = new EvidenceStore(tempRoot);
        var context = new DriverSessionStartContext
        {
            ProjectRoot = tempRoot,
            FlowId = "locator-wiring-test",
            ArtifactRoot = tempRoot,
            EvidenceStore = store,
            EffectiveConfig = config ?? new EffectiveConfig()
        };
        return await driver.StartSessionAsync(context, CancellationToken.None);
    }

    private static PlanAction MakeAction(string operation, Dictionary<string, string> inputs) => new()
    {
        Name = operation,
        Operation = operation,
        Kind = "action",
        Driver = "flawright",
        Inputs = inputs
    };

    private static FlowExecutionContext MakeFlowContext() => new()
    {
        FlowId = "locator-wiring-test",
        FlowName = "Locator Wiring Test",
        ArtifactRoot = string.Empty
    };

    // -------------------------------------------------------------------------
    // V2-1: cssSelector alone → diagnostic error (web-only)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Click_WithCssSelectorOnly_ReturnsDiagnosticError()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string>
            {
                ["cssSelector"] = ".btn-primary"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("locator-strategy-not-supported", result.FailureClassification);
        Assert.Contains("cssSelector", result.Message);
        Assert.Contains("not supported by the desktop driver", result.Message);
        Assert.Contains("automationId", result.Message);
    }

    // -------------------------------------------------------------------------
    // V2-2: xpath alone → diagnostic error (web-only)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Click_WithXPathOnly_ReturnsDiagnosticError()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string>
            {
                ["xpath"] = "//button[@id='ok']"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("locator-strategy-not-supported", result.FailureClassification);
        Assert.Contains("xpath", result.Message);
        Assert.Contains("not supported by the desktop driver", result.Message);
    }

    // -------------------------------------------------------------------------
    // V2-3: automationId + cssSelector → uses automationId silently (no error)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Click_WithAutomationIdAndCssSelector_SkipsCssDiagnostic()
    {
        // cssSelector is present but automationId is also present — the desktop driver should NOT
        // return the web-only error. It will fail with locator-not-found (no window),
        // but that is a different failure, proving the web-only gate was passed.
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string>
            {
                ["automationId"] = "myButton",
                ["cssSelector"] = ".btn-primary"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        // Must NOT be the web-only diagnostic; should be locator-not-found or window-not-found
        Assert.NotEqual("locator-strategy-not-supported", result.FailureClassification);
    }

    // -------------------------------------------------------------------------
    // V2-4: testId + cssSelector → testId takes priority, web-only gate passed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Click_WithTestIdAndCssSelector_SkipsCssDiagnostic()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string>
            {
                ["testId"] = "submit-btn",
                ["cssSelector"] = ".btn-submit"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        // Must NOT be the web-only diagnostic — testId is a valid desktop locator
        Assert.NotEqual("locator-strategy-not-supported", result.FailureClassification);
    }

    // -------------------------------------------------------------------------
    // V2-5: role alone → passes web-only gate (not a web-only locator)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Click_WithRoleOnly_PassesWebOnlyGate()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string>
            {
                ["role"] = "button"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        // Should fail because no window is open — NOT because of a web-only locator error
        Assert.NotEqual("locator-strategy-not-supported", result.FailureClassification);
        Assert.NotEqual("unsupported-flawright-operation", result.FailureClassification);
    }

    // -------------------------------------------------------------------------
    // V2-6: label alone → passes web-only gate (desktop-compatible locator)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Fill_WithLabelOnly_PassesWebOnlyGate()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("fill", new Dictionary<string, string>
            {
                ["label"] = "Email address",
                ["value"] = "test@example.com"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        // Should fail because no window is open — NOT a web-only locator error
        Assert.NotEqual("locator-strategy-not-supported", result.FailureClassification);
    }

    // -------------------------------------------------------------------------
    // V2-7: label takes priority over text — when both present, label is checked
    //        first (label is higher priority in the fallback chain)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Fill_WithLabelAndText_LabelTakesPriority_NoDiagnosticError()
    {
        // Both label and text are present; the driver should not error on locator strategy.
        // It should attempt the label path first (priority 5) before text (priority 6).
        // Since there's no window, both would fail with locator-not-found anyway, but the
        // point is that neither causes a locator-strategy-not-supported error.
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("fill", new Dictionary<string, string>
            {
                ["label"] = "Email",
                ["text"] = "Email",
                ["value"] = "user@example.com"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.NotEqual("locator-strategy-not-supported", result.FailureClassification);
        Assert.NotEqual("invalid-flawright-input", result.FailureClassification);
    }

    // -------------------------------------------------------------------------
    // V2-8: cssSelector with text fallback present → no web-only error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AssertText_WithCssSelectorAndTextFallback_SkipsDiagnostic()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("assert-text", new Dictionary<string, string>
            {
                ["cssSelector"] = "#result-label",
                ["text"] = "Display is 4",
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        // text is a desktop-compatible locator — should skip the web-only gate
        Assert.NotEqual("locator-strategy-not-supported", result.FailureClassification);
    }

    // -------------------------------------------------------------------------
    // V2-9: error message format — must contain the exact locator strategy name
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Click_WebOnlyErrorMessage_ContainsExpectedFields()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var cssResult = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string> { ["cssSelector"] = ".foo" }),
            MakeFlowContext(),
            CancellationToken.None);

        var xpathResult = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string> { ["xpath"] = "//div" }),
            MakeFlowContext(),
            CancellationToken.None);

        // Both must identify the offending strategy key in the error message
        Assert.Contains("cssSelector", cssResult.Message);
        Assert.Contains("xpath", xpathResult.Message);

        // Both must suggest valid desktop alternatives
        Assert.Contains("testId", cssResult.Message);
        Assert.Contains("testId", xpathResult.Message);
    }

    // -------------------------------------------------------------------------
    // V2-10: HasLocator returns true for all V2 locator fields
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AssertText_WithEachLocatorField_RecognisedAsHavingLocator()
    {
        // HasLocator is exercised indirectly through AssertText — when HasLocator returns
        // true, FindElement is called (and will fail with locator-not-found or window-not-found
        // because there's no running app). When HasLocator returns false, a window-wide text
        // search is used instead. Either way, assertion-specific errors (not strategy errors)
        // confirm the field was recognised.
        var locators = new[]
        {
            new Dictionary<string, string> { ["selector"] = "#x", ["text"] = "val" },
            new Dictionary<string, string> { ["automationId"] = "x", ["text"] = "val" },
            new Dictionary<string, string> { ["testId"] = "x", ["text"] = "val" },
            new Dictionary<string, string> { ["name"] = "x", ["text"] = "val" },
            new Dictionary<string, string> { ["controlType"] = "button", ["text"] = "val" },
            new Dictionary<string, string> { ["role"] = "button", ["text"] = "val" },
            new Dictionary<string, string> { ["label"] = "x", ["text"] = "val" },
        };

        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        foreach (var inputs in locators)
        {
            var result = await session.ExecuteAsync(
                MakeAction("assert-text", inputs),
                MakeFlowContext(),
                CancellationToken.None);

            Assert.Equal(RunOutcome.Failed, result.Outcome);
            Assert.NotEqual("locator-strategy-not-supported", result.FailureClassification);
            Assert.NotEqual("invalid-assertion", result.FailureClassification);
        }
    }

    [Fact]
    public async Task Click_WithInvalidSelector_ReturnsSelectorDiagnostic()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string>
            {
                ["selector"] = "css:.btn-primary"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("locator-not-found", result.FailureClassification);
    }

    [Fact]
    public async Task UnsupportedOperation_ReturnsUnsupportedFailure()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("dance", []),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("unsupported-flawright-operation", result.FailureClassification);
        Assert.Contains("dance", result.Message);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Open_WithoutApplicationPath_ReturnsMissingPathFailure()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("open", []),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("application-path-missing", result.FailureClassification);
        Assert.Contains("application path", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Open_WithMissingExecutable_ReturnsApplicationNotFound()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("open", new Dictionary<string, string>
            {
                ["application"] = Path.Combine(tempDir.Path, "missing-app.exe")
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("application-not-found", result.FailureClassification);
        Assert.Contains("missing-app.exe", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Attach_WithoutProcessIdentifier_ReturnsAttachFailure()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("attach", []),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("application-attach-failed", result.FailureClassification);
    }

    [Fact]
    public async Task Fill_WithoutValue_ReturnsInvalidInput()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("fill", new Dictionary<string, string>
            {
                ["selector"] = "#name"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("invalid-flawright-input", result.FailureClassification);
        Assert.Contains("value", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssertText_WithoutExpectedValue_ReturnsInvalidAssertion()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("assert-text", new Dictionary<string, string>
            {
                ["selector"] = "#status"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("invalid-assertion", result.FailureClassification);
    }

    [Fact]
    public async Task AssertText_WindowAssertionWithoutPage_ReturnsWindowNotFound()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("assert-text", new Dictionary<string, string>
            {
                ["window"] = "true",
                ["expected"] = "Cress"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("window-not-found", result.FailureClassification);
    }

    [Fact]
    public async Task AssertWindowTitle_WithoutTitle_ReturnsInvalidAssertion()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("assert-window-title", []),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("invalid-assertion", result.FailureClassification);
    }

    [Fact]
    public async Task AssertWindowTitle_WithoutPage_ReturnsWindowNotFound()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("assert-window-title", new Dictionary<string, string>
            {
                ["title"] = "Cress"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("window-not-found", result.FailureClassification);
    }

    [Fact]
    public async Task PressKey_WithoutKey_ReturnsInvalidInput()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("press-key", []),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("invalid-flawright-input", result.FailureClassification);
    }

    [Fact]
    public async Task PressKey_WithoutWindow_ReturnsWindowNotFound()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("press-key", new Dictionary<string, string>
            {
                ["key"] = "Enter"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("window-not-found", result.FailureClassification);
    }

    [Fact]
    public async Task Screenshot_WithoutWindow_ReturnsWindowNotFound()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("screenshot", []),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("window-not-found", result.FailureClassification);
    }

    [Fact]
    public async Task Close_WithoutRunningApplication_ReturnsAlreadyClosed()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var result = await session.ExecuteAsync(
            MakeAction("close", []),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Application is already closed.", result.Message);
    }

    [Fact]
    public async Task Session_MetadataIsSeeded_AndFinalEvidenceIsEmptyWithoutPage()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var artifacts = await session.CaptureFinalEvidenceAsync(MakeFlowContext(), CancellationToken.None);

        Assert.Equal("built-in", session.Metadata["kind"]);
        Assert.Equal("flawright", session.Metadata["framework"]);
        Assert.True(session.Metadata.ContainsKey("sessionId"));
        Assert.NotEmpty(session.Metadata["sessionId"]);
        Assert.Empty(artifacts);
    }

    [Fact]
    public async Task Click_WithFakePage_InvokesLocatorAndReturnsSuccess()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var clicked = false;
        var locator = CreateLocatorProxy(
            selector: "#save",
            onClick: () => clicked = true);
        var page = CreatePageProxy(
            locatorFactory: _ => locator);
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("click", new Dictionary<string, string> { ["selector"] = "#save" }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.True(clicked);
        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Invoked '#save'.", result.Message);
    }

    [Fact]
    public async Task Open_WithInjectedLauncher_StartsApplicationAndPopulatesMetadata()
    {
        using var tempDir = new TempDirectory();
        var applicationPath = Path.Combine(tempDir.Path, "sample-app.exe");
        File.WriteAllText(applicationPath, "stub");

        LaunchOptions? capturedLaunchOptions = null;
        FlawrightOptions? capturedFlawrightOptions = null;
        var page = CreatePageProxy(title: "Cress Window");
        var app = CreateAppProxy(CreateBrowserProxy(newPage: page));
        var driver = new FlawrightRuntimeDriver(
            (launchOptions, options, _) =>
            {
                capturedLaunchOptions = launchOptions;
                capturedFlawrightOptions = options;
                return Task.FromResult(app);
            },
            (_, _, _) => throw new NotSupportedException(),
            _ => Process.GetCurrentProcess());
        await using var session = await CreateSessionAsync(tempDir.Path, driver: driver);

        var result = await session.ExecuteAsync(
            MakeAction("open", new Dictionary<string, string> { ["application"] = applicationPath }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal($"Started desktop application '{Path.GetFileName(applicationPath)}'.", result.Message);
        Assert.NotNull(capturedLaunchOptions);
        Assert.Equal(applicationPath, capturedLaunchOptions!.ApplicationPath);
        Assert.Equal(Path.GetDirectoryName(applicationPath), capturedLaunchOptions.WorkingDirectory);
        Assert.NotNull(capturedFlawrightOptions);
        Assert.Equal("Cress Window", session.Metadata["windowTitle"]);
        Assert.Equal(applicationPath, session.Metadata["applicationPath"]);
        Assert.True(session.Metadata.ContainsKey("processId"));
    }

    [Fact]
    public async Task Attach_WithInjectedAttacher_UsesProcessIdAndPopulatesMetadata()
    {
        using var tempDir = new TempDirectory();
        var page = CreatePageProxy(title: "Attached Window");
        var app = CreateAppProxy(CreateBrowserProxy(newPage: page));
        AttachOptions? capturedAttachOptions = null;
        var driver = new FlawrightRuntimeDriver(
            (_, _, _) => throw new NotSupportedException(),
            (attachOptions, _, _) =>
            {
                capturedAttachOptions = attachOptions;
                return Task.FromResult(app);
            },
            _ => null);
        await using var session = await CreateSessionAsync(tempDir.Path, driver: driver);

        var result = await session.ExecuteAsync(
            MakeAction("attach", new Dictionary<string, string>
            {
                ["processId"] = Environment.ProcessId.ToString()
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.NotNull(capturedAttachOptions);
        Assert.Equal(Environment.ProcessId, capturedAttachOptions!.ProcessId);
        Assert.Equal("Attached Window", session.Metadata["windowTitle"]);
        Assert.True(session.Metadata.ContainsKey("processId"));
        Assert.True(session.Metadata.ContainsKey("processName"));
    }

    [Fact]
    public async Task AssertText_WithFakeLocatorText_ReturnsSuccess()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var locator = CreateLocatorProxy(
            selector: "#status",
            textContent: "Ready");
        var page = CreatePageProxy(
            locatorFactory: _ => locator);
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("assert-text", new Dictionary<string, string>
            {
                ["selector"] = "#status",
                ["text"] = "Ready"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Element text matched 'Ready'.", result.Message);
    }

    [Fact]
    public async Task Fill_WithFakeLocator_ReturnsSuccess()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        string? filledValue = null;
        var locator = CreateLocatorProxy(
            selector: "#name",
            onFill: value => filledValue = value);
        var page = CreatePageProxy(
            locatorFactory: _ => locator);
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("fill", new Dictionary<string, string>
            {
                ["selector"] = "#name",
                ["value"] = "Ada"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal("Ada", filledValue);
        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Entered text into '#name'.", result.Message);
    }

    [Fact]
    public async Task PressKey_WithFakeKeyboard_ReturnsSuccess()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        string? pressedKey = null;
        var keyboard = CreateProxy<IFlawrightKeyboard>((method, args) => method.Name switch
        {
            nameof(IFlawrightKeyboard.PressAsync) => Complete(() => pressedKey = (string?)args![0]),
            _ => DefaultReturn(method)
        });
        var page = CreatePageProxy(keyboard: keyboard);
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("press-key", new Dictionary<string, string> { ["key"] = "Enter" }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal("Enter", pressedKey);
        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Sent key 'Enter'.", result.Message);
    }

    [Fact]
    public async Task PressKey_WithSelector_UsesLocatorPressAndReturnsSuccess()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        string? pressedKey = null;
        var locator = CreateLocatorProxy(
            selector: "#name",
            onPress: key => pressedKey = key);
        var page = CreatePageProxy(
            locatorFactory: _ => locator);
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("press-key", new Dictionary<string, string>
            {
                ["selector"] = "#name",
                ["key"] = "Tab"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal("Tab", pressedKey);
        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Sent key 'Tab'.", result.Message);
    }

    [Fact]
    public async Task AssertText_WithWindowFlagAndFakePage_ReturnsSuccess()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var page = CreatePageProxy(title: "Cress Desktop Window");
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("assert-text", new Dictionary<string, string>
            {
                ["window"] = "true",
                ["expected"] = "Desktop"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Window title matched 'Desktop'.", result.Message);
    }

    [Fact]
    public async Task AssertWindowTitle_WithFakePage_ReturnsSuccess()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var page = CreatePageProxy(title: "Cress Desktop Window");
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("assert-window-title", new Dictionary<string, string>
            {
                ["title"] = "Desktop"
            }),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Window title matched 'Desktop'.", result.Message);
    }

    [Fact]
    public async Task Screenshot_WithFakePage_WritesArtifact()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var page = CreatePageProxy(screenshotBytes: [1, 2, 3, 4]);
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("screenshot", new Dictionary<string, string> { ["name"] = "Greeting Shot" }),
            MakeFlowContext(),
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("screenshots", artifact.Category);
        Assert.EndsWith("Greeting-Shot-001.png", artifact.RelativePath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(tempDir.Path, artifact.RelativePath)));
    }

    [Fact]
    public async Task UnsupportedFailure_WithScreenshotEvidence_AttachesArtifact()
    {
        using var tempDir = new TempDirectory();
        var config = new EffectiveConfig
        {
            Profile = new CressProfile
            {
                Evidence = new EvidenceProfileConfig
                {
                    Mode = "full"
                }
            }
        };
        await using var session = await CreateSessionAsync(tempDir.Path, config);

        var page = CreatePageProxy(screenshotBytes: [9, 8, 7]);
        SetPrivateField(session, "_page", page);

        var result = await session.ExecuteAsync(
            MakeAction("dance", []),
            MakeFlowContext(),
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("unsupported-flawright-operation", result.FailureClassification);
        Assert.Contains("Failure screenshot for dance", artifact.Description, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(tempDir.Path, artifact.RelativePath)));
    }

    [Fact]
    public async Task CaptureFinalEvidence_WithScreenshotsEnabled_WritesArtifact()
    {
        using var tempDir = new TempDirectory();
        var config = new EffectiveConfig
        {
            Profile = new CressProfile
            {
                Evidence = new EvidenceProfileConfig
                {
                    Screenshots = true
                }
            }
        };
        await using var session = await CreateSessionAsync(tempDir.Path, config);

        var page = CreatePageProxy(screenshotBytes: [4, 3, 2, 1]);
        SetPrivateField(session, "_page", page);

        var artifacts = await session.CaptureFinalEvidenceAsync(MakeFlowContext(), CancellationToken.None);

        var artifact = Assert.Single(artifacts);
        Assert.Equal("screenshots", artifact.Category);
        Assert.EndsWith("final-window-001.png", artifact.RelativePath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(tempDir.Path, artifact.RelativePath)));
    }

    [Fact]
    public async Task Close_WithOwnedApplication_DisposesAppAndClearsSessionState()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var disposed = false;
        var app = CreateProxy<IFlawright>((method, _) => method.Name switch
        {
            "DisposeAsync" => new ValueTask(Task.Run(() => disposed = true)),
            "get_Browser" => CreateProxy<IFlawrightBrowser>((m, a) => DefaultReturn(m)),
            _ => DefaultReturn(method)
        });

        SetPrivateField(session, "_ownedApp", app);
        SetPrivateField(session, "_activeApp", app);
        SetPrivateField(session, "_page", CreatePageProxy());
        SetPrivateField(session, "_ownsProcess", true);

        var result = await session.ExecuteAsync(
            MakeAction("close", []),
            MakeFlowContext(),
            CancellationToken.None);

        Assert.True(disposed);
        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("Application closed.", result.Message);
        Assert.Null(GetPrivateField(session, "_ownedApp"));
        Assert.Null(GetPrivateField(session, "_activeApp"));
        Assert.Null(GetPrivateField(session, "_page"));
    }

    [Fact]
    public async Task Private_ReadLocatorTextAsync_FallsBackToInnerTextThenInputValue()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);
        var method = GetPrivateMethod(session, "ReadLocatorTextAsync", isStatic: true);

        var innerLocator = CreateLocatorProxy(selector: "#status", textContent: "", innerText: "Ready now", inputValue: "ignored");
        var inputLocator = CreateLocatorProxy(selector: "#name", textContent: "", innerText: "", inputValue: "Ada");

        var innerResult = await InvokeAsync<string>(method, null, innerLocator, CancellationToken.None);
        var inputResult = await InvokeAsync<string>(method, null, inputLocator, CancellationToken.None);

        Assert.Equal("Ready now", innerResult);
        Assert.Equal("Ada", inputResult);
    }

    [Fact]
    public async Task Private_WaitForWindowTitleAsync_ReturnsMatchingTitle()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);
        var method = GetPrivateMethod(session, "WaitForWindowTitleAsync", isStatic: true);
        var page = CreatePageProxy(title: "Cress Desktop Window");

        var title = await InvokeAsync<string>(method, null, page, "desktop", 50, CancellationToken.None);

        Assert.Equal("Cress Desktop Window", title);
    }

    [Fact]
    public async Task Private_ResolveTimeoutMs_UsesActionProfileAndDefaultFallbacks()
    {
        using var overrideDir = new TempDirectory();
        await using var overrideSession = await CreateSessionAsync(overrideDir.Path);

        using var profileDir = new TempDirectory();
        await using var profileSession = await CreateSessionAsync(profileDir.Path, new EffectiveConfig
        {
            Profile = new CressProfile
            {
                Flawright = new FlawrightProfileConfig
                {
                    LaunchTimeoutMs = 4321
                }
            }
        });

        using var defaultsDir = new TempDirectory();
        await using var defaultSession = await CreateSessionAsync(defaultsDir.Path);

        var method = GetPrivateMethod(overrideSession, "ResolveTimeoutMs");

        var overrideTimeout = Assert.IsType<int>(method.Invoke(overrideSession, [MakeAction("click", new Dictionary<string, string> { ["timeout"] = "321" })]));
        var profileTimeout = Assert.IsType<int>(method.Invoke(profileSession, [MakeAction("click", [])]));
        var defaultTimeout = Assert.IsType<int>(method.Invoke(defaultSession, [MakeAction("click", [])]));

        Assert.Equal(321, overrideTimeout);
        Assert.Equal(4321, profileTimeout);
        Assert.Equal(10000, defaultTimeout);
    }

    [Fact]
    public async Task Private_ResolveApplicationPath_UsesActionAndProfileInputs()
    {
        using var tempDir = new TempDirectory();
        var config = new EffectiveConfig
        {
            Profile = new CressProfile
            {
                Flawright = new FlawrightProfileConfig
                {
                    ApplicationPath = "sample-app.exe"
                }
            }
        };
        await using var session = await CreateSessionAsync(tempDir.Path, config);
        var method = GetPrivateMethod(session, "ResolveApplicationPath");

        var actionPath = Assert.IsType<string>(method.Invoke(session, [MakeAction("open", new Dictionary<string, string>
        {
            ["application"] = Path.Combine("tools", "sample.exe")
        })]));
        var profilePath = Assert.IsType<string>(method.Invoke(session, [MakeAction("open", [])]));

        Assert.EndsWith(Path.Combine("tools", "sample.exe"), actionPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("sample-app.exe", profilePath);
    }

    [Fact]
    public async Task Private_BuildArgumentsArray_ResolveWorkingDirectory_AndWindowFlagBehaveAsExpected()
    {
        using var tempDir = new TempDirectory();
        await using var session = await CreateSessionAsync(tempDir.Path);

        var buildArguments = GetPrivateMethod(session, "BuildArgumentsArray", isStatic: true);
        var resolveWorkingDirectory = GetPrivateMethod(session, "ResolveWorkingDirectory");
        var tryGetWindowAssertion = GetPrivateMethod(session, "TryGetWindowAssertion", isStatic: true);

        var arguments = Assert.IsType<string[]>(buildArguments.Invoke(null, [MakeAction("open", new Dictionary<string, string> { ["arguments"] = "--demo" })]));
        var noArguments = buildArguments.Invoke(null, [MakeAction("open", [])]);
        var commandWorkingDirectory = Assert.IsType<string>(resolveWorkingDirectory.Invoke(session, ["sample-app.exe"]));
        var fileWorkingDirectory = Assert.IsType<string>(resolveWorkingDirectory.Invoke(session, [Path.Combine(tempDir.Path, "tools", "sample-app.exe")]));

        object?[] assertionArgs =
        [
            MakeAction("assert-text", new Dictionary<string, string> { ["window"] = "true", ["expected"] = "Cress" }),
            false
        ];
        var usesWindow = Assert.IsType<bool>(tryGetWindowAssertion.Invoke(null, assertionArgs));

        Assert.Equal(["--demo"], arguments);
        Assert.Null(noArguments);
        Assert.Equal(tempDir.Path, commandWorkingDirectory);
        Assert.Equal(Path.Combine(tempDir.Path, "tools"), fileWorkingDirectory);
        Assert.True(usesWindow);
        Assert.True(Assert.IsType<bool>(assertionArgs[1]));
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /// <summary>Creates a temporary directory and deletes it on dispose.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IFlawrightPage CreatePageProxy(
        Func<string, IFlawrightLocator>? locatorFactory = null,
        IFlawrightKeyboard? keyboard = null,
        byte[]? screenshotBytes = null,
        string title = "Cress Window")
        => CreateProxy<IFlawrightPage>((method, args) => method.Name switch
        {
            "Locator" => locatorFactory?.Invoke((string)args![0]!) ?? CreateLocatorProxy(selector: (string)args![0]!),
            "GetByTestId" => locatorFactory?.Invoke($"testid:{args![0]}") ?? CreateLocatorProxy(selector: $"testid:{args![0]}"),
            "TitleAsync" => Task.FromResult(title),
            "ScreenshotAsync" => Task.FromResult(screenshotBytes ?? [1, 2, 3]),
            "get_Keyboard" => keyboard ?? CreateProxy<IFlawrightKeyboard>((m, a) => DefaultReturn(m)),
            _ => DefaultReturn(method)
        });

    private static IFlawrightBrowser CreateBrowserProxy(IFlawrightPage? newPage = null, IFlawrightPage? waitedPage = null)
        => CreateProxy<IFlawrightBrowser>((method, _) => method.Name switch
        {
            nameof(IFlawrightBrowser.NewPageAsync) => Task.FromResult(newPage ?? CreatePageProxy()),
            nameof(IFlawrightBrowser.WaitForPageAsync) => Task.FromResult(waitedPage ?? newPage ?? CreatePageProxy()),
            _ => DefaultReturn(method)
        });

    private static IFlawright CreateAppProxy(IFlawrightBrowser browser, Action? onDispose = null)
        => CreateProxy<IFlawright>((method, _) => method.Name switch
        {
            "get_Browser" => browser,
            "DisposeAsync" => new ValueTask(Task.Run(() => onDispose?.Invoke())),
            _ => DefaultReturn(method)
        });

    private static IFlawrightLocator CreateLocatorProxy(
        string selector,
        Action? onClick = null,
        Action<string>? onFill = null,
        Action<string>? onPress = null,
        string? textContent = null,
        string? innerText = null,
        string? inputValue = null)
        => CreateProxy<IFlawrightLocator>((method, args) => method.Name switch
        {
            "get_Selector" => selector,
            "ClickAsync" => Complete(onClick),
            "FillAsync" => Complete(() => onFill?.Invoke((string)args![0]!)),
            "PressAsync" => Complete(() => onPress?.Invoke((string)args![0]!)),
            "TextContentAsync" => Task.FromResult(textContent ?? string.Empty),
            "InnerTextAsync" => Task.FromResult(innerText ?? string.Empty),
            "InputValueAsync" => Task.FromResult(inputValue ?? string.Empty),
            "And" => args![0]!,
            _ => DefaultReturn(method)
        });

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestDispatchProxy<T>>();
        ((TestDispatchProxy<T>)(object)proxy).Handler = handler;
        return proxy;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field.GetValue(target);
    }

    private static MethodInfo GetPrivateMethod(object target, string methodName, bool isStatic = false)
    {
        var flags = BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        var method = target.GetType().GetMethod(methodName, flags);
        Assert.NotNull(method);
        return method;
    }

    private static async Task<T> InvokeAsync<T>(MethodInfo method, object? target, params object?[] args)
    {
        var task = Assert.IsAssignableFrom<Task<T>>(method.Invoke(target, args));
        return await task;
    }

    private static Task Complete(Action? action = null)
    {
        action?.Invoke();
        return Task.CompletedTask;
    }

    private static object? DefaultReturn(MethodInfo method)
    {
        if (method.DeclaringType == typeof(object))
        {
            return method.Name switch
            {
                nameof(ToString) => method.ReturnType == typeof(string) ? method.DeclaringType?.Name ?? "proxy" : null,
                nameof(GetHashCode) => 0,
                nameof(Equals) => false,
                _ => null
            };
        }

        var returnType = method.ReturnType;
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType == typeof(ValueTask))
        {
            return ValueTask.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result]);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    private class TestDispatchProxy<T> : DispatchProxy
        where T : class
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            return Handler(targetMethod, args);
        }
    }
}
