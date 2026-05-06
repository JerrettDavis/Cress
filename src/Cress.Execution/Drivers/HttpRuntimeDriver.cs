using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cress.Core.Models;

namespace Cress.Execution.Drivers;

public sealed class HttpRuntimeDriver : IRuntimeDriver
{
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    public HttpRuntimeDriver(Func<HttpMessageHandler>? handlerFactory = null)
    {
        _handlerFactory = handlerFactory;
    }

    public string Name => "http";

    public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog) => [];

    public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
    {
        var client = _handlerFactory is null ? new HttpClient() : new HttpClient(_handlerFactory(), disposeHandler: true);
        return Task.FromResult<IDriverSession>(new HttpDriverSession(client, context));
    }

    private sealed class HttpDriverSession : IDriverSession
    {
        private readonly HttpClient _client;
        private readonly DriverSessionStartContext _context;
        private int _sequence;
        private HttpResponseSnapshot? _lastResponse;

        public HttpDriverSession(HttpClient client, DriverSessionStartContext context)
        {
            _client = client;
            _context = context;
        }

        public string Name => "http";

        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "built-in"
        };

        public async Task<DriverExecutionResult> ExecuteAsync(PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
        {
            var operation = (action.Operation ?? action.Name).Trim().ToLowerInvariant();
            return operation switch
            {
                "request" or "send" or "get" or "post" or "put" or "patch" or "delete" => await SendRequestAsync(operation, action, context, cancellationToken),
                "assert-status" or "assertstatus" => AssertStatus(action),
                "assert-json" or "assertjson" => AssertJson(action),
                "assert-body-contains" or "assertbodycontains" => AssertBodyContains(action),
                "assert-header" or "assertheader" => AssertHeader(action),
                _ => new DriverExecutionResult
                {
                    Outcome = RunOutcome.Failed,
                    Message = $"HTTP operation '{action.Operation}' is not supported.",
                    FailureClassification = "unsupported-http-operation"
                }
            };
        }

        public Task<IReadOnlyList<EvidenceArtifact>> CaptureFinalEvidenceAsync(FlowExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<EvidenceArtifact>>([]);

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }

        private async Task<DriverExecutionResult> SendRequestAsync(string operation, PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
        {
            var method = action.Inputs.TryGetValue("method", out var declaredMethod)
                ? new HttpMethod(declaredMethod.ToUpperInvariant())
                : new HttpMethod(operation.Equals("request", StringComparison.OrdinalIgnoreCase) || operation.Equals("send", StringComparison.OrdinalIgnoreCase)
                    ? "GET"
                    : operation.ToUpperInvariant());
            var target = ResolveUrl(action, context);
            var request = new HttpRequestMessage(method, target);

            ApplyHeaders(request, action, context);

            if (action.Inputs.TryGetValue("json", out var jsonBody))
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }
            else if (action.Inputs.TryGetValue("body", out var body))
            {
                request.Content = new StringContent(body, Encoding.UTF8, action.Inputs.TryGetValue("contentType", out var contentType) ? contentType : "text/plain");
            }
            else
            {
                var formValues = action.Inputs
                    .Where(entry => entry.Key.StartsWith("form.", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(entry => entry.Key["form.".Length..], entry => entry.Value, StringComparer.OrdinalIgnoreCase);
                if (formValues.Count > 0)
                {
                    request.Content = new FormUrlEncodedContent(formValues);
                }
            }

            var requestArtifact = CaptureRequestEvidence(request, action, context);

            try
            {
                var response = await _client.SendAsync(request, cancellationToken);
                var responseBody = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
                _lastResponse = new HttpResponseSnapshot((int)response.StatusCode, responseBody, response.Headers, response.Content?.Headers);
                var responseArtifact = CaptureResponseEvidence(response, responseBody, action, context);

                return new DriverExecutionResult
                {
                    Outcome = RunOutcome.Passed,
                    Message = $"{method} {target} returned {(int)response.StatusCode}.",
                    Outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["statusCode"] = ((int)response.StatusCode).ToString(),
                        ["body"] = responseBody
                    },
                    Artifacts = [requestArtifact, responseArtifact]
                };
            }
            catch (Exception ex)
            {
                return new DriverExecutionResult
                {
                    Outcome = RunOutcome.Failed,
                    Message = ex.Message,
                    FailureClassification = ex is TaskCanceledException ? "timeout" : "http-request-failed",
                    Artifacts = [requestArtifact]
                };
            }
        }

        private static string ResolveUrl(PlanAction action, FlowExecutionContext context)
        {
            if (action.Inputs.TryGetValue("url", out var explicitUrl) && Uri.TryCreate(explicitUrl, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }

            var baseUrl = context.EffectiveConfig.Profile.BaseUrl ?? "http://localhost";
            var path = action.Inputs.TryGetValue("path", out var value) ? value : "/";
            return Uri.TryCreate(path, UriKind.Absolute, out absolute)
                ? absolute.ToString()
                : new Uri(new Uri(baseUrl, UriKind.Absolute), path).ToString();
        }

        private static void ApplyHeaders(HttpRequestMessage request, PlanAction action, FlowExecutionContext context)
        {
            foreach (var header in context.EffectiveConfig.Profile.Headers ?? new Dictionary<string, string>())
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var header in action.Inputs.Where(entry => entry.Key.StartsWith("header.", StringComparison.OrdinalIgnoreCase)))
            {
                request.Headers.TryAddWithoutValidation(header.Key["header.".Length..], header.Value);
            }

            var authentication = context.EffectiveConfig.Profile.Authentication;
            if (authentication is null)
            {
                return;
            }

            var headerName = authentication.Header ?? "Authorization";
            if (request.Headers.Contains(headerName))
            {
                return;
            }

            var token = authentication.Token;
            if (string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(authentication.TokenEnvironmentVariable))
            {
                token = Environment.GetEnvironmentVariable(authentication.TokenEnvironmentVariable);
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            if (headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(authentication.Scheme ?? "Bearer", token);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(headerName, $"{authentication.Scheme} {token}".Trim());
            }
        }

        private EvidenceArtifact CaptureRequestEvidence(HttpRequestMessage request, PlanAction action, FlowExecutionContext context)
        {
            var relativePath = _context.EvidenceStore.MakeRelativePath("api", $"{++_sequence:D3}-{action.Name}-request.json");
            return _context.EvidenceStore.WriteJson(relativePath, new
            {
                method = request.Method.Method,
                url = request.RequestUri?.ToString(),
                headers = RedactHeaders(request.Headers),
                body = request.Content is null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }, "api", "HTTP request");
        }

        private EvidenceArtifact CaptureResponseEvidence(HttpResponseMessage response, string body, PlanAction action, FlowExecutionContext context)
        {
            var relativePath = _context.EvidenceStore.MakeRelativePath("api", $"{_sequence:D3}-{action.Name}-response.json");
            return _context.EvidenceStore.WriteJson(relativePath, new
            {
                statusCode = (int)response.StatusCode,
                reasonPhrase = response.ReasonPhrase,
                headers = RedactHeaders(response.Headers.Concat(response.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())),
                body = RedactSecrets(body, context)
            }, "api", "HTTP response");
        }

        private DriverExecutionResult AssertStatus(PlanAction action)
        {
            if (_lastResponse is null)
            {
                return Failure("No HTTP response is available to assert.", "missing-response");
            }

            if (!action.Inputs.TryGetValue("status", out var expectedValue) && !action.Inputs.TryGetValue("expected", out expectedValue))
            {
                return Failure("HTTP status assertion requires a 'status' or 'expected' input.", "invalid-assertion");
            }

            return int.TryParse(expectedValue, out var expected) && expected == _lastResponse.StatusCode
                ? Success($"HTTP status matched {expected}.")
                : Failure($"Expected HTTP status {expectedValue}, but received {_lastResponse.StatusCode}.", "assertion-failed");
        }

        private DriverExecutionResult AssertJson(PlanAction action)
        {
            if (_lastResponse is null)
            {
                return Failure("No HTTP response is available to assert.", "missing-response");
            }

            if (!action.Inputs.TryGetValue("path", out var path) || !action.Inputs.TryGetValue("equals", out var expected))
            {
                return Failure("JSON assertion requires 'path' and 'equals' inputs.", "invalid-assertion");
            }

            try
            {
                using var document = JsonDocument.Parse(_lastResponse.Body);
                if (!TryResolveJsonPath(document.RootElement, path, out var element))
                {
                    return Failure($"JSON path '{path}' was not found.", "assertion-failed");
                }

                var actual = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                return string.Equals(actual, expected, StringComparison.Ordinal)
                    ? Success($"JSON path '{path}' matched.")
                    : Failure($"Expected JSON path '{path}' to equal '{expected}', but found '{actual}'.", "assertion-failed");
            }
            catch (JsonException ex)
            {
                return Failure($"Response body is not valid JSON: {ex.Message}", "invalid-json");
            }
        }

        private DriverExecutionResult AssertBodyContains(PlanAction action)
        {
            if (_lastResponse is null)
            {
                return Failure("No HTTP response is available to assert.", "missing-response");
            }

            if (!action.Inputs.TryGetValue("text", out var expected))
            {
                return Failure("Body assertion requires a 'text' input.", "invalid-assertion");
            }

            return _lastResponse.Body.Contains(expected, StringComparison.OrdinalIgnoreCase)
                ? Success($"Response body contains '{expected}'.")
                : Failure($"Response body did not contain '{expected}'.", "assertion-failed");
        }

        private DriverExecutionResult AssertHeader(PlanAction action)
        {
            if (_lastResponse is null)
            {
                return Failure("No HTTP response is available to assert.", "missing-response");
            }

            if (!action.Inputs.TryGetValue("name", out var headerName) || !action.Inputs.TryGetValue("equals", out var expected))
            {
                return Failure("Header assertion requires 'name' and 'equals' inputs.", "invalid-assertion");
            }

            if (!_lastResponse.Headers.TryGetValue(headerName, out var actual))
            {
                return Failure($"Response header '{headerName}' was not found.", "assertion-failed");
            }

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                ? Success($"Header '{headerName}' matched.")
                : Failure($"Expected header '{headerName}' to equal '{expected}', but found '{actual}'.", "assertion-failed");
        }

        private static Dictionary<string, string> RedactHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
            => headers.ToDictionary(
                header => header.Key,
                header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ? "***REDACTED***" : string.Join(", ", header.Value),
                StringComparer.OrdinalIgnoreCase);

        private static string RedactSecrets(string value, FlowExecutionContext context)
        {
            var redacted = value;
            foreach (var secretName in context.EffectiveConfig.Profile.Secrets?.Required ?? [])
            {
                var secretValue = Environment.GetEnvironmentVariable(secretName);
                if (!string.IsNullOrWhiteSpace(secretValue))
                {
                    redacted = redacted.Replace(secretValue, "***REDACTED***", StringComparison.Ordinal);
                }
            }

            return redacted;
        }

        private static bool TryResolveJsonPath(JsonElement element, string path, out JsonElement result)
        {
            result = element;
            var normalized = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$');
            foreach (var segment in normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(segment, out result))
                {
                    return false;
                }
            }

            return true;
        }

        private static DriverExecutionResult Success(string message) => new()
        {
            Outcome = RunOutcome.Passed,
            Message = message
        };

        private static DriverExecutionResult Failure(string message, string classification) => new()
        {
            Outcome = RunOutcome.Failed,
            Message = message,
            FailureClassification = classification
        };

        private sealed class HttpResponseSnapshot
        {
            public HttpResponseSnapshot(
                int statusCode,
                string body,
                HttpResponseHeaders headers,
                HttpContentHeaders? contentHeaders)
            {
                StatusCode = statusCode;
                Body = body;
                Headers = headers
                .Concat(contentHeaders ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(entry => entry.Key, entry => string.Join(", ", entry.Value), StringComparer.OrdinalIgnoreCase);
            }

            public int StatusCode { get; }
            public string Body { get; }
            public IReadOnlyDictionary<string, string> Headers { get; }
        }
    }
}
