using System.Diagnostics;
using System.Text.Json;
using Cress.Recorder;

namespace Cress.Studio.Services;

/// <summary>
/// Spawns the Node.js web recorder (<c>node bin/record.mjs --stream</c>) as a child process
/// and translates its JSON stdout stream into <see cref="RecordedEvent"/> objects.
///
/// Approach: direct child process (option a) — Studio spawns Node and reads JSONL from stdout.
/// One JSON line is emitted per event.  The C# side parses each line and raises
/// <see cref="EventCaptured"/>.  <see cref="StopAsync"/> sends SIGINT (Process.Kill on Windows)
/// and waits for the process to exit.
/// </summary>
public sealed class WebRecorderClient : IDisposable
{
    // Relative path from the solution root to the CLI entry point.
    private const string RecordMjsRelative = "node/cress-web-recorder/bin/record.mjs";

    private Process? _process;
    private Task? _readerTask;
    private readonly List<RecordedEvent> _events = [];
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Raised on a background thread for each event emitted by the Node recorder.</summary>
    public event Action<RecordedEvent>? EventCaptured;

    /// <summary>
    /// Resolves the absolute path to <c>bin/record.mjs</c> by walking up from the assembly
    /// location until the solution root (containing the <c>node/</c> directory) is found.
    /// </summary>
    private static string FindRecordMjs(string? assemblyDirectory = null)
    {
        // Try environment override first (useful in tests / CI).
        var envOverride = Environment.GetEnvironmentVariable("CRESS_RECORD_MJS");
        if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
        {
            return envOverride;
        }

        // Walk up from the assembly directory.
        var dir = assemblyDirectory ?? Path.GetDirectoryName(typeof(WebRecorderClient).Assembly.Location);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, RecordMjsRelative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate '{RecordMjsRelative}' relative to the assembly. " +
            "Set the CRESS_RECORD_MJS environment variable to the absolute path of record.mjs.");
    }

    /// <summary>
    /// Starts the Node recorder process and begins streaming events.
    /// </summary>
    /// <param name="url">Initial URL to navigate to.</param>
    /// <param name="browserType">Browser type: chromium, firefox, or webkit.</param>
    /// <param name="ct">Cancellation token (cancellation kills the child process).</param>
    public Task StartAsync(string url, string browserType, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var recordMjs = FindRecordMjs();

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            ArgumentList =
            {
                recordMjs,
                "--url", url,
                "--browser", browserType,
                "--stream",
            },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
        };

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null — Node.js may not be on PATH.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (
            ex.Message.Contains("The system cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
            ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException(
                "Node.js not found on PATH; install Node.js 18+ to enable web recording.", ex);
        }

        lock (_lock)
        {
            _process = process;
            _events.Clear();
        }

        ct.Register(() => TryKillProcess());

        // Start background reader
        _readerTask = Task.Run(() => ReadStdoutAsync(process), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Kills the Node recorder process, waits for it to exit, and returns all captured events.
    /// </summary>
    public async Task<IReadOnlyList<RecordedEvent>> StopAsync()
    {
        TryKillProcess();

        if (_readerTask is not null)
        {
            try
            {
                await _readerTask.ConfigureAwait(false);
            }
            catch
            {
                // Reader faulted (process killed) — that's expected.
            }
        }

        lock (_lock)
        {
            return _events.ToList();
        }
    }

    /// <summary>Live snapshot of events captured so far (thread-safe).</summary>
    public IReadOnlyList<RecordedEvent> CurrentEvents
    {
        get
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TryKillProcess();
        _process?.Dispose();
        _process = null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void TryKillProcess()
    {
        try
        {
            var p = _process;
            if (p is not null && !p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore — process may have already exited.
        }
    }

    private async Task ReadStdoutAsync(Process process)
    {
        var reader = process.StandardOutput;
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var evt = ParseJsonLine(line);
            if (evt is null)
            {
                continue;
            }

            lock (_lock)
            {
                _events.Add(evt);
            }

            EventCaptured?.Invoke(evt);
        }
    }

    /// <summary>
    /// Parses a single JSON line from the Node recorder into a <see cref="RecordedEvent"/>.
    /// The Node shape is:
    /// <code>
    /// { "kind": "click", "timestamp": "...", "element": { "testId": "...", "role": "...", ... }, "value": null, "key": null, "url": null }
    /// </code>
    /// Exposed as <c>public</c> so that unit tests can exercise the parser directly
    /// without spawning a Node process.
    /// </summary>
    public static RecordedEvent? ParseJsonLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var kindStr = root.TryGetProperty("kind", out var kindProp)
                ? kindProp.GetString() ?? string.Empty
                : string.Empty;

            var kind = kindStr switch
            {
                "click" or "invoke" => EventKind.Invoke,
                "fill" or "change" => EventKind.ValueChanged,
                "keypress" => EventKind.KeyDown,
                "navigate" or "submit" => EventKind.Navigate,
                _ => EventKind.Invoke,
            };

            var timestamp = root.TryGetProperty("timestamp", out var tsProp)
                ? DateTimeOffset.TryParse(tsProp.GetString(), out var ts) ? ts : DateTimeOffset.UtcNow
                : DateTimeOffset.UtcNow;

            var element = new ElementInfo();
            if (root.TryGetProperty("element", out var elProp) && elProp.ValueKind == JsonValueKind.Object)
            {
                element = new ElementInfo
                {
                    TestId = elProp.TryGetProperty("testId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                    Role = elProp.TryGetProperty("role", out p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                    Label = elProp.TryGetProperty("label", out p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                    Text = elProp.TryGetProperty("text", out p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                    Placeholder = elProp.TryGetProperty("placeholder", out p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                    CssSelector = elProp.TryGetProperty("cssSelector", out p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                    XPath = elProp.TryGetProperty("xpath", out p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                    TagName = elProp.TryGetProperty("tagName", out p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
                };
            }

            var value = root.TryGetProperty("value", out var valProp) && valProp.ValueKind != JsonValueKind.Null
                ? valProp.GetString()
                : null;

            var key = root.TryGetProperty("key", out var keyProp) && keyProp.ValueKind != JsonValueKind.Null
                ? keyProp.GetString()
                : null;

            var url = root.TryGetProperty("url", out var urlProp) && urlProp.ValueKind != JsonValueKind.Null
                ? urlProp.GetString()
                : null;

            return new RecordedEvent
            {
                Sequence = 0, // assigned by caller if needed
                Timestamp = timestamp,
                Kind = kind,
                Element = element,
                Value = value,
                Key = key,
                Url = url,
            };
        }
        catch
        {
            return null;
        }
    }
}
