using Cress.Recorder;
using Cress.Studio.Services;
using System.Diagnostics;
using System.Reflection;

namespace Cress.Studio.Web.Tests;

public sealed class WebRecorderClientProcessTests
{
    [Fact]
    public async Task StartAsync_streams_events_from_node_process_and_stop_returns_them()
    {
        using var script = TemporaryRecordScript.Create();
        using var env = new RecordMjsScope(script.ScriptPath);
        using var client = new WebRecorderClient();
        var captured = new List<string>();

        client.EventCaptured += evt => captured.Add(evt.Kind.ToString());

        await client.StartAsync("https://example.test", "chromium");
        await WaitForAsync(() => client.CurrentEvents.Count >= 2, TimeSpan.FromSeconds(10));
        var events = await client.StopAsync();

        Assert.Equal(2, events.Count);
        Assert.Equal([EventKind.Invoke, EventKind.Navigate], events.Select(evt => evt.Kind).ToArray());
        Assert.Equal(["Invoke", "Navigate"], captured);
    }

    [Fact]
    public async Task StartAsync_skips_blank_and_invalid_stdout_lines()
    {
        using var script = TemporaryRecordScript.Create("""
            console.log("");
            console.log("not-json");
            console.log(JSON.stringify({
              kind: "click",
              timestamp: "2026-05-13T12:00:00Z",
              element: { testId: "save-button", role: "button", label: "Save" }
            }));
            setInterval(() => {}, 1000);
            process.on("SIGTERM", () => process.exit(0));
            process.on("SIGINT", () => process.exit(0));
            """);
        using var env = new RecordMjsScope(script.ScriptPath);
        using var client = new WebRecorderClient();

        await client.StartAsync("https://example.test", "chromium");
        await WaitForAsync(() => client.CurrentEvents.Count == 1, TimeSpan.FromSeconds(10));
        var events = await client.StopAsync();

        Assert.Single(events);
        Assert.Equal(EventKind.Invoke, events[0].Kind);
    }

    [Fact]
    public void FindRecordMjs_walks_up_from_supplied_directory()
    {
        using var env = new RecordMjsScope(null);
        var root = Path.Combine(Path.GetTempPath(), "cress-web-recorder-find-tests", Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "bin", "Debug", "net10.0");
        var scriptPath = Path.Combine(root, "node", "cress-web-recorder", "bin", "record.mjs");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        Directory.CreateDirectory(nested);
        File.WriteAllText(scriptPath, "console.log('ready');");

        try
        {
            var resolved = Assert.IsType<string>(InvokePrivateStaticMethod(typeof(WebRecorderClient), "FindRecordMjs", nested));

            Assert.Equal(NormalizePath(scriptPath), NormalizePath(resolved));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindRecordMjs_throws_when_script_cannot_be_found()
    {
        using var env = new RecordMjsScope(null);
        var root = Path.Combine(Path.GetTempPath(), "cress-web-recorder-find-tests", Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(nested);

        try
        {
            var ex = Assert.Throws<TargetInvocationException>(() => InvokePrivateStaticMethod(typeof(WebRecorderClient), "FindRecordMjs", nested));

            Assert.Contains("Could not locate", ex.InnerException?.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_throws_helpful_error_when_node_is_not_on_path()
    {
        using var script = TemporaryRecordScript.Create();
        using var env = new RecordMjsScope(script.ScriptPath);
        using var path = new EnvironmentVariableScope("PATH", string.Empty);
        using var client = new WebRecorderClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync("https://example.test", "chromium"));

        Assert.Contains("Node.js not found on PATH", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_swallows_reader_faults_and_returns_captured_events()
    {
        using var client = new WebRecorderClient();
        SetPrivateField(client, "_readerTask", Task.FromException(new InvalidOperationException("boom")));
        SetPrivateField(client, "_events", new List<RecordedEvent>
        {
            new()
            {
                Kind = EventKind.Navigate,
                Url = "https://example.test/checkout",
                Element = new ElementInfo { Role = "document" }
            }
        });

        var events = await client.StopAsync();

        Assert.Single(events);
        Assert.Equal("https://example.test/checkout", events[0].Url);
    }

    [Fact]
    public void Dispose_swallows_process_state_exceptions()
    {
        using var client = new WebRecorderClient();
        SetPrivateField(client, "_process", new Process());

        InvokePrivateInstanceMethod(client, "TryKillProcess");
    }

    [Fact]
    public void Dispose_can_be_called_without_starting()
    {
        using var client = new WebRecorderClient();

        client.Dispose();
        client.Dispose();

        Assert.Empty(client.CurrentEvents);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for the recorder process to emit events.");
    }

    private sealed class RecordMjsScope : IDisposable
    {
        private readonly string? _originalValue;

        public RecordMjsScope(string? scriptPath)
        {
            _originalValue = Environment.GetEnvironmentVariable("CRESS_RECORD_MJS");
            Environment.SetEnvironmentVariable("CRESS_RECORD_MJS", scriptPath);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable("CRESS_RECORD_MJS", _originalValue);
    }

    private sealed class TemporaryRecordScript : IDisposable
    {
        private readonly string _rootPath;

        private TemporaryRecordScript(string rootPath, string scriptPath)
        {
            _rootPath = rootPath;
            ScriptPath = scriptPath;
        }

        public string ScriptPath { get; }

        public static TemporaryRecordScript Create(string? scriptContents = null)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "cress-web-recorder-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var scriptPath = Path.Combine(rootPath, "record.mjs");
            File.WriteAllText(scriptPath, scriptContents ?? """
                console.log(JSON.stringify({
                  kind: "click",
                  timestamp: "2026-05-13T12:00:00Z",
                  element: { testId: "save-button", role: "button", label: "Save" }
                }));
                console.log(JSON.stringify({
                  kind: "navigate",
                  timestamp: "2026-05-13T12:00:01Z",
                  url: "https://example.test/next",
                  element: { role: "document", text: "Next page" }
                }));
                setInterval(() => {}, 1000);
                process.on("SIGTERM", () => process.exit(0));
                process.on("SIGINT", () => process.exit(0));
                """);
            return new TemporaryRecordScript(rootPath, scriptPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _originalValue);
    }

    private static object? InvokePrivateInstanceMethod(object target, string methodName, params object?[]? args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(target, args);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static object? InvokePrivateStaticMethod(Type targetType, string methodName, params object?[]? args)
    {
        var method = targetType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(null, args);
    }

    private static string NormalizePath(string value)
        => Path.GetFullPath(value).Replace('/', '\\');
}
