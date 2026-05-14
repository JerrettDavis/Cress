using Cress.Recorder;
using Cress.Studio.Services;

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

        public RecordMjsScope(string scriptPath)
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

        public static TemporaryRecordScript Create()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "cress-web-recorder-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var scriptPath = Path.Combine(rootPath, "record.mjs");
            File.WriteAllText(scriptPath, """
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
}
