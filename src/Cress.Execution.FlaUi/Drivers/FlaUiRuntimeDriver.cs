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

            if (TryGetWindowAssertion(action, out var usesWindow))
            {
                var window = EnsureWindow();
                if (window is null)
                {
                    return Failure("FlaUI could not locate a window to assert against.", "window-not-found");
                }

                var actualWindowTitle = window.Title ?? string.Empty;
                return ContainsOrEquals(actualWindowTitle, expected)
                    ? Success($"Window title matched '{expected}'.")
                    : Failure($"Expected window title to match '{expected}', but found '{actualWindowTitle}'.", "assertion-failed");
            }

            var element = FindElement(action);
            if (element is null)
            {
                return Failure("FlaUI could not locate an element for text assertion.", "locator-not-found");
            }

            var actual = ReadElementText(element);
            return ContainsOrEquals(actual, expected)
                ? Success($"Element text matched '{expected}'.")
                : Failure($"Expected element text to match '{expected}', but found '{actual}'.", "assertion-failed");
        }

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

            return ContainsOrEquals(window.Title ?? string.Empty, expected)
                ? Success($"Window title matched '{expected}'.")
                : Failure($"Expected window title to match '{expected}', but found '{window.Title}'.", "assertion-failed");
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

            try
            {
                window.Focus();
                FlaUI.Core.Input.Keyboard.Type(key);
                return Success($"Sent key '{key}'.");
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
                return Success("Application is already closed.");
            }

            try
            {
                if (_ownsProcess)
                {
                    _process.CloseMainWindow();
                    if (!_process.WaitForExit(2000))
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
            }

            return Success("Application closed.");
        }

        private DriverExecutionResult CaptureScreenshot(string name, string description)
        {
            var window = EnsureWindow();
            if (window is null)
            {
                return Failure("FlaUI could not locate the application window for screenshot capture.", "window-not-found");
            }

            Directory.CreateDirectory(_context.ArtifactRoot);
            var fileName = $"{Sanitize(name)}-{Interlocked.Increment(ref _sequence):D3}.png";
            var fullPath = Path.Combine(_context.ArtifactRoot, fileName);
            Capture.Element(window).ToFile(fullPath);
            return Success(description, [
                new EvidenceArtifact
                {
                    Category = "screenshot",
                    RelativePath = fileName,
                    Description = description,
                    MediaType = "image/png",
                    SizeBytes = new FileInfo(fullPath).Length
                }
            ]);
        }

        private DriverExecutionResult AttachFailureScreenshot(PlanAction action, DriverExecutionResult result)
        {
            if (!ShouldCaptureFailureEvidence() || EnsureWindow() is null)
            {
                return result;
            }

            var screenshot = CaptureScreenshot(action.Name, $"Failure screenshot for {action.Name}");
            if (screenshot.Outcome != RunOutcome.Passed)
            {
                return result;
            }

            return result with
            {
                Artifacts = result.Artifacts.Concat(screenshot.Artifacts).ToList()
            };
        }

        private bool ShouldCaptureFailureEvidence()
            => string.Equals(_context.EffectiveConfig.Profile.Evidence?.Screenshots?.ToString(), bool.TrueString, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_context.EffectiveConfig.Profile.Evidence?.ScreenshotPolicy, "on-failure", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_context.EffectiveConfig.Profile.Evidence?.Mode, "full", StringComparison.OrdinalIgnoreCase);

        private bool ShouldCaptureFinalEvidence()
            => string.Equals(_context.EffectiveConfig.Profile.Evidence?.Screenshots?.ToString(), bool.TrueString, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_context.EffectiveConfig.Profile.Evidence?.Mode, "full", StringComparison.OrdinalIgnoreCase);

        private Window? EnsureWindow()
            => _window ??= WaitForWindow(_context.EffectiveConfig.Profile.FlaUi?.WindowTitle, _context.EffectiveConfig.Profile.FlaUi?.LaunchTimeoutMs ?? 10000);

        private Window? WaitForWindow(string? expectedTitle, int timeoutMs)
        {
            if (_application is null)
            {
                return null;
            }

            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs <= 0 ? 10000 : timeoutMs);
            while (DateTime.UtcNow < end)
            {
                var windows = _application.GetAllTopLevelWindows(_automation);
                var window = string.IsNullOrWhiteSpace(expectedTitle)
                    ? windows.FirstOrDefault()
                    : windows.FirstOrDefault(candidate => candidate.Title.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase));
                if (window is not null)
                {
                    return window;
                }

                Thread.Sleep(250);
            }

            return null;
        }

        private AutomationElement? FindElement(PlanAction action)
        {
            var window = EnsureWindow();
            if (window is null)
            {
                return null;
            }

            var conditionFactory = _automation.ConditionFactory;
            var conditions = new List<ConditionBase>();

            if (TryGetInput(action.Inputs, out var automationId, "automationId"))
            {
                conditions.Add(conditionFactory.ByAutomationId(automationId));
            }

            if (TryGetInput(action.Inputs, out var name, "name", "label", "text"))
            {
                conditions.Add(conditionFactory.ByName(name));
            }

            if (TryGetInput(action.Inputs, out var controlTypeValue, "controlType", "role") && TryMapControlType(controlTypeValue, out var controlType))
            {
                conditions.Add(conditionFactory.ByControlType(controlType));
            }

            if (conditions.Count == 0)
            {
                return null;
            }

            var combined = conditions.Count == 1
                ? conditions[0]
                : conditions.Skip(1).Aggregate(conditions[0], (current, next) => current.And(next));

            return window.FindFirstDescendant(combined);
        }

        private DriverExecutionResult? CheckWebOnlyLocators(IReadOnlyDictionary<string, string> inputs)
        {
            if (inputs.ContainsKey("cssSelector") || inputs.ContainsKey("xpath"))
            {
                return Failure("FlaUI does not support cssSelector or xpath locators. Use automationId, name, label, or controlType.", "unsupported-locator");
            }

            return null;
        }

        private string? ResolveApplicationPath(PlanAction action)
        {
            var candidate = GetInput(action.Inputs, "application", "applicationPath", "path")
                ?? _context.EffectiveConfig.Profile.FlaUi?.ApplicationPath;

            return string.IsNullOrWhiteSpace(candidate)
                ? null
                : ResolveConfiguredPath(_context.ProjectRoot, candidate);
        }

        private string? GetWindowTitle(PlanAction action)
            => GetInput(action.Inputs, "windowTitle", "title") ?? _context.EffectiveConfig.Profile.FlaUi?.WindowTitle;

        private int ResolveTimeoutMs(PlanAction action)
        {
            if (TryGetInput(action.Inputs, out var timeoutValue, "timeoutMs", "timeout") && int.TryParse(timeoutValue, out var timeout))
            {
                return timeout;
            }

            return _context.EffectiveConfig.Profile.FlaUi?.LaunchTimeoutMs
                ?? _context.EffectiveConfig.Profile.Timeouts?.Driver
                ?? 10000;
        }

        private static bool ContainsOrEquals(string actual, string expected)
            => actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        private static string ReadElementText(AutomationElement element)
        {
            if (element.Patterns.Value.IsSupported)
            {
                return element.Patterns.Value.Pattern.Value ?? string.Empty;
            }

            if (element.Patterns.Text.IsSupported)
            {
                return element.Patterns.Text.Pattern.DocumentRange.GetText(-1);
            }

            return element.Name ?? string.Empty;
        }

        private static bool TryGetWindowAssertion(PlanAction action, out bool usesWindow)
        {
            usesWindow = action.Inputs.TryGetValue("window", out var windowFlag)
                && bool.TryParse(windowFlag, out var isWindow)
                && isWindow;
            return usesWindow;
        }

        private static bool TryMapControlType(string value, out ControlType controlType)
        {
            var normalized = value.Trim().Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            var property = typeof(ControlType).GetProperties()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase));
            if (property?.GetValue(null) is ControlType mapped)
            {
                controlType = mapped;
                return true;
            }

            controlType = ControlType.Custom;
            return false;
        }

        private static bool TryGetInput(IReadOnlyDictionary<string, string> inputs, out string value, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (inputs.TryGetValue(key, out var candidate) && !string.IsNullOrWhiteSpace(candidate))
                {
                    value = candidate;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        private static string? GetInput(IReadOnlyDictionary<string, string> inputs, params string[] keys)
            => TryGetInput(inputs, out var value, keys) ? value : null;

        private static string DescribeElement(AutomationElement element)
            => string.IsNullOrWhiteSpace(element.Name)
                ? element.AutomationId
                : element.Name;

        private static string Sanitize(string value)
            => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');

        private static DriverExecutionResult Success(string message, IReadOnlyList<EvidenceArtifact>? artifacts = null)
            => new()
            {
                Outcome = RunOutcome.Passed,
                Message = message,
                Artifacts = artifacts ?? []
            };

        private static DriverExecutionResult Failure(string message, string classification)
            => new()
            {
                Outcome = RunOutcome.Failed,
                Message = message,
                FailureClassification = classification
            };
    }
}
