using System.Diagnostics;
using Cress.Recorder;
using Cress.Recorder.Inference;
using Cress.Recorder.Serialization;
using Flawright;
using FlawrightClient = Flawright.Flawright;

// ---- argument parsing ----
string? launchExe = null;
int attachPid = 0;
bool autoDemo = false;
string? recordAndSavePath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--launch" when i + 1 < args.Length:
            launchExe = args[++i];
            break;
        case "--attach" when i + 1 < args.Length:
            attachPid = int.Parse(args[++i]);
            break;
        case "--auto-demo":
            autoDemo = true;
            break;
        case "--record-and-save" when i + 1 < args.Length:
            recordAndSavePath = args[++i];
            autoDemo = true; // record-and-save implies auto-demo
            break;
    }
}

// ---- launch or attach ----
if (!string.IsNullOrWhiteSpace(launchExe))
{
    Console.WriteLine($"[PoC] Launching: {launchExe}");
    Process.Start(new ProcessStartInfo { FileName = launchExe, UseShellExecute = true });

    Console.WriteLine("[PoC] Waiting 3s for Calculator window to appear...");
    Thread.Sleep(3000);

    // Windows Calculator is a UWP app hosted by ApplicationFrameHost.
    // Find the process that has a window titled "Calculator".
    var host = Process.GetProcesses()
        .FirstOrDefault(p =>
        {
            try { return !p.HasExited && p.MainWindowTitle.Contains("Calculator", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });

    if (host is null)
    {
        // Fallback: find CalculatorApp process
        host = Process.GetProcessesByName("CalculatorApp").FirstOrDefault(p => !p.HasExited);
    }

    if (host is null)
    {
        Console.Error.WriteLine("[PoC] ERROR: Could not locate Calculator window/process after launch.");
        return 1;
    }

    attachPid = host.Id;
    Console.WriteLine($"[PoC] Attaching to '{host.ProcessName}' (PID {attachPid}) — Window: '{host.MainWindowTitle}'");
}

// For --record-and-save without explicit --launch or --attach, look for a running Calculator
if (attachPid == 0 && recordAndSavePath is not null)
{
    Console.WriteLine("[PoC] --record-and-save: looking for running Calculator...");

    // Try to find running Calculator
    var calcProcess = Process.GetProcesses()
        .FirstOrDefault(p =>
        {
            try { return !p.HasExited && p.MainWindowTitle.Contains("Calculator", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        })
        ?? Process.GetProcessesByName("CalculatorApp").FirstOrDefault(p => !p.HasExited);

    if (calcProcess is null)
    {
        // Launch it
        Console.WriteLine("[PoC] Calculator not found. Launching calc.exe...");
        Process.Start(new ProcessStartInfo { FileName = "calc.exe", UseShellExecute = true });
        Console.WriteLine("[PoC] Waiting 4s for Calculator window...");
        Thread.Sleep(4000);

        calcProcess = Process.GetProcesses()
            .FirstOrDefault(p =>
            {
                try { return !p.HasExited && p.MainWindowTitle.Contains("Calculator", StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            })
            ?? Process.GetProcessesByName("CalculatorApp").FirstOrDefault(p => !p.HasExited);
    }

    if (calcProcess is null)
    {
        Console.Error.WriteLine("[PoC] ERROR: Could not locate Calculator window/process.");
        return 1;
    }

    attachPid = calcProcess.Id;
    Console.WriteLine($"[PoC] Using Calculator: '{calcProcess.ProcessName}' (PID {attachPid}) — Window: '{calcProcess.MainWindowTitle}'");
}

if (attachPid == 0)
{
    Console.Error.WriteLine("Usage: Cress.Recorder.PoC --launch <exe> | --attach <pid> [--auto-demo]");
    Console.Error.WriteLine("       Cress.Recorder.PoC --record-and-save <output-flow-yaml-path>");
    return 1;
}

// ---- recording ----
using var session = RecordingSession.FromProcessId(attachPid);

int eventCount = 0;
session.EventCaptured += evt =>
{
    Interlocked.Increment(ref eventCount);
    var valueStr = evt.Value is not null ? $" value='{evt.Value}'" : string.Empty;
    Console.WriteLine($"[{evt.Timestamp:HH:mm:ss.fff}] {evt.Kind,-14} {evt.Element,-45}{valueStr}");
};

Console.WriteLine("[PoC] Starting session...");
try
{
    session.Start();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[PoC] ERROR starting session: {ex.Message}");
    Console.Error.WriteLine($"[PoC] Inner: {ex.InnerException?.Message}");
    return 1;
}

Console.WriteLine("[PoC] Recording... press Ctrl+C to stop (or wait for auto-demo).");
Console.WriteLine();

// ---- optional auto-demo: drive Calculator via Flawright ----
if (autoDemo)
{
    Console.WriteLine("[PoC] Auto-demo mode: driving Calculator via Flawright in 1s...");
    Thread.Sleep(1000);

    try
    {
        await using var app = await FlawrightClient.AttachAsync(
            new AttachOptions { ProcessId = attachPid },
            new FlawrightOptions
            {
                DefaultTimeout = TimeSpan.FromSeconds(5),
                DefaultRetryInterval = TimeSpan.FromMilliseconds(100)
            });

        var calcWindow = await app.Browser.WaitForPageAsync("Calculator", TimeSpan.FromSeconds(5));
        if (calcWindow is null)
        {
            Console.WriteLine("[PoC] WARNING: Could not get Calculator window for auto-demo.");
        }
        else
        {
            Console.WriteLine($"[PoC] Got window: '{await calcWindow.TitleAsync()}'");
            await DriveCalculatorAsync(calcWindow);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PoC] Auto-demo error: {ex.Message}");
        Console.WriteLine($"[PoC]   Inner: {ex.InnerException?.Message}");
    }

    // Let events settle then exit automatically
    Console.WriteLine("[PoC] Auto-demo complete. Waiting 2s for events to settle...");
    Thread.Sleep(2000);
}
else
{
    // Wait for Ctrl+C
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    try { await Task.Delay(Timeout.Infinite, cts.Token); }
    catch (OperationCanceledException) { }
}

// ---- stop and report ----
var captured = session.Stop();
Console.WriteLine();
Console.WriteLine($"[PoC] Session stopped. Total events captured: {captured.Count}");

if (captured.Count > 0)
{
    Console.WriteLine("[PoC] --- Event Summary ---");
    var groups = captured.GroupBy(e => e.Kind);
    foreach (var g in groups.OrderBy(g => g.Key.ToString()))
    {
        Console.WriteLine($"  {g.Key}: {g.Count()}");
    }

    Console.WriteLine();
    Console.WriteLine("[PoC] --- First 20 Events ---");
    foreach (var evt in captured.Take(20))
    {
        var valueStr = evt.Value is not null ? $" value='{evt.Value}'" : string.Empty;
        Console.WriteLine($"  [{evt.Timestamp:HH:mm:ss.fff}] {evt.Kind,-14} {evt.Element}{valueStr}");
    }
}
else
{
    Console.WriteLine("[PoC] WARNING: No events were captured.");
    Console.WriteLine("      Possible causes:");
    Console.WriteLine("      - Calculator UWP uses ApplicationFrameHost; attach to CalculatorApp PID instead");
    Console.WriteLine("      - UAC elevation mismatch (run as admin if Calculator runs elevated)");
    Console.WriteLine("      - UIA events may need --attach to CalculatorApp PID rather than ApplicationFrameHost PID");
}

// ---- record-and-save: run inference + serialize ----
if (recordAndSavePath is not null)
{
    Console.WriteLine();
    Console.WriteLine($"[PoC] --record-and-save: running inference engine...");

    var engine = new StepInferenceEngine();
    var options = new InferenceOptions
    {
        IgnoreFocusEvents = true,
        AssertionTargetAutomationId = "CalculatorResults",
        DebounceWindow = TimeSpan.FromMilliseconds(50),
    };

    var inferredSteps = engine.Infer(captured, options);
    Console.WriteLine($"[PoC] Inferred {inferredSteps.Count} step(s):");
    foreach (var step in inferredSteps)
    {
        Console.WriteLine($"  {step}");
    }

    // Ensure at least a minimal set of steps for a valid flow
    if (inferredSteps.Count == 0)
    {
        Console.WriteLine("[PoC] WARNING: No steps inferred from captured events. The saved YAML will contain placeholder steps.");
    }

    // The AttachProcessName causes the serializer to prepend a ui.attach step.
    // Windows Calculator (UWP) is hosted by ApplicationFrameHost on the Win32 side.
    // Flawright attach against the window-host process successfully exposes the
    // Calculator UIA-backed page, which the Flawright driver uses for element
    // search. Attaching to CalculatorApp directly may not yield a visible window because
    // the HWND belongs to ApplicationFrameHost.
    // Strategy: prefer the process that actually has the Calculator window title.
    var attachProcess = "ApplicationFrameHost"; // safe default that actually has the window
    var hostWithCalc = Process.GetProcesses()
        .FirstOrDefault(p =>
        {
            try { return !p.HasExited && p.MainWindowTitle.Contains("Calculator", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });
    if (hostWithCalc is not null)
    {
        attachProcess = hostWithCalc.ProcessName;
    }

    var metadata = new RecordedFlowSerializer.RecordedFlowMetadata
    {
        Id = "calc.add-two-plus-two",
        Name = "Calculator: 2 + 2 = 4",
        Capability = "calc-arithmetic",
        Summary = "Verifies that Calculator correctly computes 2 + 2 = 4 and displays the result.",
        Tags = ["recorded", "draft", "calculator"],
        Status = "draft",
        AttachProcessName = attachProcess,
    };

    Console.WriteLine($"[PoC] Will attach to process: '{attachProcess}'");

    var serializer = new RecordedFlowSerializer();

    string flowYaml;
    if (inferredSteps.Count == 0)
    {
        // Synthesize a minimal valid flow with placeholder steps
        var placeholderSteps = new List<InferredStep>
        {
            new InferredStep
            {
                Kind = StepKind.Click,
                Locator = new Locator { AutomationId = "num2Button", ControlType = "Button" },
                SourceTimestamp = DateTime.UtcNow,
            },
            new InferredStep
            {
                Kind = StepKind.Click,
                Locator = new Locator { AutomationId = "plusButton", ControlType = "Button" },
                SourceTimestamp = DateTime.UtcNow.AddMilliseconds(100),
            },
            new InferredStep
            {
                Kind = StepKind.Click,
                Locator = new Locator { AutomationId = "num2Button", ControlType = "Button" },
                SourceTimestamp = DateTime.UtcNow.AddMilliseconds(200),
            },
            new InferredStep
            {
                Kind = StepKind.Click,
                Locator = new Locator { AutomationId = "equalButton", ControlType = "Button" },
                SourceTimestamp = DateTime.UtcNow.AddMilliseconds(300),
            },
            new InferredStep
            {
                Kind = StepKind.AssertText,
                Locator = new Locator { AutomationId = "CalculatorResults" },
                Value = "Display is 4",
                SourceTimestamp = DateTime.UtcNow.AddMilliseconds(400),
            },
        };
        Console.WriteLine("[PoC] Using placeholder steps for demonstration.");
        flowYaml = serializer.Serialize(placeholderSteps, metadata);
    }
    else
    {
        flowYaml = serializer.Serialize(inferredSteps, metadata);
    }

    Console.WriteLine();
    Console.WriteLine("[PoC] Serialized flow YAML:");
    Console.WriteLine("---");
    Console.WriteLine(flowYaml);
    Console.WriteLine("---");

    try
    {
        var outputPath = Path.GetFullPath(recordAndSavePath);
        var outputDir = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(outputPath, flowYaml, System.Text.Encoding.UTF8);
        Console.WriteLine($"[PoC] Flow saved to: {outputPath}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[PoC] ERROR saving flow: {ex.Message}");
        return 1;
    }
}

return 0;

// ---- Calculator driver ----
static async Task DriveCalculatorAsync(IFlawrightPage window)
{
    static async Task ClickByIdAsync(IFlawrightPage window, string id)
    {
        await window.ClickAsync($"#{id}");
        Thread.Sleep(300); // brief pause so UIA events fire before the next click
        Console.WriteLine($"[PoC]   Clicked: {id}");
    }

    // Clear first in case Calculator has state
    try { await ClickByIdAsync(window, "clearButton"); Thread.Sleep(300); } catch { /* might not be visible */ }

    // Type: 2 + 2 =
    Console.WriteLine("[PoC] Clicking: 2 + 2 =");
    await ClickByIdAsync(window, "num2Button");
    await ClickByIdAsync(window, "plusButton");
    await ClickByIdAsync(window, "num2Button");
    await ClickByIdAsync(window, "equalButton");

    Thread.Sleep(500);
    Console.WriteLine("[PoC] Drive sequence complete: 2 + 2 = ");

    // Read the result display
    try
    {
        var result = window.Locator("#CalculatorResults");
        Console.WriteLine($"[PoC] Calculator display: '{await result.TextContentAsync() ?? await result.InnerTextAsync()}'");
    }
    catch { }
}
