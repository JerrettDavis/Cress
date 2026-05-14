using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Cress.Companion;
using Cress.Studio.Services;

namespace Cress.Studio.Web.Tests;

public sealed class StudioCompanionClientTests
{
    [Fact]
    public async Task GetSnapshotAsync_returns_snapshot_from_configured_companion_service()
    {
        using var server = new TestCompanionServer();
        server.QueueJsonResponse(
            "/api/companion?includePreview=true",
            new CompanionServiceSnapshot
            {
                IsAvailable = true,
                GeneratedAtUtc = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero),
                Sessions =
                [
                    new CompanionSessionSnapshot
                    {
                        ProcessId = 42,
                        ProcessName = "notepad",
                        Status = CompanionSessionStatus.Recording
                    }
                ]
            });

        using var scope = new CompanionUrlScope(server.BaseAddress);
        using var client = new StudioCompanionClient();

        var snapshot = await client.GetSnapshotAsync(includePreview: true);

        Assert.True(snapshot.IsAvailable);
        Assert.Single(snapshot.Sessions);
        Assert.Equal("/api/companion?includePreview=true", server.RequestPaths.Single());
    }

    [Fact]
    public async Task ListTargetsAsync_returns_empty_list_when_service_is_unreachable()
    {
        using var scope = new CompanionUrlScope($"http://127.0.0.1:{GetUnusedPort()}");
        using var client = new StudioCompanionClient();

        var targets = await client.ListTargetsAsync();

        Assert.Empty(targets);
    }

    [Fact]
    public async Task Command_methods_post_to_expected_routes_and_deserialize_sessions()
    {
        using var server = new TestCompanionServer();
        server.QueueJsonResponse("/api/companion/sessions/101/start", new CompanionSessionSnapshot { ProcessId = 101, Status = CompanionSessionStatus.Recording });
        server.QueueJsonResponse("/api/companion/sessions/101/pause", new CompanionSessionSnapshot { ProcessId = 101, Status = CompanionSessionStatus.Paused });
        server.QueueJsonResponse("/api/companion/sessions/101/resume", new CompanionSessionSnapshot { ProcessId = 101, Status = CompanionSessionStatus.Recording });
        server.QueueJsonResponse("/api/companion/sessions/101/stop", new CompanionSessionSnapshot { ProcessId = 101, Status = CompanionSessionStatus.Stopped });

        using var scope = new CompanionUrlScope(server.BaseAddress);
        using var client = new StudioCompanionClient();

        var started = await client.StartRecordingAsync(101, overlayEnabled: false);
        var paused = await client.PauseRecordingAsync(101);
        var resumed = await client.ResumeRecordingAsync(101);
        var stopped = await client.StopRecordingAsync(101);

        Assert.Equal(CompanionSessionStatus.Recording, started.Status);
        Assert.Equal(CompanionSessionStatus.Paused, paused.Status);
        Assert.Equal(CompanionSessionStatus.Recording, resumed.Status);
        Assert.Equal(CompanionSessionStatus.Stopped, stopped.Status);
        Assert.Equal(
            [
                "/api/companion/sessions/101/start",
                "/api/companion/sessions/101/pause",
                "/api/companion/sessions/101/resume",
                "/api/companion/sessions/101/stop"
            ],
            server.RequestPaths);
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class CompanionUrlScope : IDisposable
    {
        private readonly string? _originalValue;

        public CompanionUrlScope(string baseAddress)
        {
            _originalValue = Environment.GetEnvironmentVariable("CRESS_COMPANION_URL");
            Environment.SetEnvironmentVariable("CRESS_COMPANION_URL", baseAddress);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable("CRESS_COMPANION_URL", _originalValue);
    }

    private sealed class TestCompanionServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serverLoop;
        private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);

        public TestCompanionServer()
        {
            var port = GetUnusedPort();
            BaseAddress = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseAddress);
            _listener.Start();
            _serverLoop = Task.Run(() => RunAsync(_cts.Token));
        }

        public string BaseAddress { get; }

        public List<string> RequestPaths { get; } = [];

        public void QueueJsonResponse<T>(string pathAndQuery, T response)
            => _responses[pathAndQuery] = JsonSerializer.Serialize(response);

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
            try
            {
                _serverLoop.GetAwaiter().GetResult();
            }
            catch
            {
            }
            _cts.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                var requestPath = context.Request.RawUrl ?? "/";
                RequestPaths.Add(requestPath);
                var body = _responses.TryGetValue(requestPath, out var payload) ? payload : "{}";
                var buffer = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.LongLength;
                await context.Response.OutputStream.WriteAsync(buffer, cancellationToken);
                context.Response.OutputStream.Close();
            }
        }
    }
}
