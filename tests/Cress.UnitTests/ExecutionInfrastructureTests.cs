using System.Reflection;
using System.Runtime.CompilerServices;
using Cress.Core.Models;
using Cress.Execution;

namespace Cress.UnitTests;

public sealed class ExecutionInfrastructureTests
{
    [Fact]
    public void ReportGenerator_generates_all_formats_and_lists_reports_in_descending_order()
    {
        using var workspace = new TestWorkspace();
        var generator = new ReportGenerator();
        var catalog = CreateCatalog(workspace.GetPath("project"));

        var firstResult = CreateRunResult(
            "run-001",
            workspace.GetPath("project", "artifacts", "runs", "run-001"));
        var secondResult = CreateRunResult(
            "run-002",
            workspace.GetPath("project", "artifacts", "runs", "run-002"));

        var firstReports = generator.Generate(catalog, firstResult, ["html", "json", "junit", "md", "ignored"]);
        var secondReports = generator.Generate(catalog, secondResult, []);
        var listedReports = generator.ListReports(workspace.GetPath("project"), "reports");

        Assert.Equal(["html", "json", "junit", "markdown"], firstReports.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Path.Combine(workspace.GetPath("project"), "reports", "run-002"), listedReports[0]);
        Assert.Equal(Path.Combine(workspace.GetPath("project"), "reports", "run-001"), listedReports[1]);
        Assert.Equal(4, secondReports.Count);

        var html = File.ReadAllText(firstReports["html"]);
        Assert.Contains("REQ-123 / AC-1, AC-2", html, StringComparison.Ordinal);
        Assert.Contains("Something broke", html, StringComparison.Ordinal);

        var json = File.ReadAllText(firstReports["json"]);
        Assert.Contains("\"durationMs\"", json, StringComparison.OrdinalIgnoreCase);

        var junit = File.ReadAllText(firstReports["junit"]);
        Assert.Contains("<failure", junit, StringComparison.Ordinal);
        Assert.Contains("<skipped>", junit, StringComparison.Ordinal);

        var markdown = File.ReadAllText(firstReports["markdown"]);
        Assert.Contains("## Failed flows", markdown, StringComparison.Ordinal);
        Assert.Contains("Rerun start step", markdown, StringComparison.Ordinal);
        Assert.Contains("- html:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportGenerator_list_reports_returns_empty_for_missing_root()
    {
        var generator = new ReportGenerator();

        var reports = generator.ListReports(@"C:\repo\missing", "reports");

        Assert.Empty(reports);
    }

    [Fact]
    public void RepositoryAssetLocator_finds_assets_from_repository_root_and_current_directory()
    {
        using var workspace = new TestWorkspace();
        var repositoryRoot = workspace.GetPath("repo");
        var nestedRoot = Path.Combine(repositoryRoot, "src", "nested");
        Directory.CreateDirectory(nestedRoot);
        var relativeAsset = Path.Combine("node", "tool.mjs");
        var assetPath = Path.Combine(repositoryRoot, relativeAsset);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllText(assetPath, "export {};");

        var locatorType = typeof(ReportGenerator).Assembly.GetType("Cress.Execution.RepositoryAssetLocator", throwOnError: true)!;
        var findAsset = locatorType.GetMethod("FindRepositoryAsset", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var resolveNode = locatorType.GetMethod("ResolveNodeExecutable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(findAsset);
        Assert.NotNull(resolveNode);

        var originalRoot = Environment.GetEnvironmentVariable("CRESS_REPOSITORY_ROOT");
        var originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.SetEnvironmentVariable("CRESS_REPOSITORY_ROOT", nestedRoot);
            Environment.CurrentDirectory = nestedRoot;

            var resolvedAsset = Assert.IsType<string>(findAsset.Invoke(null, [relativeAsset]));
            var missingAsset = findAsset.Invoke(null, [Path.Combine("node", "missing.mjs")]);
            var nodeExecutable = Assert.IsType<string>(resolveNode.Invoke(null, []));

            Assert.Equal(assetPath, resolvedAsset);
            Assert.Null(missingAsset);
            Assert.False(string.IsNullOrWhiteSpace(nodeExecutable));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CRESS_REPOSITORY_ROOT", originalRoot);
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Fact]
    public void NodeProcessJsonRpcClient_private_helpers_include_stderr_details()
    {
        var clientType = typeof(ReportGenerator).Assembly.GetType("Cress.Execution.NodeProcessJsonRpcClient", throwOnError: true)!;
        var client = RuntimeHelpers.GetUninitializedObject(clientType);
        var stderrField = clientType.GetField("_stderr", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The _stderr field was not found.");
        stderrField.SetValue(client, new List<string> { "first", "second" });

        var buildTerminationMessage = clientType.GetMethod("BuildTerminationMessage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildTerminationMessage was not found.");
        var buildStderrSummary = clientType.GetMethod("BuildStderrSummary", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildStderrSummary was not found.");

        var summary = Assert.IsType<string>(buildStderrSummary.Invoke(client, []));
        var termination = Assert.IsType<string>(buildTerminationMessage.Invoke(client, ["invoke"]));

        Assert.Contains("stderr:", summary, StringComparison.Ordinal);
        Assert.Contains("first", summary, StringComparison.Ordinal);
        Assert.Contains("second", termination, StringComparison.Ordinal);
        Assert.Contains("invoke", termination, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NodeProcessJsonRpcClient_can_initialize_invoke_and_surface_host_failures()
    {
        using var workspace = new TestWorkspace();
        var scriptPath = workspace.GetPath("rpc-host.mjs");
        workspace.WriteFile("rpc-host.mjs", """
        import readline from 'node:readline';

        const rl = readline.createInterface({
          input: process.stdin,
          crlfDelay: Infinity
        });

        for await (const line of rl) {
          const request = JSON.parse(line);

          if (request.method === 'cress/initialize') {
            console.log(JSON.stringify({
              jsonrpc: '2.0',
              id: request.id,
              result: {
                protocolVersion: 1,
                pluginId: 'test.plugin',
                capabilities: [],
                ready: true
              }
            }));
            continue;
          }

          if (request.method === 'math/add') {
            console.log(JSON.stringify({
              jsonrpc: '2.0',
              id: request.id,
              result: request.params.a + request.params.b
            }));
            continue;
          }

          if (request.method === 'noop') {
            console.log(JSON.stringify({
              jsonrpc: '2.0',
              id: request.id,
              result: null
            }));
            continue;
          }

          if (request.method === 'fail') {
            console.error('rpc failure on stderr');
            console.log(JSON.stringify({
              jsonrpc: '2.0',
              id: request.id,
              error: {
                code: -32000,
                message: 'boom'
              }
            }));
            continue;
          }

          if (request.method === 'exit') {
            process.stderr.write('host exiting\n', () => process.exit(0));
            continue;
          }
        }
        """);

        var clientType = typeof(ReportGenerator).Assembly.GetType("Cress.Execution.NodeProcessJsonRpcClient", throwOnError: true)!;
        var client = Activator.CreateInstance(clientType, scriptPath)
            ?? throw new InvalidOperationException("NodeProcessJsonRpcClient could not be constructed.");
        var initializeAsync = clientType.GetMethod("InitializeAsync")
            ?? throw new InvalidOperationException("InitializeAsync was not found.");
        var invokeAsync = clientType.GetMethod("InvokeAsync")
            ?? throw new InvalidOperationException("InvokeAsync was not found.");

        await ((Task?)initializeAsync.Invoke(client, [workspace.RootPath, "local", CancellationToken.None])
            ?? throw new InvalidOperationException("InitializeAsync returned null."));

        var sumTask = (Task<int>?)invokeAsync.MakeGenericMethod(typeof(int)).Invoke(client, ["math/add", new { a = 2, b = 3 }, CancellationToken.None])
            ?? throw new InvalidOperationException("InvokeAsync<int> returned null.");
        Assert.Equal(5, await sumTask);

        var noopTask = (Task<string?>?)invokeAsync.MakeGenericMethod(typeof(string)).Invoke(client, ["noop", null, CancellationToken.None])
            ?? throw new InvalidOperationException("InvokeAsync<string> returned null.");
        Assert.Null(await noopTask);

        var failureTask = (Task<string>?)invokeAsync.MakeGenericMethod(typeof(string)).Invoke(client, ["fail", null, CancellationToken.None])
            ?? throw new InvalidOperationException("InvokeAsync<string> failure task returned null.");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await failureTask);
        Assert.Contains("boom", failure.Message, StringComparison.Ordinal);
        Assert.Contains("stderr:", failure.Message, StringComparison.Ordinal);

        var exitTask = (Task<string>?)invokeAsync.MakeGenericMethod(typeof(string)).Invoke(client, ["exit", null, CancellationToken.None])
            ?? throw new InvalidOperationException("InvokeAsync<string> exit task returned null.");
        var termination = await Assert.ThrowsAsync<InvalidOperationException>(async () => await exitTask);
        Assert.Contains("The Node host exited while handling 'exit'.", termination.Message, StringComparison.Ordinal);
        Assert.Contains("host exiting", termination.Message, StringComparison.Ordinal);

        await ((IAsyncDisposable)client).DisposeAsync();
    }

    [Fact]
    public void PlanGenerator_reports_fixture_step_input_and_driver_diagnostics()
    {
        var generator = new PlanGenerator();
        var catalog = new ProjectCatalog
        {
            ProjectRoot = @"C:\workspace",
            EffectiveConfig = new EffectiveConfig
            {
                Config = new CressConfig
                {
                    Drivers = new Dictionary<string, DriverConfig>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["playwright"] = new() { Enabled = false }
                    }
                }
            },
            StepRegistry = new StepRegistrySnapshot(
                new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["browser.open"] = new()
                    {
                        Name = "browser.open",
                        Drivers = ["playwright"],
                        Inputs = new Dictionary<string, StepContractField>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["path"] = new() { Required = true }
                        },
                        Implementation = new StepImplementationBinding { Plugin = "builtin.playwright", Operation = "open" }
                    }
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            FixtureDefinitions = new Dictionary<string, FixtureDefinition>(StringComparer.OrdinalIgnoreCase)
        };
        var flow = new NormalizedFlow
        {
            FlowId = "diagnostic-flow",
            Name = "Diagnostic flow",
            SourceFile = @"C:\workspace\flows\diagnostic.flow.yaml",
            Fixtures =
            [
                new() { Name = "anon" },
                new() { Name = "missing", Use = "missing.fixture" }
            ],
            Actions =
            [
                new()
                {
                    Kind = "action",
                    Name = "browser.open",
                    Source = new SourceReference { SourceFile = @"C:\workspace\flows\diagnostic.flow.yaml" }
                }
            ],
            Expectations =
            [
                new()
                {
                    Kind = "expectation",
                    Name = "browser.assert",
                    Source = new SourceReference { SourceFile = @"C:\workspace\flows\diagnostic.flow.yaml" }
                }
            ]
        };

        var plan = generator.Generate(catalog, [flow]);

        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "PLN001");
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "PLN002");
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "PLN003");
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "PLN004");
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "PLN005");
    }

    [Fact]
    public void RuntimeOrchestrator_private_helpers_select_flows_build_invocation_and_match_requested_steps()
    {
        var orchestrator = RuntimeHelpers.GetUninitializedObject(typeof(RuntimeOrchestrator));
        var selectFlows = typeof(RuntimeOrchestrator).GetMethod("SelectFlows", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SelectFlows was not found.");
        var buildInvocation = typeof(RuntimeOrchestrator).GetMethod("BuildInvocation", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildInvocation was not found.");
        var matchesRequestedStart = typeof(RuntimeOrchestrator).GetMethod("MatchesRequestedStart", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MatchesRequestedStart was not found.");

        var catalog = new ProjectCatalog
        {
            ProjectRoot = @"C:\workspace",
            EffectiveConfig = new EffectiveConfig
            {
                Config = new CressConfig
                {
                    Defaults = new DefaultsConfig { Retries = 2, Evidence = "standard" }
                },
                Profile = new CressProfile
                {
                    Evidence = new EvidenceProfileConfig { Mode = "full", ScreenshotPolicy = "on-failure" }
                }
            },
            NormalizedFlows =
            [
                new() { FlowId = "checkout", Name = "Checkout", Tags = ["smoke"], SourceFile = @"C:\workspace\flows\checkout.flow.yaml" },
                new() { FlowId = "returns", Name = "Returns", Tags = ["regression"], SourceFile = @"C:\workspace\flows\returns.flow.yaml" }
            ]
        };
        var diagnostics = new List<Diagnostic>();
        var options = new RunOptions
        {
            FlowPaths = [Path.Combine("flows", "checkout.flow.yaml"), Path.Combine("flows", "missing.flow.yaml")],
            Tag = "smoke",
            Trigger = "",
            StartFromStep = "action:browser.open"
        };

        var selected = Assert.IsAssignableFrom<IReadOnlyList<NormalizedFlow>>(selectFlows.Invoke(orchestrator, [catalog, options, diagnostics]));
        var invocation = Assert.IsType<RunInvocation>(buildInvocation.Invoke(null, [options, catalog, selected]));

        Assert.Single(selected);
        Assert.Equal("checkout", selected[0].FlowId);
        Assert.Equal("manual", invocation.Trigger);
        Assert.Equal(["C:\\workspace\\flows\\checkout.flow.yaml"], invocation.RequestedFlows);
        Assert.Equal(2, invocation.RetryCount);
        Assert.Equal("full", invocation.EvidenceMode);
        Assert.Equal("on-failure", invocation.ScreenshotPolicy);
        Assert.True(diagnostics.Count == 0);

        var unmatched = Assert.IsAssignableFrom<IReadOnlyList<NormalizedFlow>>(selectFlows.Invoke(orchestrator, [catalog, options with { Tag = "missing" }, diagnostics]));
        Assert.Empty(unmatched);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "SEL002");

        var action = new PlanAction { Kind = "action", Name = "browser.open", Step = "browser.open" };
        Assert.True(Assert.IsType<bool>(matchesRequestedStart.Invoke(null, [action, "browser.open"])));
        Assert.True(Assert.IsType<bool>(matchesRequestedStart.Invoke(null, [action, "action:browser.open"])));
        Assert.True(Assert.IsType<bool>(matchesRequestedStart.Invoke(null, [action, null])));
        Assert.False(Assert.IsType<bool>(matchesRequestedStart.Invoke(null, [action, "other.step"])));
    }

    private static ProjectCatalog CreateCatalog(string projectRoot) => new()
    {
        ProjectRoot = projectRoot,
        EffectiveConfig = new EffectiveConfig
        {
            Config = new CressConfig
            {
                Project = new ProjectConfig
                {
                    Name = "Execution Infra"
                },
                Paths = new PathsConfig
                {
                    Reports = "reports"
                }
            },
            ActiveProfile = "local"
        }
    };

    private static RunResult CreateRunResult(string runId, string artifactRoot) => new()
    {
        Metadata = new RunMetadata
        {
            RunId = runId,
            ArtifactRoot = artifactRoot,
            ProjectName = "Execution Infra",
            Profile = "local",
            StartedAt = new DateTimeOffset(2026, 5, 14, 22, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 5, 14, 22, 1, 0, TimeSpan.Zero),
            DurationMs = 60000
        },
        Invocation = new RunInvocation
        {
            Trigger = "manual",
            RetryCount = 2,
            ScreenshotPolicy = "on-failure",
            StartFromStep = "step-2"
        },
        Flows =
        [
            new FlowRunResult
            {
                FlowId = "flow-failed",
                Name = "Failed flow",
                CapabilityId = "capability-a",
                Outcome = RunOutcome.Failed,
                FailureClassification = "assertion-failed",
                FailureMessage = "Something broke",
                DurationMs = 1000,
                Drivers = ["http"],
                Traceability = new TraceabilityInfo
                {
                    Requirement = "REQ-123",
                    AcceptanceCriteria = ["AC-1", "AC-2"]
                },
                Steps =
                [
                    new StepRunResult
                    {
                        Kind = "when",
                        Name = "step-1",
                        Outcome = RunOutcome.Failed,
                        Attempt = 2,
                        DurationMs = 250,
                        Artifacts =
                        [
                            new EvidenceArtifact
                            {
                                Category = "screenshots",
                                RelativePath = "screenshots\\shot.png"
                            }
                        ]
                    }
                ]
            },
            new FlowRunResult
            {
                FlowId = "flow-skipped",
                Name = "Skipped flow",
                Outcome = RunOutcome.Skipped,
                FailureMessage = "Skipped for maintenance",
                DurationMs = 10,
                Drivers = ["http"]
            }
        ]
    };
}
