using System.Net.Http.Json;
using Cress.Companion;

namespace Cress.Studio.Services;

public sealed class StudioCompanionClient : IStudioCompanionClient, IDisposable
{
    private readonly HttpClient _httpClient;

    public StudioCompanionClient()
    {
        var baseUrl = Environment.GetEnvironmentVariable("CRESS_COMPANION_URL")?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://127.0.0.1:7321/";
        }
        else if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<CompanionServiceSnapshot> GetSnapshotAsync(bool includePreview = false, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<CompanionServiceSnapshot>(
                       $"api/companion?includePreview={includePreview.ToString().ToLowerInvariant()}",
                       cancellationToken)
                   ?? CreateUnavailableSnapshot();
        }
        catch
        {
            return CreateUnavailableSnapshot();
        }
    }

    public async Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<CompanionTargetInfo>>("api/companion/targets", cancellationToken)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    public Task<CompanionSessionSnapshot> StartRecordingAsync(int processId, bool overlayEnabled = true, CancellationToken cancellationToken = default)
        => SendCommandAsync<StartSessionRequest, CompanionSessionSnapshot>(
            $"api/companion/sessions/{processId}/start",
            new StartSessionRequest(overlayEnabled),
            cancellationToken);

    public Task<CompanionSessionSnapshot> PauseRecordingAsync(int processId, CancellationToken cancellationToken = default)
        => SendCommandAsync<object, CompanionSessionSnapshot>(
            $"api/companion/sessions/{processId}/pause",
            new { },
            cancellationToken);

    public Task<CompanionSessionSnapshot> ResumeRecordingAsync(int processId, CancellationToken cancellationToken = default)
        => SendCommandAsync<object, CompanionSessionSnapshot>(
            $"api/companion/sessions/{processId}/resume",
            new { },
            cancellationToken);

    public Task<CompanionSessionSnapshot> StopRecordingAsync(int processId, CancellationToken cancellationToken = default)
        => SendCommandAsync<object, CompanionSessionSnapshot>(
            $"api/companion/sessions/{processId}/stop",
            new { },
            cancellationToken);

    public void Dispose()
        => _httpClient.Dispose();

    private async Task<TResponse> SendCommandAsync<TRequest, TResponse>(string url, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException($"Desktop companion returned an empty response for '{url}'.");
    }

    private static CompanionServiceSnapshot CreateUnavailableSnapshot()
        => new()
        {
            IsAvailable = false,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Sessions = []
        };

    private sealed record StartSessionRequest(bool OverlayEnabled);
}
