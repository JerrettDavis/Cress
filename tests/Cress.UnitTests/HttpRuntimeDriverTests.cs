using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Execution.Drivers;

namespace Cress.UnitTests;

public sealed class HttpRuntimeDriverTests
{
    [Fact]
    public async Task ExecuteAsync_CapturesArtifacts_AndRedactsConfiguredSecrets()
    {
        using var workspace = new TestWorkspace();
        var artifactRoot = workspace.GetPath("artifacts");
        var authEnv = "CRESS_TEST_AUTH_TOKEN";
        var secretEnv = "CRESS_TEST_RESPONSE_SECRET";
        Environment.SetEnvironmentVariable(authEnv, "top-secret-token");
        Environment.SetEnvironmentVariable(secretEnv, "very-secret-value");

        HttpRequestMessage? capturedRequest = null;

        try
        {
            var profile = new CressProfile
            {
                BaseUrl = "https://example.test",
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-Profile"] = "from-profile"
                },
                Authentication = new AuthenticationConfig
                {
                    TokenEnvironmentVariable = authEnv,
                    Scheme = "Bearer"
                },
                Secrets = new SecretsConfig
                {
                    Required = [secretEnv]
                }
            };

            await using var session = await CreateSessionAsync(
                artifactRoot,
                () => new CapturingHandler(request =>
                {
                    capturedRequest = request;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"status\":\"ready\",\"secret\":\"very-secret-value\"}", Encoding.UTF8, "application/json"),
                        Headers =
                        {
                            { "X-Mode", "FAST" }
                        }
                    };
                }),
                profile);

            var result = await session.ExecuteAsync(
                CreateAction("submit", "post", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["path"] = "/orders",
                    ["body"] = "plain payload",
                    ["contentType"] = "text/plain",
                    ["header.X-Request"] = "from-action"
                }),
                CreateFlowContext(artifactRoot, profile),
                CancellationToken.None);

            Assert.Equal(RunOutcome.Passed, result.Outcome);
            Assert.NotNull(capturedRequest);
            Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
            Assert.Equal("https://example.test/orders", capturedRequest.RequestUri!.ToString());
            Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
            Assert.Equal("top-secret-token", capturedRequest.Headers.Authorization.Parameter);
            Assert.Equal("from-profile", Assert.Single(capturedRequest.Headers.GetValues("X-Profile")));
            Assert.Equal("from-action", Assert.Single(capturedRequest.Headers.GetValues("X-Request")));
            Assert.Equal("plain payload", await capturedRequest.Content!.ReadAsStringAsync());

            Assert.Equal("200", result.Outputs["statusCode"]);
            Assert.Contains("\"status\":\"ready\"", result.Outputs["body"], StringComparison.Ordinal);
            Assert.Equal(2, result.Artifacts.Count);

            var requestArtifact = await File.ReadAllTextAsync(Path.Combine(artifactRoot, result.Artifacts[0].RelativePath));
            Assert.Contains("***REDACTED***", requestArtifact, StringComparison.Ordinal);
            Assert.DoesNotContain("top-secret-token", requestArtifact, StringComparison.Ordinal);

            var responseArtifact = await File.ReadAllTextAsync(Path.Combine(artifactRoot, result.Artifacts[1].RelativePath));
            Assert.Contains("***REDACTED***", responseArtifact, StringComparison.Ordinal);
            Assert.DoesNotContain("very-secret-value", responseArtifact, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(authEnv, null);
            Environment.SetEnvironmentVariable(secretEnv, null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UsesFormContent_AndClassifiesTaskCancellationAsTimeout()
    {
        using var workspace = new TestWorkspace();
        var artifactRoot = workspace.GetPath("artifacts");

        await using var session = await CreateSessionAsync(
            artifactRoot,
            () => new ThrowingHandler(new TaskCanceledException("request timed out")));

        var flowContext = CreateFlowContext(artifactRoot);
        var result = await session.ExecuteAsync(
            CreateAction("submit-form", "post", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["path"] = "/submit",
                ["form.alpha"] = "1",
                ["form.beta"] = "two"
            }),
            flowContext,
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("timeout", result.FailureClassification);
        Assert.Single(result.Artifacts);

        var requestArtifact = await File.ReadAllTextAsync(Path.Combine(artifactRoot, result.Artifacts[0].RelativePath));
        using var requestJson = JsonDocument.Parse(requestArtifact);
        Assert.Equal("alpha=1&beta=two", requestJson.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_SupportsHttpAssertions_AfterARequest()
    {
        using var workspace = new TestWorkspace();
        var artifactRoot = workspace.GetPath("artifacts");

        await using var session = await CreateSessionAsync(
            artifactRoot,
            () => new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("{\"nested\":{\"id\":42},\"message\":\"READY now\"}", Encoding.UTF8, "application/json"),
                Headers =
                {
                    { "X-Mode", "FAST" }
                }
            }));

        var flowContext = CreateFlowContext(artifactRoot);

        var request = await session.ExecuteAsync(
            CreateAction("fetch", "get", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["url"] = "https://api.example.test/health"
            }),
            flowContext,
            CancellationToken.None);
        var status = await session.ExecuteAsync(CreateAction("status", "assert-status", new Dictionary<string, string> { ["expected"] = "202" }), flowContext, CancellationToken.None);
        var body = await session.ExecuteAsync(CreateAction("body", "assert-body-contains", new Dictionary<string, string> { ["text"] = "ready" }), flowContext, CancellationToken.None);
        var header = await session.ExecuteAsync(CreateAction("header", "assert-header", new Dictionary<string, string> { ["name"] = "X-Mode", ["equals"] = "fast" }), flowContext, CancellationToken.None);
        var json = await session.ExecuteAsync(CreateAction("json", "assert-json", new Dictionary<string, string> { ["path"] = "$.nested.id", ["equals"] = "42" }), flowContext, CancellationToken.None);
        var missingPath = await session.ExecuteAsync(CreateAction("missing-path", "assert-json", new Dictionary<string, string> { ["path"] = "$.nested.missing", ["equals"] = "42" }), flowContext, CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, request.Outcome);
        Assert.Equal(RunOutcome.Passed, status.Outcome);
        Assert.Equal(RunOutcome.Passed, body.Outcome);
        Assert.Equal(RunOutcome.Passed, header.Outcome);
        Assert.Equal(RunOutcome.Passed, json.Outcome);
        Assert.Equal(RunOutcome.Failed, missingPath.Outcome);
        Assert.Equal("assertion-failed", missingPath.FailureClassification);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsUsefulFailures_ForMissingResponse_InvalidJson_AndUnsupportedOperations()
    {
        using var workspace = new TestWorkspace();
        var artifactRoot = workspace.GetPath("artifacts");

        await using var session = await CreateSessionAsync(
            artifactRoot,
            () => new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
            }));

        var flowContext = CreateFlowContext(artifactRoot);

        var missingResponse = await session.ExecuteAsync(CreateAction("status", "assert-status", new Dictionary<string, string> { ["status"] = "200" }), flowContext, CancellationToken.None);
        var request = await session.ExecuteAsync(CreateAction("fetch", "send", new Dictionary<string, string> { ["path"] = "/plain" }), flowContext, CancellationToken.None);
        var invalidRequest = await session.ExecuteAsync(CreateAction("invalid-header", "assert-header", new Dictionary<string, string>()), flowContext, CancellationToken.None);
        var invalidJson = await session.ExecuteAsync(CreateAction("json", "assert-json", new Dictionary<string, string> { ["path"] = "$.id", ["equals"] = "1" }), flowContext, CancellationToken.None);
        var unsupported = await session.ExecuteAsync(CreateAction("custom", "trace", new Dictionary<string, string>()), flowContext, CancellationToken.None);

        Assert.Equal("missing-response", missingResponse.FailureClassification);
        Assert.Equal("invalid-assertion", invalidRequest.FailureClassification);
        Assert.Equal(RunOutcome.Passed, request.Outcome);
        Assert.Equal("invalid-json", invalidJson.FailureClassification);
        Assert.Equal("unsupported-http-operation", unsupported.FailureClassification);
    }

    private static async Task<IDriverSession> CreateSessionAsync(string artifactRoot, Func<HttpMessageHandler> handlerFactory, CressProfile? profile = null)
    {
        var evidenceStore = new EvidenceStore(artifactRoot);
        var driver = new HttpRuntimeDriver(handlerFactory);
        return await driver.StartSessionAsync(
            new DriverSessionStartContext
            {
                ProjectRoot = artifactRoot,
                FlowId = "http-driver-test",
                ArtifactRoot = artifactRoot,
                EvidenceStore = evidenceStore,
                EffectiveConfig = new EffectiveConfig
                {
                    ActiveProfile = "test",
                    Profile = profile ?? new CressProfile
                    {
                        BaseUrl = "https://example.test"
                    }
                }
            },
            CancellationToken.None);
    }

    private static FlowExecutionContext CreateFlowContext(string artifactRoot, CressProfile? profile = null)
        => new()
        {
            FlowId = "http-driver-test",
            FlowName = "HTTP Driver Test",
            ArtifactRoot = artifactRoot,
            EvidenceStore = new EvidenceStore(artifactRoot),
            EffectiveConfig = new EffectiveConfig
            {
                ActiveProfile = "test",
                Profile = profile ?? new CressProfile
                {
                    BaseUrl = "https://example.test"
                }
            }
        };

    private static PlanAction CreateAction(string name, string operation, Dictionary<string, string> inputs)
        => new()
        {
            Kind = "action",
            Name = name,
            Operation = operation,
            Driver = "http",
            Inputs = inputs
        };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }
}
