using Cress.Core.Models;

namespace Cress.Execution.Drivers;

public sealed class PlaywrightRuntimeDriver : IRuntimeDriver
{
    private static readonly string? HostScriptPath = RepositoryAssetLocator.FindRepositoryAsset(Path.Combine("node", "cress-driver-playwright", "host.js"));
    private static readonly string? InstalledPlaywrightPackage = RepositoryAssetLocator.FindRepositoryAsset(Path.Combine("node_modules", "playwright", "package.json"))
        ?? RepositoryAssetLocator.FindRepositoryAsset(Path.Combine("node", "cress-driver-playwright", "node_modules", "playwright", "package.json"));

    public string Name => "playwright";

    public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog)
        => HostScriptPath is null || InstalledPlaywrightPackage is null
            ?
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "DRV001",
                    Message = HostScriptPath is null
                        ? "The in-repo Playwright host script is missing."
                        : "Playwright dependencies are not installed. Run npm install from the repository root and install the required browser binaries.",
                    File = HostScriptPath ?? Path.Combine(catalog.ProjectRoot, ".cress", "profiles", $"{catalog.EffectiveConfig.ActiveProfile}.yaml")
                }
            ]
            : [];

    public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
        => Task.FromResult<IDriverSession>(new PlaywrightDriverSession(context));

    private sealed class PlaywrightDriverSession : IDriverSession
    {
        private readonly DriverSessionStartContext _context;
        private readonly NodeProcessJsonRpcClient _client;
        private readonly string _sessionId;
        private readonly IReadOnlyDictionary<string, string> _metadata;

        public PlaywrightDriverSession(DriverSessionStartContext context)
        {
            _context = context;
            if (HostScriptPath is null)
            {
                throw new InvalidOperationException("The in-repo Playwright host script is missing.");
            }

            _client = new NodeProcessJsonRpcClient(HostScriptPath);
            _client.InitializeAsync(context.ProjectRoot, context.EffectiveConfig.ActiveProfile, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var response = _client.InvokeAsync<StartSessionResponse>("driver/startSession", new
            {
                flowId = context.FlowId,
                artifactRoot = context.ArtifactRoot,
                profile = new
                {
                    baseUrl = context.EffectiveConfig.Profile.BaseUrl,
                    playwright = new
                    {
                        browser = context.EffectiveConfig.Profile.Playwright?.Browser,
                        headless = context.EffectiveConfig.Profile.Playwright?.Headless
                    },
                    evidence = new
                    {
                        mode = context.EffectiveConfig.Profile.Evidence?.Mode,
                        screenshotPolicy = context.EffectiveConfig.Profile.Evidence?.ScreenshotPolicy
                    },
                    timeouts = new
                    {
                        driver = context.EffectiveConfig.Profile.Timeouts?.Driver
                    }
                }
            }, CancellationToken.None).GetAwaiter().GetResult();
            _sessionId = response.SessionId;
            _metadata = response.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Name => "playwright";

        public IReadOnlyDictionary<string, string> Metadata => _metadata;

        public async Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
        {
            var response = await _client.InvokeAsync<DriverActionResponse>("driver/perform", new
            {
                sessionId = _sessionId,
                action = new
                {
                    name = action.Name,
                    operation = action.Operation,
                    inputs = action.Inputs
                }
            }, cancellationToken);

            return new DriverExecutionResult
            {
                Outcome = string.Equals(response.Status, "passed", StringComparison.OrdinalIgnoreCase) ? RunOutcome.Passed : RunOutcome.Failed,
                Message = response.Message,
                FailureClassification = response.FailureClassification,
                Artifacts = (response.Artifacts ?? [])
                    .Select(artifact => new EvidenceArtifact
                    {
                        Category = artifact.Category ?? "playwright",
                        RelativePath = artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar),
                        Description = artifact.Description
                    })
                    .ToList()
            };
        }

        public async Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
        {
            var response = await _client.InvokeAsync<CaptureEvidenceResponse>("driver/captureEvidence", new
            {
                sessionId = _sessionId
            }, cancellationToken);
            return (response.Artifacts ?? [])
                .Select(artifact => new EvidenceArtifact
                {
                    Category = artifact.Category ?? "playwright",
                    RelativePath = artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar),
                    Description = artifact.Description
                })
                .ToList();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _client.InvokeAsync<object>("driver/stopSession", new { sessionId = _sessionId }, CancellationToken.None);
                await _client.InvokeAsync<object>("cress/shutdown", new { }, CancellationToken.None);
            }
            catch
            {
            }

            await _client.DisposeAsync();
        }

        private sealed record StartSessionResponse(string SessionId, Dictionary<string, string>? Metadata);
        private sealed record DriverActionResponse(string Status, string? Message, string? FailureClassification, List<DriverArtifact>? Artifacts);
        private sealed record CaptureEvidenceResponse(List<DriverArtifact>? Artifacts);
        private sealed record DriverArtifact(string? Category, string RelativePath, string? Description);
    }
}
