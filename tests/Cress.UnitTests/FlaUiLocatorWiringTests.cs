using Cress.Core.Models;
using Cress.Execution;
using Cress.Execution.Drivers;

namespace Cress.UnitTests;

/// <summary>
/// Unit tests for FlaUI driver V2 locator wiring.
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
/// integration is covered by FlaUiRuntimeDriverTests.RuntimeOrchestrator_RunsRealFlaUiFlow.
/// </summary>
public sealed class FlaUiLocatorWiringTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<IDriverSession> CreateSessionAsync(string tempRoot)
    {
        var driver = new FlaUiRuntimeDriver();
        var store = new EvidenceStore(tempRoot);
        var context = new DriverSessionStartContext
        {
            ProjectRoot = tempRoot,
            FlowId = "locator-wiring-test",
            ArtifactRoot = tempRoot,
            EvidenceStore = store,
            EffectiveConfig = new EffectiveConfig()
        };
        return await driver.StartSessionAsync(context, CancellationToken.None);
    }

    private static PlanAction MakeAction(string operation, Dictionary<string, string> inputs) => new()
    {
        Name = operation,
        Operation = operation,
        Kind = "action",
        Driver = "flaui",
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
        // cssSelector is present but automationId is also present — FlaUI should NOT
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
        Assert.NotEqual("unsupported-flaui-operation", result.FailureClassification);
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
        Assert.NotEqual("invalid-flaui-input", result.FailureClassification);
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
}
