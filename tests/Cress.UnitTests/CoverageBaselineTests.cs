using Cress.Core.Models;
using Cress.Sdk;

namespace Cress.UnitTests;

public sealed class CoverageBaselineTests
{
    [Fact]
    public void CoreModelContracts_PreserveConfiguredValues()
    {
        var fixtureProvider = new FixtureProviderBinding
        {
            Plugin = "fixtures.demo",
            Operation = "seed"
        };
        var traceability = new TraceabilityInfo
        {
            Requirement = "REQ-42",
            AcceptanceCriteria = ["AC-1", "AC-2"],
            Owner = "quality",
            Risk = "high"
        };
        var step = new StepDefinition
        {
            Name = "browser.search",
            Description = "Searches from the landing page.",
            Aliases = ["search", "find"],
            Inputs = new Dictionary<string, StepContractField>(StringComparer.OrdinalIgnoreCase)
            {
                ["query"] = new()
                {
                    Type = "string",
                    Required = true,
                    Description = "The phrase to search for."
                }
            },
            Outputs = new Dictionary<string, StepContractField>(StringComparer.OrdinalIgnoreCase)
            {
                ["resultCount"] = new()
                {
                    Type = "integer",
                    Required = false,
                    Description = "The number of results rendered."
                }
            },
            Preconditions = ["browser.open"],
            Effects = ["browser.results-ready"],
            Drivers = ["playwright"],
            Idempotency = "repeatable",
            RetrySafe = true,
            TimeoutMs = 3_000,
            Owner = "qa",
            Version = 2,
            Implementation = new StepImplementationBinding
            {
                Plugin = "builtin.playwright",
                Operation = "search"
            },
            SourceFile = "steps\\browser.yaml"
        };
        var profile = new CressProfile
        {
            Profile = "ci",
            BaseUrl = "https://example.test",
            Timeouts = new TimeoutsConfig
            {
                Step = 15,
                Expectation = 25,
                Driver = 35
            },
            Evidence = new EvidenceProfileConfig
            {
                Mode = "full",
                Screenshots = true,
                ScreenshotPolicy = "always"
            },
            Secrets = new SecretsConfig
            {
                Required = ["api-key", "auth-token"]
            },
            Authentication = new AuthenticationConfig
            {
                Header = "Authorization",
                Scheme = "Bearer",
                Token = "token-value",
                TokenEnvironmentVariable = "CRESS_TOKEN"
            },
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Trace"] = "enabled"
            },
            Playwright = new PlaywrightProfileConfig
            {
                Simulated = false,
                Headless = true,
                Browser = "chromium"
            },
            Flawright = new FlawrightProfileConfig
            {
                ApplicationPath = "calc.exe",
                Arguments = "/portable",
                WindowTitle = "Calculator",
                LaunchTimeoutMs = 10_000
            },
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant"] = "demo"
            },
            Flake = new FlakeProfileConfig
            {
                Window = 5,
                MinPasses = 3,
                MinFails = 2,
                Threshold = 0.25
            }
        };

        Assert.Equal("fixtures.demo", fixtureProvider.Plugin);
        Assert.Equal("seed", fixtureProvider.Operation);

        Assert.Equal("REQ-42", traceability.Requirement);
        Assert.Equal(["AC-1", "AC-2"], traceability.AcceptanceCriteria);
        Assert.Equal("quality", traceability.Owner);
        Assert.Equal("high", traceability.Risk);

        Assert.Equal("browser.search", step.Name);
        Assert.Equal("Searches from the landing page.", step.Description);
        Assert.Equal(["search", "find"], step.Aliases);
        Assert.Equal("string", step.Inputs!["query"].Type);
        Assert.True(step.Inputs["query"].Required);
        Assert.Equal("The phrase to search for.", step.Inputs["query"].Description);
        Assert.Equal("integer", step.Outputs!["resultCount"].Type);
        Assert.False(step.Outputs["resultCount"].Required);
        Assert.Equal("The number of results rendered.", step.Outputs["resultCount"].Description);
        Assert.Equal(["browser.open"], step.Preconditions);
        Assert.Equal(["browser.results-ready"], step.Effects);
        Assert.Equal(["playwright"], step.Drivers);
        Assert.Equal("repeatable", step.Idempotency);
        Assert.True(step.RetrySafe);
        Assert.Equal(3_000, step.TimeoutMs);
        Assert.Equal("qa", step.Owner);
        Assert.Equal(2, step.Version);
        Assert.Equal("builtin.playwright", step.Implementation!.Plugin);
        Assert.Equal("search", step.Implementation.Operation);
        Assert.Equal("steps\\browser.yaml", step.SourceFile);

        Assert.Equal("ci", profile.Profile);
        Assert.Equal("https://example.test", profile.BaseUrl);
        Assert.Equal(15, profile.Timeouts!.Step);
        Assert.Equal(25, profile.Timeouts.Expectation);
        Assert.Equal(35, profile.Timeouts.Driver);
        Assert.Equal("full", profile.Evidence!.Mode);
        Assert.True(profile.Evidence.Screenshots);
        Assert.Equal("always", profile.Evidence.ScreenshotPolicy);
        Assert.Equal(["api-key", "auth-token"], profile.Secrets!.Required);
        Assert.Equal("Authorization", profile.Authentication!.Header);
        Assert.Equal("Bearer", profile.Authentication.Scheme);
        Assert.Equal("token-value", profile.Authentication.Token);
        Assert.Equal("CRESS_TOKEN", profile.Authentication.TokenEnvironmentVariable);
        Assert.Equal("enabled", profile.Headers!["X-Trace"]);
        Assert.False(profile.Playwright!.Simulated);
        Assert.True(profile.Playwright.Headless);
        Assert.Equal("chromium", profile.Playwright.Browser);
        Assert.Equal("calc.exe", profile.Flawright!.ApplicationPath);
        Assert.Equal("/portable", profile.Flawright.Arguments);
        Assert.Equal("Calculator", profile.Flawright.WindowTitle);
        Assert.Equal(10_000, profile.Flawright.LaunchTimeoutMs);
        Assert.Equal("demo", profile.Variables!["tenant"]);
        Assert.Equal(5, profile.Flake!.Window);
        Assert.Equal(3, profile.Flake.MinPasses);
        Assert.Equal(2, profile.Flake.MinFails);
        Assert.Equal(0.25, profile.Flake.Threshold);
    }

    [Fact]
    public void DiagnosticsAndValidationFlags_ReflectErrorPresence()
    {
        var warning = new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Code = "WARN001",
            Message = "Minor issue",
            File = "flows\\demo.flow.yaml",
            Line = 12,
            Column = 3,
            Details = "Extra detail"
        };
        var error = new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Code = "ERR001",
            Message = "Blocking issue"
        };

        var success = new OperationResult<string>
        {
            Value = "ready",
            Diagnostics = [warning]
        };
        var failed = new OperationResult<string>
        {
            Value = "ready",
            Diagnostics = [warning, error]
        };
        var missingValue = new OperationResult<string>
        {
            Value = null,
            Diagnostics = [warning]
        };
        var valid = new ValidationResult
        {
            Diagnostics = [warning]
        };
        var invalid = new ValidationResult
        {
            Diagnostics = [warning, error]
        };

        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("WARN001", warning.Code);
        Assert.Equal("Minor issue", warning.Message);
        Assert.Equal("flows\\demo.flow.yaml", warning.File);
        Assert.Equal(12, warning.Line);
        Assert.Equal(3, warning.Column);
        Assert.Equal("Extra detail", warning.Details);

        Assert.True(success.Success);
        Assert.False(failed.Success);
        Assert.False(missingValue.Success);
        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public async Task SdkContracts_DefaultHelpersAndHandlersBehaveAsExpected()
    {
        var stepContext = new StepExecutionContext
        {
            FlowId = "search-flow",
            StepName = "browser.search",
            ArtifactDirectory = "artifacts\\search",
            BaseUrl = "https://example.test",
            Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["query"] = "cress",
                ["blank"] = "  "
            },
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant"] = "demo"
            },
            Fixtures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["browser"] = "chromium"
            }
        };
        var fixtureContext = new FixtureExecutionContext
        {
            FlowId = "search-flow",
            FixtureAlias = "browser",
            FixtureName = "browser.shared",
            ArtifactDirectory = "artifacts\\fixtures",
            Bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["channel"] = "stable"
            },
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["region"] = "us-east"
            }
        };
        var artifacts = new[]
        {
            new EvidenceArtifact
            {
                Category = "screenshot",
                RelativePath = "artifacts\\search\\result.png",
                Description = "Result",
                MediaType = "image/png",
                SizeBytes = 128
            }
        };
        var stepRegistration = new StepHandlerRegistration(
            "search",
            (context, _) => Task.FromResult(new StepExecutionResult
            {
                Success = true,
                Message = context.GetRequiredInput("query"),
                FailureClassification = "none",
                Outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["status"] = "ok"
                },
                Artifacts = artifacts
            }));
        var fixtureRegistration = new FixtureProviderRegistration(
            "browser",
            (context, _) => Task.FromResult(new FixtureExecutionResult
            {
                Success = true,
                Message = context.FixtureName,
                Outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["channel"] = context.Bindings["channel"]
                },
                Artifacts = artifacts
            }));
        ICressPluginModule pluginModule = new TestPluginModule(stepRegistration);

        Assert.Equal("search-flow", stepContext.FlowId);
        Assert.Equal("browser.search", stepContext.StepName);
        Assert.Equal("artifacts\\search", stepContext.ArtifactDirectory);
        Assert.Equal("https://example.test", stepContext.BaseUrl);
        Assert.Equal("demo", stepContext.Variables["tenant"]);
        Assert.Equal("chromium", stepContext.Fixtures["browser"]);
        Assert.Equal("cress", stepContext.GetRequiredInput("QUERY"));
        Assert.Equal("cress", stepContext.GetInput("query"));
        Assert.Null(stepContext.GetInput("missing"));
        Assert.Same(NullCressLogger.Instance, stepContext.Logger);
        Assert.Same(NullDriverAccessor.Instance, stepContext.Drivers);

        var missingInput = Assert.Throws<InvalidOperationException>(() => stepContext.GetRequiredInput("blank"));
        Assert.Contains("Required input 'blank' was not supplied for step 'browser.search'.", missingInput.Message);

        Assert.Equal("search-flow", fixtureContext.FlowId);
        Assert.Equal("browser", fixtureContext.FixtureAlias);
        Assert.Equal("browser.shared", fixtureContext.FixtureName);
        Assert.Equal("artifacts\\fixtures", fixtureContext.ArtifactDirectory);
        Assert.Equal("stable", fixtureContext.Bindings["channel"]);
        Assert.Equal("us-east", fixtureContext.Variables["region"]);
        Assert.Same(NullCressLogger.Instance, fixtureContext.Logger);

        var stepResult = await stepRegistration.Execute(stepContext, CancellationToken.None);
        Assert.Equal("search", stepRegistration.Operation);
        Assert.True(stepResult.Success);
        Assert.Equal("cress", stepResult.Message);
        Assert.Equal("none", stepResult.FailureClassification);
        Assert.Equal("ok", stepResult.Outputs["status"]);
        Assert.Same(artifacts, stepResult.Artifacts);

        var fixtureResult = await fixtureRegistration.Execute(fixtureContext, CancellationToken.None);
        Assert.Equal("browser", fixtureRegistration.Operation);
        Assert.True(fixtureResult.Success);
        Assert.Equal("browser.shared", fixtureResult.Message);
        Assert.Equal("stable", fixtureResult.Outputs["channel"]);
        Assert.Same(artifacts, fixtureResult.Artifacts);

        Assert.Single(pluginModule.GetStepHandlers());
        Assert.Empty(pluginModule.GetFixtureProviders());

        NullCressLogger.Instance.Info("info");
        NullCressLogger.Instance.Warning("warning");
        NullCressLogger.Instance.Error("error");

        Assert.False(NullDriverAccessor.Instance.TryGetMetadata("playwright", out var metadata));
        Assert.Null(metadata);
        Assert.Empty(NullDriverAccessor.Instance.Snapshot());
    }

    private sealed class TestPluginModule(StepHandlerRegistration registration) : ICressPluginModule
    {
        public IEnumerable<StepHandlerRegistration> GetStepHandlers()
            => [registration];
    }
}
