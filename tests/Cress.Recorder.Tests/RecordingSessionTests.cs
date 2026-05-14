using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Automation;

namespace Cress.Recorder.Tests;

public sealed class RecordingSessionTests
{
    [Fact]
    public void FromProcessId_EventsAndStopReturnQueuedSnapshots()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);
        var first = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero),
            Kind = EventKind.Invoke,
            Element = new ElementInfo { ControlType = "button", AutomationId = "save-button" }
        };
        var second = new RecordedEvent
        {
            Sequence = 2,
            Timestamp = new DateTimeOffset(2026, 5, 8, 12, 0, 1, TimeSpan.Zero),
            Kind = EventKind.Navigate,
            Element = new ElementInfo { ControlType = "document", Name = "Dashboard" },
            Url = "https://example.test/dashboard"
        };

        EnqueueEvent(session, first);
        EnqueueEvent(session, second);

        var liveSnapshot = session.Events;
        var stoppedSnapshot = session.Stop();

        Assert.Equal(2, liveSnapshot.Count);
        Assert.Equal(2, stoppedSnapshot.Count);
        Assert.Equal(first, stoppedSnapshot[0]);
        Assert.Equal(second, stoppedSnapshot[1]);
    }

    [Fact]
    public void FromProcessName_AttachesToCurrentProcess()
    {
        using var currentProcess = Process.GetCurrentProcess();

        var session = RecordingSession.FromProcessName(currentProcess.ProcessName);

        Assert.NotNull(session);
        Assert.Empty(session.Events);
    }

    [Fact]
    public void FromProcessName_ThrowsWhenProcessIsMissing()
    {
        var missingProcessName = $"missing-{Guid.NewGuid():N}";

        var exception = Assert.Throws<InvalidOperationException>(() => RecordingSession.FromProcessName(missingProcessName));

        Assert.Contains(missingProcessName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_ThrowsWhenSessionHasBeenDisposed()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Start());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);

        session.Dispose();
        session.Dispose();

        Assert.Empty(session.Events);
    }

    [Fact]
    public void Start_throws_when_target_process_has_no_main_window()
    {
        using var process = StartHeadlessProcess();
        try
        {
            var session = RecordingSession.FromProcessId(process.Id);

            var exception = Assert.Throws<InvalidOperationException>(() => session.Start());

            Assert.Contains(process.Id.ToString(), exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            StopProcess(process);
        }
    }

    [Fact]
    public void Stop_disposes_configured_dispatcher_and_clears_reference()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);
        var dispatcher = new UiaEventDispatcher(
            AutomationElement.RootElement,
            AutomationElement.RootElement.Current.ProcessId,
            new ConcurrentQueue<RecordedEvent>(),
            _ => { });
        var expected = new RecordedEvent
        {
            Sequence = 1,
            Timestamp = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero),
            Kind = EventKind.Invoke,
            Element = new ElementInfo { ControlType = "button", AutomationId = "save-button" }
        };

        SetDispatcher(session, dispatcher);
        EnqueueEvent(session, expected);

        var stoppedSnapshot = session.Stop();

        Assert.Single(stoppedSnapshot);
        Assert.Equal(expected, stoppedSnapshot[0]);
        Assert.Null(GetDispatcher(session));
    }

    [Fact]
    public void Dispose_disposes_configured_dispatcher()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);
        var dispatcher = new UiaEventDispatcher(
            AutomationElement.RootElement,
            AutomationElement.RootElement.Current.ProcessId,
            new ConcurrentQueue<RecordedEvent>(),
            _ => { });

        SetDispatcher(session, dispatcher);

        session.Dispose();

        var disposedField = typeof(UiaEventDispatcher).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(disposedField);
        Assert.True((bool)disposedField.GetValue(dispatcher)!);
    }

    [Fact]
    public void ElementInfo_ToString_PrefersAutomationIdThenTestIdThenName()
    {
        Assert.Equal(
            "button[save-button]",
            new ElementInfo { ControlType = "button", AutomationId = "save-button", Name = "Save" }.ToString());
        Assert.Equal(
            "[data-testid=save-button]",
            new ElementInfo { ControlType = "button", TestId = "save-button", Name = "Save" }.ToString());
        Assert.Equal(
            "button[name='Save']",
            new ElementInfo { ControlType = "button", Name = "Save" }.ToString());
        Assert.Equal(
            "button",
            new ElementInfo { ControlType = "button" }.ToString());
    }

    [Fact]
    public void RecordedEvent_ToString_FormatsTimestampKindAndElement()
    {
        var evt = new RecordedEvent
        {
            Timestamp = new DateTimeOffset(2026, 5, 8, 12, 30, 45, 123, TimeSpan.Zero),
            Kind = EventKind.ValueChanged,
            Element = new ElementInfo { ControlType = "textbox", AutomationId = "search-box" }
        };

        Assert.Equal("[12:30:45.123] ValueChanged: textbox[search-box]", evt.ToString());
    }

    [Fact]
    public void WaitForMainWindow_returns_null_for_headless_process()
    {
        using var process = StartHeadlessProcess();
        try
        {
            var window = InvokeWaitForMainWindow(process, TimeSpan.FromMilliseconds(250));

            Assert.Null(window);
        }
        finally
        {
            StopProcess(process);
        }
    }

    [Fact]
    public void WaitForMainWindow_returns_window_for_windowed_process()
    {
        using var processHost = TemporaryWindowedProcess.Create();
        using var process = processHost.Start();
        try
        {
            var window = InvokeWaitForMainWindow(process, TimeSpan.FromSeconds(10));

            Assert.NotNull(window);
            Assert.Equal(process.Id, window.Current.ProcessId);
        }
        finally
        {
            StopProcess(process);
        }
    }

    [Fact]
    public void Start_and_stop_succeed_for_windowed_process()
    {
        using var processHost = TemporaryWindowedProcess.Create();
        using var process = processHost.Start();
        try
        {
            var session = RecordingSession.FromProcessId(process.Id);

            session.Start();
            var events = session.Stop();

            Assert.NotNull(events);
            Assert.Empty(events);
        }
        finally
        {
            StopProcess(process);
        }
    }

    private static void EnqueueEvent(RecordingSession session, RecordedEvent recordedEvent)
    {
        var queueField = typeof(RecordingSession).GetField("_events", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queueField);

        var queue = Assert.IsType<ConcurrentQueue<RecordedEvent>>(queueField.GetValue(session));
        queue.Enqueue(recordedEvent);
    }

    private static void SetDispatcher(RecordingSession session, UiaEventDispatcher dispatcher)
    {
        var dispatcherField = typeof(RecordingSession).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dispatcherField);
        dispatcherField.SetValue(session, dispatcher);
    }

    private static UiaEventDispatcher? GetDispatcher(RecordingSession session)
    {
        var dispatcherField = typeof(RecordingSession).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dispatcherField);
        return (UiaEventDispatcher?)dispatcherField.GetValue(session);
    }

    private static AutomationElement? InvokeWaitForMainWindow(Process process, TimeSpan timeout)
    {
        var method = typeof(RecordingSession).GetMethod("WaitForMainWindow", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (AutomationElement?)method.Invoke(null, [process, timeout]);
    }

    private static Process StartHeadlessProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");

        var process = Process.Start(startInfo);

        Assert.NotNull(process);
        return process;
    }

    private static void StopProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }

    private sealed class TemporaryWindowedProcess : IDisposable
    {
        private readonly string _rootPath;
        private readonly string _publishDirectory;

        private TemporaryWindowedProcess(string rootPath, string publishDirectory)
        {
            _rootPath = rootPath;
            _publishDirectory = publishDirectory;
        }

        public static TemporaryWindowedProcess Create()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "cress-recorder-windowed-tests", Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(rootPath, "src");
            var publishDirectory = Path.Combine(rootPath, "publish");
            Directory.CreateDirectory(projectDirectory);

            File.WriteAllText(Path.Combine(projectDirectory, "RecorderWindowedApp.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), """
            using System.Windows.Forms;

            ApplicationConfiguration.Initialize();
            using var form = new Form
            {
                Text = "Cress Recorder Windowed Test",
                Width = 320,
                Height = 200,
                StartPosition = FormStartPosition.CenterScreen
            };
            Application.Run(form);
            """);

            RunDotNet(projectDirectory, "publish", "RecorderWindowedApp.csproj", "-c", "Release", "-o", publishDirectory);
            return new TemporaryWindowedProcess(rootPath, publishDirectory);
        }

        public Process Start()
        {
            var executablePath = Directory.GetFiles(_publishDirectory, "RecorderWindowedApp.exe", SearchOption.TopDirectoryOnly).Single();
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = _publishDirectory,
                UseShellExecute = false
            });

            Assert.NotNull(process);
            process.WaitForInputIdle(5000);
            return process;
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }

        private static void RunDotNet(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"dotnet {string.Join(' ', arguments)} failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }
}
