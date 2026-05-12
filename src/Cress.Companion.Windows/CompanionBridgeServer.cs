using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Cress.Companion.Windows;

internal sealed class CompanionBridgeServer : IAsyncDisposable, IDisposable
{
    private readonly DesktopCompanionCoordinator _coordinator;
    private readonly int _port;
    private WebApplication? _application;

    public CompanionBridgeServer(DesktopCompanionCoordinator coordinator, int port)
    {
        _coordinator = coordinator;
        _port = port;
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
    }

    public Uri BaseAddress { get; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(BaseAddress.ToString());

        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/companion", (bool includePreview) => _coordinator.GetSnapshot(includePreview));
        app.MapGet("/api/companion/targets", async () => await _coordinator.ListTargetsAsync());
        app.MapPost("/api/companion/sessions/{processId:int}/start", async (int processId, StartSessionRequest? request) =>
            Results.Ok(await _coordinator.StartRecordingAsync(processId, request?.OverlayEnabled ?? true)));
        app.MapPost("/api/companion/sessions/{processId:int}/pause", async (int processId) =>
            Results.Ok(await _coordinator.PauseRecordingAsync(processId)));
        app.MapPost("/api/companion/sessions/{processId:int}/resume", async (int processId) =>
            Results.Ok(await _coordinator.ResumeRecordingAsync(processId)));
        app.MapPost("/api/companion/sessions/{processId:int}/stop", async (int processId) =>
            Results.Ok(await _coordinator.StopRecordingAsync(processId)));
        app.MapPost("/api/companion/sessions/{processId:int}/overlay", async (int processId, OverlayRequest request) =>
            Results.Ok(await _coordinator.SetOverlayEnabledAsync(processId, request.Enabled)));

        await app.StartAsync(cancellationToken);
        _application = app;
    }

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_application is null)
        {
            return;
        }

        await _application.StopAsync();
        await _application.DisposeAsync();
        _application = null;
    }

    private sealed record StartSessionRequest(bool OverlayEnabled = true);

    private sealed record OverlayRequest(bool Enabled);
}
