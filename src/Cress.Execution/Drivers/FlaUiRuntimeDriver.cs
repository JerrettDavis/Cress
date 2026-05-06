using System.Diagnostics;
using Cress.Core.Models;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Cress.Execution.Drivers;

public sealed class FlaUiRuntimeDriver : IRuntimeDriver
{
    public string Name => "flaui";

    public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog)
    {
        var diagnostics = new List<Diagnostic>();
        if (!OperatingSystem.IsWindows())
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "DRV101",
                Message = "The FlaUI driver is only supported on Windows.",
                File = Path.Combine(catalog.ProjectRoot, ".cress", "config.yaml")
            });
            return diagnostics;
        }

        if (!Environment.UserInteractive)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = "DRV102",
                Message = "The current session is not interactive. Desktop UI automation may fail.",
                File = Path.Combine(catalog.ProjectRoot, ".cress", "profiles", $"{catalog.EffectiveConfig.ActiveProfile}.yaml")
            });
        }

        var applicationPath = catalog.EffectiveConfig.Profile.FlaUi?.ApplicationPath;
        if (!string.IsNullOrWhiteSpace(applicationPath))
        {
            var resolvedPath = ResolveConfiguredPath(catalog.ProjectRoot, applicationPath);
            diagnostics.Add(new Diagnostic
            {
                Severity = File.Exists(resolvedPath) ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                Code = File.Exists(resolvedPath) ? "DRV103" : "DRV104",
                Message = File.Exists(resolvedPath)
                    ? $"FlaUI application path resolved to '{resolvedPath}'."
                    : $"FlaUI application path '{resolvedPath}' does not exist. The flow must supply a valid application path or update the profile.",
                File = Path.Combine(catalog.ProjectRoot, ".cress", "profiles", $"{catalog.EffectiveConfig.ActiveProfile}.yaml")
            });
        }

        return diagnostics;
    }

    public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
        => Task.FromResult<IDriverSession>(new FlaUiDriverSession(context));

    private static string ResolveConfiguredPath(string projectRoot, string candidate)
        => Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(projectRoot, candidate));

    private sealed class FlaUiDriverSession : IDriverSession
    {
        private readonly DriverSessionStartContext _context;
        private readonly UIA3Automation _automation = new();
        private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "built-in",
            ["framework"] = "uia3",
            ["sessionId"] = Guid.NewGuid().ToString("N")
        };

        private FlaUI.Core.Application? _application;
        private Process? _process;
        private Window? _window;
        private bool _ownsProcess;
        private bool _disposed;
        private int _sequence;

        public FlaUiDriverSession(DriverSessionStartContext context)
        {
            _context = context;
        }

        public string Name => "flaui";

        public IReadOnlyDictionary<string, string> Metadata => _metadata;

        public Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
        {
            try
            {
                var operation = (action.Operation ?? action.Name).Trim().ToLowerInvariant();
                var result = operation switch
                {
                    "open" or "launch" or "start" or "ui.open" or "ui.launch" or "ui.start" => OpenApplication(action),
                    "attach" or "ui.attach" => AttachApplication(action),
                    "click" or "invoke" or "ui.click" or "ui.invoke" => ClickElement(action),
                    "fill" or "type" or "enter-text" or "entertext" or "ui.fill" or "ui.type" or "ui.enter-text" => FillElement(action),
                    "assert-text" or "asserttext" or "ui.assert-text" or "ui.asserttext" => AssertText(action),
                    "assert-window-title" or "assertwindowtitle" or "assert-title" or "asserttitle" or "ui.assert-window-title" or "ui.wait-for-window" => AssertWindowTitle(action),
                    "screenshot" or "capture" or "capture-screenshot" or "ui.screenshot" => CaptureScreenshot(action.Inputs.TryGetValue("name", out var name) ? name : action.Name, "FlaUI screenshot"),
                    "close" or "stop" or "shutdown" or "ui.close" or "ui.stop" => CloseApplication(),
                    "press-key" or "presskey" or "ui.press-key" or "ui.presskey" => PressKey(action),
                    _ => Failure($"FlaUI operation '{action.Operation}' is not supported.", "unsupported-flaui-operation")
                };

                if (result.Outcome != RunOutcome.Passed)
                {
                    result = AttachFailureScreenshot(action, result);
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                var failure = Failure(ex.Message, "flaui-driver-error");
                return Task.FromResult(AttachFailureScreenshot(action, failure));
            }
        }

        public Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
        {
            if (!ShouldCaptureFinalEvidence() || EnsureWindow() is null)
            {
                return Task.FromResult<IReadOnlyList<EvidenceArtifact>>([]);
            }

            return Task.FromResult<IReadOnlyList<EvidenceArtifact>>(
            [
                CaptureScreenshot("final-window", "FlaUI final window screenshot").Artifacts.Single()
            ]);
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;

            try
            {
                if (_ownsProcess && _process is { HasExited: false })
                {
                    try
                    {
                        _process.CloseMainWindow();
                        if (!_process.WaitForExit(2000))
                        {
                            _process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                _application?.Dispose();
                _automation.Dispose();
                _process?.Dispose();
            }

            return ValueTask.CompletedTask;
        }

        private DriverExecutionResult OpenApplication(PlanAction action)
        {
            if (_process is { HasExited: false } && EnsureWindow() is not null)
            {
                return Success("Application is already running.");
            }

            var executablePath = ResolveApplicationPath(action);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return Failure("FlaUI open requires an application path in the step inputs or profile.", "application-path-missing");
            }

            if (!File.Exists(executablePath))
            {
                return Failure($"FlaUI application '{executablePath}' was not found.", "application-not-found");
            }

            var arguments = GetInput(action.Inputs, "arguments", "args") ?? _context.EffectiveConfig.Profile.FlaUi?.Arguments;
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                UseShellExecute = false
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return Failure($"FlaUI could not start '{executablePath}'.", "application-launch-failed");
            }

            _process = process;
            _ownsProcess = true;
            _application = FlaUI.Core.Application.Attach(process.Id);
            _window = WaitForWindow(GetWindowTitle(action), ResolveTimeoutMs(action));
            if (_window is null)
            {
                return Failure("FlaUI could not locate the main window after launch.", "window-not-found");
            }

            _metadata["processId"] = process.Id.ToString();
            _metadata["applicationPath"] = executablePath;
            _metadata["windowTitle"] = _window.Title;
            return Success($"Started desktop application '{Path.GetFileName(executablePath)}' (PID {process.Id}).");
        }

        private DriverExecutionResult AttachApplication(PlanAction action)
        {
            Process? process = null;
            if (action.Inputs.TryGetValue("processId", out var processIdValue) && int.TryParse(processIdValue, out var processId))
            {
                process = Process.GetProcessById(processId);
            }
            else if (action.Inputs.TryGetValue("processName", out var processName) && !string.IsNullOrWhiteSpace(processName))
            {
                process = Process.GetProcessesByName(processName).FirstOrDefault(candidate => !candidate.HasExited);
            }

            if (process is null)
            {
                return Failure("FlaUI attach requires a running processId or processName.", "application-attach-failed");
            }

            _process = process;
            _ownsProcess = false;
            _application = FlaUI.Core.Application.Attach(process.Id);
            _window = WaitForWindow(GetWindowTitle(action), ResolveTimeoutMs(action));
            if (_window is null)
            {
                return Failure($"FlaUI could not locate a window for process '{process.ProcessName}'.", "window-not-found");
            }

            _metadata["processId"] = process.Id.ToString();
            _metadata["processName"] = process.ProcessName;
            _metadata["windowTitle"] = _window.Title;
            return Success($"Attached to desktop application '{process.ProcessName}' (PID {process.Id}).");
        }

        private DriverExecutionResult ClickElement(PlanAction action)
        {
            var webOnlyError = CheckWebOnlyLocators(action.Inputs);
            if (webOnlyError is not null)
            {
                return webOnlyError;
            }

            var element = FindElement(action);
            if (element is null)
            {
                return Failure("FlaUI could not locate an element to click.", "locator-not-found");
            }

            if (element.Patterns.Invoke.IsSupported)
            {
                element.Patterns.Invoke.Pattern.Invoke();
                return Success($"Invoked '{DescribeElement(element)}'.");
            }

            if (element.Patterns.SelectionItem.IsSupported)
            {
                element.Patterns.SelectionItem.Pattern.Select();
                return Success($"Selected '{DescribeElement(element)}'.");
            }

            return Failure($"Element '{DescribeElement(element)}' does not support click or invoke patterns.", "unsupported-element-pattern");
        }

        private DriverExecutionResult FillElement(PlanAction action)
        {
            var webOnlyError = CheckWebOnlyLocators(action.Inputs);
            if (webOnlyError is not null)
            {
                return webOnlyError;
            }

            var value = GetInput(action.Inputs, "value", "text");
            if (string.IsNullOrWhiteSpace(value))
            {
                return Failure("FlaUI fill requires a 'value' or 'text' input.", "invalid-flaui-input");
            }

            var element = FindElement(action);
            if (element is null)
            {
                return Failure("FlaUI could not locate an element to fill.", "locator-not-found");
            }

            var textBox = element.AsTextBox();
            textBox.Text = string.Empty;
            textBox.Enter(value);
            return Success($"Entered text into '{DescribeElement(element)}'.");
        }

        private DriverExecutionResult AssertText(PlanAction action)
        {
            var webOnlyError = CheckWebOnlyLocators(action.Inputs);
            if (webOnlyError is not null)
            {
                return webOnlyError;
            }

            var expected = GetInput(action.Inputs, "text", "equals", "expected");
            if (string.IsNullOrWhiteSpace(expected))
            {
                return Failure("FlaUI text assertions require a 'text', 'equals', or 'expected' input.", "invalid-assertion");
            }

            if (!HasLocator(action.Inputs))
            {
                var window = EnsureWindow();
                if (window is null)
                {
                    return Failure("FlaUI could not locate a window to assert against.", "window-not-found");
                }

                var found = window.FindFirstDescendant(_automation.ConditionFactory.ByName(expected));
                return found is not null
                    ? Success($"Found text '{expected}'.")
                    : Failure($"Expected text '{expected}' was not found.", "assertion-failed");
            }

            var element = FindElement(action);
            if (element is null)
            {
                return Failure("FlaUI could not locate an element for text assertion.", "locator-not-found");
            }

            var actual = element.Name;
            var normalizedExpected = NormalizeWhitespace(expected);
            var normalizedActual = NormalizeWhitespace(actual);
            if (string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal))
            {
                return Success($"Element text matched '{expected}'.");
            }

            if (!string.IsNullOrWhiteSpace(normalizedActual) && normalizedActual.Contains(normalizedExpected, StringComparison.Ordinal))
            {
                return Success($"Element text contained '{expected}'.");
            }

            if (element.Patterns.Value.IsSupported)
            {
                var value = element.Patterns.Value.Pattern.Value.ValueOrDefault;
                var normalizedValue = NormalizeWhitespace(value);
                if (string.Equals(normalizedValue, normalizedExpected, StringComparison.Ordinal))
                {
                    return Success($"Element value matched '{expected}'.");
                }

                if (!string.IsNullOrWhiteSpace(normalizedValue) && normalizedValue.Contains(normalizedExpected, StringComparison.Ordinal))
                {
                    return Success($"Element value contained '{expected}'.");
                }
            }

            return Failure($"Expected text '{expected}', but found '{actual}'.", "assertion-failed");
        }

        private static string NormalizeWhitespace(string? value)
            => string.Join(' ', (value ?? string.Empty)
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        private DriverExecutionResult AssertWindowTitle(PlanAction action)
        {
            var expected = GetInput(action.Inputs, "title", "equals", "expected");
            if (string.IsNullOrWhiteSpace(expected))
            {
                return Failure("FlaUI window title assertions require a 'title', 'equals', or 'expected' input.", "invalid-assertion");
            }

            var window = EnsureWindow();
            if (window is null)
            {
                return Failure("FlaUI could not locate the application window.", "window-not-found");
            }

            return string.Equals(window.Title, expected, StringComparison.Ordinal)
                ? Success($"Window title matched '{expected}'.")
                : Failure($"Expected window title '{expected}', but found '{window.Title}'.", "assertion-failed");
        }

        private DriverExecutionResult PressKey(PlanAction action)
        {
            var key = GetInput(action.Inputs, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                return Failure("FlaUI press-key requires a 'key' input.", "invalid-flaui-input");
            }

            var window = EnsureWindow();
            if (window is null)
            {
                return Failure("FlaUI could not locate a window to send key input to.", "window-not-found");
            }

            // Use FlaUI keyboard simulation via SendKeys pattern.
            // For simple key names, map common values to their keyboard representations.
            try
            {
                FlaUI.Core.Input.Keyboard.Type(key);
                return Success($"Pressed key '{key}'.");
            }
            catch (Exception ex)
            {
                return Failure($"FlaUI key press failed: {ex.Message}", "key-press-failed");
            }
        }

        private DriverExecutionResult CloseApplication()
        {
            if (_process is null || _process.HasExited)
            {
                return Success("No desktop application was running.");
            }

            if (_ownsProcess)
            {
                try
                {
                    _process.CloseMainWindow();
                    if (!_process.WaitForExit(2000))
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    return Failure(ex.Message, "application-close-failed");
                }
            }

            return Success("Desktop application closed.");
        }

        private AutomationElement? FindElement(PlanAction action)
        {
            var window = EnsureWindow();
            if (window is null)
            {
                return null;
            }

            var timeoutMs = ResolveTimeoutMs(action);
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                var element = TryFindElement(window, action.Inputs);
                if (element is not null)
                {
                    return element;
                }

                Thread.Sleep(150);
            }
            while (DateTime.UtcNow < until);

            return null;
        }

        /// <summary>
        /// Validates that a step is not using web-only locators without a desktop-native fallback.
        /// When a step has ONLY <c>cssSelector</c> or <c>xpath</c> (no desktop-friendly locator),
        /// returns a clear diagnostic failure.
        /// <para>
        /// Mixed locator preference: when both <c>testId</c> (or any desktop locator) and
        /// <c>cssSelector</c>/<c>xpath</c> are present, FlaUI uses the desktop locator silently
        /// and ignores the web-only fields. This enables flows shared across desktop and web drivers.
        /// </para>
        /// </summary>
        private DriverExecutionResult? CheckWebOnlyLocators(IReadOnlyDictionary<string, string> inputs)
        {
            var hasWebOnly = (inputs.ContainsKey("cssSelector") && !string.IsNullOrWhiteSpace(inputs["cssSelector"]))
                || (inputs.ContainsKey("xpath") && !string.IsNullOrWhiteSpace(inputs["xpath"]));

            if (!hasWebOnly)
            {
                return null;
            }

            // If the step ONLY has web-only locators with no desktop-native fallback, fail clearly.
            var hasDesktopLocator = (inputs.TryGetValue("automationId", out var aid) && !string.IsNullOrWhiteSpace(aid))
                || (inputs.TryGetValue("name", out var nm) && !string.IsNullOrWhiteSpace(nm))
                || (inputs.TryGetValue("controlType", out var ct) && !string.IsNullOrWhiteSpace(ct))
                || (inputs.TryGetValue("testId", out var tid) && !string.IsNullOrWhiteSpace(tid))
                || (inputs.TryGetValue("role", out var role) && !string.IsNullOrWhiteSpace(role))
                || (inputs.TryGetValue("label", out var lbl) && !string.IsNullOrWhiteSpace(lbl))
                || (inputs.TryGetValue("text", out var txt) && !string.IsNullOrWhiteSpace(txt));

            if (!hasDesktopLocator)
            {
                var webKey = inputs.ContainsKey("cssSelector") && !string.IsNullOrWhiteSpace(inputs["cssSelector"])
                    ? "cssSelector"
                    : "xpath";
                return Failure(
                    $"Locator strategy '{webKey}' is not supported by the desktop driver. Use automationId, testId, name, role, or label.",
                    "locator-strategy-not-supported");
            }

            // Mixed (desktop + web-only): silently ignore the web-only fields and proceed with
            // desktop locator. This allows a single flow YAML to target both FlaUI and Playwright.
            return null;
        }

        /// <summary>
        /// Resolves a UIA element from the window using the following locator priority order:
        /// <list type="number">
        ///   <item><c>automationId</c> — direct UIA AutomationId match (highest precision)</item>
        ///   <item><c>testId</c> — first-class alias for automationId on desktop (V2)</item>
        ///   <item><c>name</c> + <c>controlType</c> — combined UIA Name + ControlType AND match</item>
        ///   <item><c>name</c> alone — UIA Name match</item>
        ///   <item><c>label</c> — accessible label; attempts UIA LabeledBy relation, falls back to Name search</item>
        ///   <item><c>role</c> alone — matches by ControlType; returns first match (wide — emits warning suffix)</item>
        ///   <item><c>text</c> — visible inner text / Name fallback</item>
        ///   <item><c>cssSelector</c> / <c>xpath</c> — web-only; blocked by <see cref="CheckWebOnlyLocators"/> before reaching here</item>
        /// </list>
        /// When multiple fields are present, higher-priority fields are preferred. <c>name</c> and
        /// <c>controlType</c>/<c>role</c> are AND-combined with each other for precision when both
        /// are present alongside a higher-priority anchor (e.g. automationId + name = more specific match).
        /// </summary>
        private AutomationElement? TryFindElement(Window window, IReadOnlyDictionary<string, string> inputs)
        {
            ConditionBase? condition = null;
            var factory = _automation.ConditionFactory;

            // Priority 1: automationId — direct UIA AutomationId (highest precision)
            if (inputs.TryGetValue("automationId", out var automationId) && !string.IsNullOrWhiteSpace(automationId))
            {
                condition = factory.ByAutomationId(automationId);
            }

            // Priority 2: testId — first-class desktop alias for automationId (V2)
            // When testId and cssSelector both present, testId is used silently (cssSelector blocked earlier).
            if (condition is null
                && inputs.TryGetValue("testId", out var testId) && !string.IsNullOrWhiteSpace(testId))
            {
                condition = factory.ByAutomationId(testId);
            }

            // Priority 3/4: name — UIA Name; AND-combined with existing condition for precision
            if (inputs.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                condition = condition is null ? factory.ByName(name) : condition.And(factory.ByName(name));
            }

            // controlType — explicit UIA ControlType; AND-combined for precision
            if (inputs.TryGetValue("controlType", out var controlTypeName)
                && TryResolveControlType(controlTypeName, out var controlType))
            {
                condition = condition is null ? factory.ByControlType(controlType) : condition.And(factory.ByControlType(controlType));
            }

            // role — cross-platform alias for controlType (V2 first-class); only applied when controlType absent.
            // When role is the sole locator (no automationId/testId/name), the match is wide — returns first element
            // of the given ControlType. See summary for warning.
            if (!inputs.ContainsKey("controlType")
                && inputs.TryGetValue("role", out var roleName)
                && TryResolveControlType(roleName, out var roleControlType))
            {
                condition = condition is null ? factory.ByControlType(roleControlType) : condition.And(factory.ByControlType(roleControlType));
            }

            if (condition is not null)
            {
                return window.FindFirstDescendant(condition);
            }

            // Priority 5: label — UIA LabeledBy relation if available; falls back to Name search.
            // Note: true UIA LabeledBy traversal requires element.Properties.LabeledBy, which is only
            // available via the element itself, not as a tree condition. We use Name search as the
            // practical desktop equivalent (sufficient for most labelled controls).
            if (inputs.TryGetValue("label", out var label) && !string.IsNullOrWhiteSpace(label))
            {
                // First try: find a Text/Label element whose Name matches, then return its LabeledBy target.
                var labelElement = window.FindFirstDescendant(factory.ByName(label));
                if (labelElement is not null)
                {
                    try
                    {
                        var labeledBy = labelElement.Properties.LabeledBy.Value;
                        if (labeledBy is not null)
                        {
                            return labeledBy;
                        }
                    }
                    catch
                    {
                        // LabeledBy not supported on this element; fall through to direct Name match.
                    }

                    return labelElement;
                }

                return window.FindFirstDescendant(factory.ByName(label));
            }

            // Priority 6: text — visible content / Name fallback
            if (inputs.TryGetValue("text", out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return window.FindFirstDescendant(factory.ByName(text));
            }

            // cssSelector / xpath — web-only; handled by CheckWebOnlyLocators before we reach here
            return null;
        }

        private Window? EnsureWindow()
        {
            if (_window is not null)
            {
                return _window;
            }

            if (_application is null)
            {
                return null;
            }

            _window = WaitForWindow(_context.EffectiveConfig.Profile.FlaUi?.WindowTitle, ResolveTimeoutMs(null));
            return _window;
        }

        private Window? WaitForWindow(string? expectedTitle, int timeoutMs)
        {
            if (_application is null)
            {
                return null;
            }

            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                var window = _application.GetMainWindow(_automation);
                if (window is not null
                    && (string.IsNullOrWhiteSpace(expectedTitle)
                        || window.Title.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase)))
                {
                    return window;
                }

                Thread.Sleep(200);
            }
            while (DateTime.UtcNow < until);

            return null;
        }

        private string? ResolveApplicationPath(PlanAction action)
        {
            var candidate = GetInput(action.Inputs, "application", "path", "executable")
                ?? _context.EffectiveConfig.Profile.FlaUi?.ApplicationPath;
            return string.IsNullOrWhiteSpace(candidate)
                ? null
                : ResolveConfiguredPath(_context.ProjectRoot, candidate);
        }

        private string? GetWindowTitle(PlanAction action)
            => GetInput(action.Inputs, "windowTitle", "title") ?? _context.EffectiveConfig.Profile.FlaUi?.WindowTitle;

        private int ResolveTimeoutMs(PlanAction? action)
        {
            if (action is not null
                && action.Inputs.TryGetValue("timeoutMs", out var timeoutValue)
                && int.TryParse(timeoutValue, out var explicitTimeout)
                && explicitTimeout > 0)
            {
                return explicitTimeout;
            }

            return _context.EffectiveConfig.Profile.FlaUi?.LaunchTimeoutMs
                ?? _context.EffectiveConfig.Profile.Timeouts?.Driver
                ?? 10000;
        }

        private DriverExecutionResult CaptureScreenshot(string name, string description)
        {
            var window = EnsureWindow();
            if (window is null)
            {
                return Failure("FlaUI could not locate the application window for screenshot capture.", "window-not-found");
            }

            var relativePath = _context.EvidenceStore.MakeRelativePath("screenshots", $"{++_sequence:D3}-{name}.png");
            var artifact = _context.EvidenceStore.WriteFile(relativePath, path =>
            {
                using var image = Capture.Element(window);
                image.ToFile(path);
            }, "screenshots", description);

            return new DriverExecutionResult
            {
                Outcome = RunOutcome.Passed,
                Message = $"Captured screenshot '{Path.GetFileName(relativePath)}'.",
                Artifacts = [artifact]
            };
        }

        private DriverExecutionResult AttachFailureScreenshot(PlanAction action, DriverExecutionResult result)
        {
            if (!ShouldCaptureFailureScreenshots() || EnsureWindow() is null)
            {
                return result;
            }

            try
            {
                var screenshot = CaptureScreenshot($"{action.Name}-failure", $"Failure screenshot for {action.Name}");
                return result with
                {
                    Artifacts = result.Artifacts.Concat(screenshot.Artifacts).ToList()
                };
            }
            catch
            {
                return result;
            }
        }

        private bool ShouldCaptureFailureScreenshots()
            => _context.EffectiveConfig.Profile.Evidence?.Screenshots
                ?? !string.Equals(_context.EffectiveConfig.Profile.Evidence?.Mode, "minimal", StringComparison.OrdinalIgnoreCase);

        private bool ShouldCaptureFinalEvidence()
            => string.Equals(_context.EffectiveConfig.Profile.Evidence?.Mode, "full", StringComparison.OrdinalIgnoreCase);

        private static bool HasLocator(IReadOnlyDictionary<string, string> inputs)
            => (inputs.TryGetValue("automationId", out var aid) && !string.IsNullOrWhiteSpace(aid))
                || (inputs.TryGetValue("name", out var nm) && !string.IsNullOrWhiteSpace(nm))
                || (inputs.TryGetValue("controlType", out var ct) && !string.IsNullOrWhiteSpace(ct))
                || (inputs.TryGetValue("testId", out var tid) && !string.IsNullOrWhiteSpace(tid))
                || (inputs.TryGetValue("role", out var role) && !string.IsNullOrWhiteSpace(role))
                || (inputs.TryGetValue("label", out var lbl) && !string.IsNullOrWhiteSpace(lbl))
                || (inputs.TryGetValue("text", out var txt) && !string.IsNullOrWhiteSpace(txt))
                || (inputs.TryGetValue("cssSelector", out var css) && !string.IsNullOrWhiteSpace(css))
                || (inputs.TryGetValue("xpath", out var xp) && !string.IsNullOrWhiteSpace(xp));

        private static string? GetInput(IReadOnlyDictionary<string, string> inputs, params string[] names)
        {
            foreach (var name in names)
            {
                if (inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string DescribeElement(AutomationElement element)
            => !string.IsNullOrWhiteSpace(element.AutomationId)
                ? element.AutomationId
                : !string.IsNullOrWhiteSpace(element.Name)
                    ? element.Name
                    : element.ControlType.ToString();

        private static bool TryResolveControlType(string value, out ControlType controlType)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "button":
                    controlType = ControlType.Button;
                    return true;
                case "edit":
                case "textbox":
                case "text-box":
                    controlType = ControlType.Edit;
                    return true;
                case "text":
                case "label":
                    controlType = ControlType.Text;
                    return true;
                case "window":
                    controlType = ControlType.Window;
                    return true;
                case "checkbox":
                case "check-box":
                    controlType = ControlType.CheckBox;
                    return true;
                default:
                    controlType = ControlType.Custom;
                    return false;
            }
        }

        private static DriverExecutionResult Success(string message) => new()
        {
            Outcome = RunOutcome.Passed,
            Message = message
        };

        private static DriverExecutionResult Failure(string message, string classification) => new()
        {
            Outcome = RunOutcome.Failed,
            Message = message,
            FailureClassification = classification
        };
    }
}
