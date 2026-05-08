using System.Diagnostics;
using Cress.Core.Models;
using JerrettDavis.Flawright;

namespace Cress.Execution.Drivers;

public sealed class FlawrightRuntimeDriver : IRuntimeDriver
{
    public string Name => "flawright";

    public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog)
    {
        var diagnostics = new List<Diagnostic>();
        if (!OperatingSystem.IsWindows())
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = "DRV101",
                Message = "The Flawright driver is only supported on Windows.",
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

        var applicationPath = catalog.EffectiveConfig.Profile.Flawright?.ApplicationPath;
        if (!string.IsNullOrWhiteSpace(applicationPath))
        {
            var resolvedPath = ResolveConfiguredPath(catalog.ProjectRoot, applicationPath);
            var usesCommandName = !RequiresDirectFileCheck(resolvedPath);
            diagnostics.Add(new Diagnostic
            {
                Severity = usesCommandName || File.Exists(resolvedPath) ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                Code = usesCommandName || File.Exists(resolvedPath) ? "DRV103" : "DRV104",
                Message = usesCommandName
                    ? $"Flawright application path '{resolvedPath}' will be resolved from PATH at runtime."
                    : File.Exists(resolvedPath)
                    ? $"Flawright application path resolved to '{resolvedPath}'."
                    : $"Flawright application path '{resolvedPath}' does not exist. The flow must supply a valid application path or update the profile.",
                File = Path.Combine(catalog.ProjectRoot, ".cress", "profiles", $"{catalog.EffectiveConfig.ActiveProfile}.yaml")
            });
        }

        return diagnostics;
    }

    public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
        => Task.FromResult<IDriverSession>(new FlawrightDriverSession(context));

    private static string ResolveConfiguredPath(string projectRoot, string candidate)
    {
        var expanded = Environment.ExpandEnvironmentVariables(candidate);
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        if (expanded.Contains(Path.DirectorySeparatorChar) || expanded.Contains(Path.AltDirectorySeparatorChar))
        {
            return Path.GetFullPath(Path.Combine(projectRoot, expanded));
        }

        return expanded;
    }

    private static bool RequiresDirectFileCheck(string candidate)
        => Path.IsPathRooted(candidate)
           || candidate.Contains(Path.DirectorySeparatorChar)
           || candidate.Contains(Path.AltDirectorySeparatorChar);

    private sealed class FlawrightDriverSession : IDriverSession
    {
        private readonly DriverSessionStartContext _context;
        private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "built-in",
            ["framework"] = "flawright",
            ["sessionId"] = Guid.NewGuid().ToString("N")
        };

        private IFlawright? _ownedApp;
        private IFlawright? _activeApp;
        private IFlawrightBrowser? _browser;
        private IFlawrightPage? _page;
        private Process? _process;
        private bool _ownsProcess;
        private bool _disposed;
        private int _sequence;

        public FlawrightDriverSession(DriverSessionStartContext context)
        {
            _context = context;
        }

        public string Name => "flawright";

        public IReadOnlyDictionary<string, string> Metadata => _metadata;

        public async Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
        {
            try
            {
                var operation = (action.Operation ?? action.Name).Trim().ToLowerInvariant();
                var result = operation switch
                {
                    "open" or "launch" or "start" or "ui.open" or "ui.launch" or "ui.start" => await OpenApplicationAsync(action, cancellationToken).ConfigureAwait(false),
                    "attach" or "ui.attach" => await AttachApplicationAsync(action, cancellationToken).ConfigureAwait(false),
                    "click" or "invoke" or "ui.click" or "ui.invoke" => await ClickElementAsync(action, cancellationToken).ConfigureAwait(false),
                    "fill" or "type" or "enter-text" or "entertext" or "ui.fill" or "ui.type" or "ui.enter-text" => await FillElementAsync(action, cancellationToken).ConfigureAwait(false),
                    "assert-text" or "asserttext" or "ui.assert-text" or "ui.asserttext" => await AssertTextAsync(action, cancellationToken).ConfigureAwait(false),
                    "assert-window-title" or "assertwindowtitle" or "assert-title" or "asserttitle" or "ui.assert-window-title" or "ui.wait-for-window" => await AssertWindowTitleAsync(action, cancellationToken).ConfigureAwait(false),
                    "screenshot" or "capture" or "capture-screenshot" or "ui.screenshot" => await CaptureScreenshotAsync(action.Inputs.TryGetValue("name", out var name) ? name : action.Name, "Flawright screenshot", cancellationToken).ConfigureAwait(false),
                    "close" or "stop" or "shutdown" or "ui.close" or "ui.stop" => await CloseApplicationAsync().ConfigureAwait(false),
                    "press-key" or "presskey" or "ui.press-key" or "ui.presskey" => await PressKeyAsync(action, cancellationToken).ConfigureAwait(false),
                    _ => Failure($"Flawright operation '{action.Operation}' is not supported.", "unsupported-flawright-operation")
                };

                if (result.Outcome != RunOutcome.Passed)
                {
                    result = await AttachFailureScreenshotAsync(action, result, cancellationToken).ConfigureAwait(false);
                }

                return result;
            }
            catch (FlawrightTimeoutException ex)
            {
                var failure = Failure(ex.Message, "locator-not-found");
                return await AttachFailureScreenshotAsync(action, failure, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var failure = Failure(ex.Message, "flawright-driver-error");
                return await AttachFailureScreenshotAsync(action, failure, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
        {
            if (!ShouldCaptureFinalEvidence() || _page is null)
            {
                return [];
            }

            return
            [
                (await CaptureScreenshotAsync("final-window", "Flawright final window screenshot", cancellationToken).ConfigureAwait(false)).Artifacts.Single()
            ];
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_ownedApp is not null)
            {
                await _ownedApp.DisposeAsync().ConfigureAwait(false);
            }

            _page = null;
            _browser = null;
            _activeApp = null;
            _ownedApp = null;
            _process?.Dispose();
            _process = null;
        }

        private async Task<DriverExecutionResult> OpenApplicationAsync(PlanAction action, CancellationToken cancellationToken)
        {
            if (_ownsProcess && _activeApp is not null && _page is not null)
            {
                return Success("Application is already running.");
            }

            var executablePath = ResolveApplicationPath(action);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return Failure("Flawright open requires an application path in the step inputs or profile.", "application-path-missing");
            }

            if (RequiresDirectFileCheck(executablePath) && !File.Exists(executablePath))
            {
                return Failure($"Flawright application '{executablePath}' was not found.", "application-not-found");
            }

            var timeout = ResolveTimeoutMs(action);
            var launchOptions = new LaunchOptions
            {
                ApplicationPath = executablePath,
                Arguments = BuildArgumentsArray(action),
                WorkingDirectory = ResolveWorkingDirectory(executablePath),
                StartupTimeout = TimeSpan.FromMilliseconds(timeout)
            };

            var flawright = await Flawright.LaunchAsync(launchOptions, BuildFlawrightOptions(timeout), cancellationToken).ConfigureAwait(false);
            var browser = flawright.Browser;
            var page = await ResolvePageAsync(browser, GetWindowTitle(action), timeout, cancellationToken).ConfigureAwait(false);

            if (_ownedApp is not null)
            {
                await _ownedApp.DisposeAsync().ConfigureAwait(false);
            }

            _ownedApp = flawright;
            _activeApp = flawright;
            _browser = browser;
            _page = page;
            _ownsProcess = true;
            _process = TryResolveProcessFromLaunch(executablePath);

            if (_process is not null)
            {
                _metadata["processId"] = _process.Id.ToString();
                _metadata["processName"] = _process.ProcessName;
            }

            _metadata["applicationPath"] = executablePath;
            _metadata["windowTitle"] = await page.TitleAsync(cancellationToken).ConfigureAwait(false);
            return Success($"Started desktop application '{Path.GetFileName(executablePath)}'.");
        }

        private async Task<DriverExecutionResult> AttachApplicationAsync(PlanAction action, CancellationToken cancellationToken)
        {
            AttachOptions? attachOptions = null;
            Process? process = null;

            if (action.Inputs.TryGetValue("processId", out var processIdValue) && int.TryParse(processIdValue, out var processId))
            {
                process = Process.GetProcessById(processId);
                attachOptions = new AttachOptions { ProcessId = processId };
            }
            else if (action.Inputs.TryGetValue("processName", out var processName) && !string.IsNullOrWhiteSpace(processName))
            {
                var normalized = Path.GetFileNameWithoutExtension(processName);
                process = Process.GetProcessesByName(normalized).FirstOrDefault(candidate => !candidate.HasExited);
                if (process is not null)
                {
                    attachOptions = new AttachOptions { ProcessId = process.Id };
                }
            }

            if (attachOptions is null)
            {
                return Failure("Flawright attach requires a running processId or processName.", "application-attach-failed");
            }

            var timeout = ResolveTimeoutMs(action);
            var flawright = await Flawright.AttachAsync(attachOptions, BuildFlawrightOptions(timeout), cancellationToken).ConfigureAwait(false);
            var browser = flawright.Browser;
            var page = await ResolvePageAsync(browser, GetWindowTitle(action), timeout, cancellationToken).ConfigureAwait(false);

            _activeApp = flawright;
            _browser = browser;
            _page = page;
            _process = process;
            _ownsProcess = false;

            if (process is not null)
            {
                _metadata["processId"] = process.Id.ToString();
                _metadata["processName"] = process.ProcessName;
            }

            _metadata["windowTitle"] = await page.TitleAsync(cancellationToken).ConfigureAwait(false);
            return Success(process is null
                ? "Attached to desktop application."
                : $"Attached to desktop application '{process.ProcessName}' (PID {process.Id}).");
        }

        private async Task<DriverExecutionResult> ClickElementAsync(PlanAction action, CancellationToken cancellationToken)
        {
            var webOnlyError = CheckWebOnlyLocators(action.Inputs);
            if (webOnlyError is not null)
            {
                return webOnlyError;
            }

            var locator = ResolveLocator(action, out var locatorError);
            if (locatorError is not null)
            {
                return locatorError;
            }

            if (locator is null)
            {
                return Failure("Flawright could not locate an element to click.", "locator-not-found");
            }

            await locator.ClickAsync(ct: cancellationToken).ConfigureAwait(false);
            return Success($"Invoked '{locator.Selector}'.");
        }

        private async Task<DriverExecutionResult> FillElementAsync(PlanAction action, CancellationToken cancellationToken)
        {
            var webOnlyError = CheckWebOnlyLocators(action.Inputs);
            if (webOnlyError is not null)
            {
                return webOnlyError;
            }

            var value = GetInput(action.Inputs, "value", "text");
            if (string.IsNullOrWhiteSpace(value))
            {
                return Failure("Flawright fill requires a 'value' or 'text' input.", "invalid-flawright-input");
            }

            var locator = ResolveLocator(action, out var locatorError);
            if (locatorError is not null)
            {
                return locatorError;
            }

            if (locator is null)
            {
                return Failure("Flawright could not locate an element to fill.", "locator-not-found");
            }

            await locator.FillAsync(value, ct: cancellationToken).ConfigureAwait(false);
            return Success($"Entered text into '{locator.Selector}'.");
        }

        private async Task<DriverExecutionResult> AssertTextAsync(PlanAction action, CancellationToken cancellationToken)
        {
            var webOnlyError = CheckWebOnlyLocators(action.Inputs);
            if (webOnlyError is not null)
            {
                return webOnlyError;
            }

            var expected = GetInput(action.Inputs, "text", "equals", "expected");
            if (string.IsNullOrWhiteSpace(expected))
            {
                return Failure("Flawright text assertions require a 'text', 'equals', or 'expected' input.", "invalid-assertion");
            }

            if (TryGetWindowAssertion(action, out var usesWindow))
            {
                var page = _page;
                if (page is null)
                {
                    return Failure("Flawright could not locate a window to assert against.", "window-not-found");
                }

                var actualWindowTitle = await WaitForWindowTitleAsync(page, expected, ResolveTimeoutMs(action), cancellationToken).ConfigureAwait(false);
                return ContainsOrEquals(actualWindowTitle, expected)
                    ? Success($"Window title matched '{expected}'.")
                    : Failure($"Expected window title to match '{expected}', but found '{actualWindowTitle}'.", "assertion-failed");
            }

            var locator = ResolveLocator(action, out var locatorError);
            if (locatorError is not null)
            {
                return locatorError;
            }

            if (locator is null)
            {
                return Failure("Flawright could not locate an element for text assertion.", "locator-not-found");
            }

            var actual = await WaitForLocatorTextAsync(locator, expected, ResolveTimeoutMs(action), cancellationToken).ConfigureAwait(false);
            return ContainsOrEquals(actual, expected)
                ? Success($"Element text matched '{expected}'.")
                : Failure($"Expected element text to match '{expected}', but found '{actual}'.", "assertion-failed");
        }

        private async Task<DriverExecutionResult> AssertWindowTitleAsync(PlanAction action, CancellationToken cancellationToken)
        {
            var expected = GetInput(action.Inputs, "title", "equals", "expected");
            if (string.IsNullOrWhiteSpace(expected))
            {
                return Failure("Flawright window title assertions require a 'title', 'equals', or 'expected' input.", "invalid-assertion");
            }

            var page = _page;
            if (page is null)
            {
                return Failure("Flawright could not locate the application window.", "window-not-found");
            }

            var actual = await WaitForWindowTitleAsync(page, expected, ResolveTimeoutMs(action), cancellationToken).ConfigureAwait(false);
            return ContainsOrEquals(actual, expected)
                ? Success($"Window title matched '{expected}'.")
                : Failure($"Expected window title to match '{expected}', but found '{actual}'.", "assertion-failed");
        }

        private async Task<DriverExecutionResult> PressKeyAsync(PlanAction action, CancellationToken cancellationToken)
        {
            var key = GetInput(action.Inputs, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                return Failure("Flawright press-key requires a 'key' input.", "invalid-flawright-input");
            }

            var page = _page;
            if (page is null)
            {
                return Failure("Flawright could not locate a window to send key input to.", "window-not-found");
            }

            if (TryGetInput(action.Inputs, out _, "selector"))
            {
                var locator = ResolveLocator(action, out var locatorError);
                if (locatorError is not null)
                {
                    return locatorError;
                }

                if (locator is null)
                {
                    return Failure("Flawright could not locate an element to send key input to.", "locator-not-found");
                }

                await locator.PressAsync(key, ct: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await page.Keyboard.PressAsync(key, ct: cancellationToken).ConfigureAwait(false);
            }

            return Success($"Sent key '{key}'.");
        }

        private async Task<DriverExecutionResult> CloseApplicationAsync()
        {
            if (!_ownsProcess || _ownedApp is null)
            {
                return Success("Application is already closed.");
            }

            await _ownedApp.DisposeAsync().ConfigureAwait(false);
            _ownedApp = null;
            _activeApp = null;
            _browser = null;
            _page = null;
            _ownsProcess = false;

            if (_process is not null)
            {
                _process.Dispose();
                _process = null;
            }

            return Success("Application closed.");
        }

        private async Task<DriverExecutionResult> CaptureScreenshotAsync(string name, string description, CancellationToken cancellationToken)
        {
            var page = _page;
            if (page is null)
            {
                return Failure("Flawright could not locate the application window for screenshot capture.", "window-not-found");
            }

            var fileName = $"{Sanitize(name)}-{Interlocked.Increment(ref _sequence):D3}.png";
            var relativePath = _context.EvidenceStore.MakeRelativePath("screenshots", fileName);
            var bytes = await page.ScreenshotAsync(ct: cancellationToken).ConfigureAwait(false);
            var artifact = _context.EvidenceStore.WriteFile(
                relativePath,
                path => File.WriteAllBytes(path, bytes),
                "screenshots",
                description);

            return Success(description, [artifact]);
        }

        private async Task<DriverExecutionResult> AttachFailureScreenshotAsync(PlanAction action, DriverExecutionResult result, CancellationToken cancellationToken)
        {
            if (!ShouldCaptureFailureEvidence() || _page is null)
            {
                return result;
            }

            var screenshot = await CaptureScreenshotAsync(action.Name, $"Failure screenshot for {action.Name}", cancellationToken).ConfigureAwait(false);
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

        private IFlawrightLocator? ResolveLocator(PlanAction action, out DriverExecutionResult? locatorError)
        {
            locatorError = null;
            var page = _page;
            if (page is null)
            {
                return null;
            }

            try
            {
                if (TryGetInput(action.Inputs, out var selector, "selector"))
                {
                    return page.Locator(selector);
                }

                var locators = new List<IFlawrightLocator>();
                if (TryGetInput(action.Inputs, out var automationId, "automationId", "testId"))
                {
                    locators.Add(page.GetByTestId(automationId));
                }

                if (TryGetInput(action.Inputs, out var name, "name", "label", "text"))
                {
                    locators.Add(page.Locator($"name:{name}"));
                }

                if (TryGetInput(action.Inputs, out var controlType, "controlType", "role"))
                {
                    locators.Add(page.Locator($"role:{controlType}"));
                }

                if (locators.Count == 0)
                {
                    return null;
                }

                var combined = locators[0];
                for (var index = 1; index < locators.Count; index++)
                {
                    combined = combined.And(locators[index]);
                }

                return combined;
            }
            catch (ArgumentException ex)
            {
                locatorError = Failure(ex.Message, "invalid-selector");
                return null;
            }
        }

        private DriverExecutionResult? CheckWebOnlyLocators(IReadOnlyDictionary<string, string> inputs)
        {
            var hasCssSelector = TryGetInput(inputs, out _, "cssSelector");
            var hasXPath = TryGetInput(inputs, out _, "xpath");
            if (!hasCssSelector && !hasXPath)
            {
                return null;
            }

            var hasDesktopLocator =
                TryGetInput(inputs, out _, "selector") ||
                TryGetInput(inputs, out _, "automationId", "testId") ||
                TryGetInput(inputs, out _, "name", "label", "text") ||
                TryGetInput(inputs, out _, "controlType", "role");

            if (hasDesktopLocator)
            {
                return null;
            }

            var unsupportedLocator = hasCssSelector
                ? "cssSelector"
                : "xpath";

            return Failure(
                $"The {unsupportedLocator} locator strategy is not supported by the desktop driver when no desktop locator is provided. Use selector, testId, automationId, name, label, text, role, or controlType.",
                "locator-strategy-not-supported");
        }

        private string? ResolveApplicationPath(PlanAction action)
        {
            var candidate = GetInput(action.Inputs, "application", "applicationPath", "path")
                ?? _context.EffectiveConfig.Profile.Flawright?.ApplicationPath;

            return string.IsNullOrWhiteSpace(candidate)
                ? null
                : ResolveConfiguredPath(_context.ProjectRoot, candidate);
        }

        private string? GetWindowTitle(PlanAction action)
            => GetInput(action.Inputs, "windowTitle", "title") ?? _context.EffectiveConfig.Profile.Flawright?.WindowTitle;

        private int ResolveTimeoutMs(PlanAction action)
        {
            if (TryGetInput(action.Inputs, out var timeoutValue, "timeoutMs", "timeout") && int.TryParse(timeoutValue, out var timeout))
            {
                return timeout;
            }

            return _context.EffectiveConfig.Profile.Flawright?.LaunchTimeoutMs
                ?? _context.EffectiveConfig.Profile.Timeouts?.Driver
                ?? 10000;
        }

        private FlawrightOptions BuildFlawrightOptions(int timeoutMs)
            => new()
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(timeoutMs <= 0 ? 10000 : timeoutMs),
                DefaultRetryInterval = TimeSpan.FromMilliseconds(100)
            };

        private static string[]? BuildArgumentsArray(PlanAction action)
        {
            var arguments = GetInput(action.Inputs, "arguments", "args");
            return string.IsNullOrWhiteSpace(arguments)
                ? null
                : [arguments];
        }

        private string ResolveWorkingDirectory(string executablePath)
        {
            if (!RequiresDirectFileCheck(executablePath))
            {
                return _context.ProjectRoot;
            }

            return Path.GetDirectoryName(executablePath) ?? _context.ProjectRoot;
        }

        private static async Task<IFlawrightPage> ResolvePageAsync(IFlawrightBrowser browser, string? expectedTitle, int timeoutMs, CancellationToken cancellationToken)
            => string.IsNullOrWhiteSpace(expectedTitle)
                ? await browser.NewPageAsync(cancellationToken).ConfigureAwait(false)
                : await browser.WaitForPageAsync(expectedTitle, TimeSpan.FromMilliseconds(timeoutMs <= 0 ? 10000 : timeoutMs), cancellationToken).ConfigureAwait(false);

        private static Process? TryResolveProcessFromLaunch(string executablePath)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(executablePath);
                return Process.GetProcessesByName(processName)
                    .OrderByDescending(candidate => candidate.StartTime)
                    .FirstOrDefault(candidate => !candidate.HasExited);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> ReadLocatorTextAsync(IFlawrightLocator locator, CancellationToken cancellationToken)
        {
            var text = await locator.TextContentAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            text = await locator.InnerTextAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return await locator.InputValueAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        }

        private static async Task<string> WaitForLocatorTextAsync(IFlawrightLocator locator, string expected, int timeoutMs, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs <= 0 ? 10000 : timeoutMs);
            var actual = string.Empty;
            do
            {
                actual = await ReadLocatorTextAsync(locator, cancellationToken).ConfigureAwait(false);
                if (ContainsOrEquals(actual, expected))
                {
                    return actual;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    break;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            } while (true);

            return actual;
        }

        private static async Task<string> WaitForWindowTitleAsync(IFlawrightPage page, string expected, int timeoutMs, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs <= 0 ? 10000 : timeoutMs);
            var actual = string.Empty;
            do
            {
                actual = await page.TitleAsync(cancellationToken).ConfigureAwait(false);
                if (ContainsOrEquals(actual, expected))
                {
                    return actual;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    break;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            } while (true);

            return actual;
        }

        private static bool ContainsOrEquals(string actual, string expected)
            => actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        private static bool TryGetWindowAssertion(PlanAction action, out bool usesWindow)
        {
            usesWindow = action.Inputs.TryGetValue("window", out var windowFlag)
                && bool.TryParse(windowFlag, out var isWindow)
                && isWindow;
            return usesWindow;
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
