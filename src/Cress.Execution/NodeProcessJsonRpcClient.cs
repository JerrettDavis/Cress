using System.Diagnostics;
using System.Text.Json;

namespace Cress.Execution;

internal sealed class NodeProcessJsonRpcClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions WireOptions = new(ExecutionJson.Options)
    {
        WriteIndented = false
    };

    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly Task _stderrPump;
    private readonly List<string> _stderr = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _nextId;

    public NodeProcessJsonRpcClient(string scriptPath, params string[] arguments)
    {
        var nodePath = RepositoryAssetLocator.ResolveNodeExecutable();
        if (string.IsNullOrWhiteSpace(nodePath))
        {
            throw new InvalidOperationException("Node.js could not be found. Install Node.js to use Node-backed plugins and Playwright.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = nodePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start Node host '{scriptPath}'.");
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
        _stderrPump = Task.Run(async () =>
        {
            while (!_process.HasExited)
            {
                var line = await _process.StandardError.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                lock (_stderr)
                {
                    _stderr.Add(line);
                }
            }
        });
    }

    public async Task InitializeAsync(string projectRoot, string profile, CancellationToken cancellationToken)
    {
        var response = await InvokeAsync<InitializeResponse>("cress/initialize", new
        {
            protocolVersion = 1,
            projectRoot,
            profile,
            capabilities = new
            {
                supportsStreamingLogs = false,
                supportsCancellation = false
            }
        }, cancellationToken);

        if (response.ProtocolVersion != 1 || !response.Ready)
        {
            throw new InvalidOperationException("The Node host did not complete JSON-RPC initialization successfully.");
        }
    }

    public async Task<TResult> InvokeAsync<TResult>(string method, object? parameters, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var id = Interlocked.Increment(ref _nextId).ToString();
            var payload = JsonSerializer.Serialize(new JsonRpcRequest("2.0", id, method, parameters), WireOptions);
            await _stdin.WriteLineAsync(payload);
            await _stdin.FlushAsync();

            while (true)
            {
                var line = await _stdout.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    throw new InvalidOperationException(BuildTerminationMessage(method));
                }

                var response = JsonSerializer.Deserialize<JsonRpcResponse>(line, ExecutionJson.Options)
                    ?? throw new InvalidOperationException("The Node host returned an unreadable JSON-RPC response.");
                if (!string.Equals(response.Id, id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (response.Error is not null)
                {
                    throw new InvalidOperationException($"{response.Error.Message}{Environment.NewLine}{BuildStderrSummary()}".Trim());
                }

                if (response.Result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                {
                    return default!;
                }

                return response.Result.Deserialize<TResult>(ExecutionJson.Options)
                    ?? throw new InvalidOperationException($"The Node host returned an empty result for '{method}'.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _stdin.Close();
            }
        }
        catch
        {
        }

        try
        {
            if (!_process.WaitForExit(2000))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        await _stderrPump;
        _process.Dispose();
        _gate.Dispose();
    }

    private string BuildTerminationMessage(string method)
        => $"The Node host exited while handling '{method}'.{Environment.NewLine}{BuildStderrSummary()}".Trim();

    private string BuildStderrSummary()
    {
        lock (_stderr)
        {
            return _stderr.Count == 0
                ? string.Empty
                : $"stderr:{Environment.NewLine}{string.Join(Environment.NewLine, _stderr.TakeLast(20))}";
        }
    }

    private sealed record JsonRpcRequest(string Jsonrpc, string Id, string Method, object? Params);
    private sealed record JsonRpcResponse(string? Id, JsonElement Result, JsonRpcError? Error);
    private sealed record JsonRpcError(int Code, string Message);
    private sealed record InitializeResponse(int ProtocolVersion, string PluginId, IReadOnlyList<string> Capabilities, bool Ready);
}

internal static class RepositoryAssetLocator
{
    public static string? FindRepositoryAsset(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public static string ResolveNodeExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var preferred = Path.Combine(programFiles, "nodejs", "node.exe");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return "node";
    }
}
