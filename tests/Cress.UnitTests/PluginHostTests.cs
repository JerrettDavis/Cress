using Cress.Core.Models;
using Cress.Execution;
using Cress.Sdk;

namespace Cress.UnitTests;

public sealed class PluginHostTests
{
    [Fact]
    public async Task InvokeStepAsync_ExecutesDotNetStepHandler()
    {
        StepExecutionContext? capturedContext = null;
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) =>
        [
            new FakePluginModule(
                stepHandlers:
                [
                    new StepHandlerRegistration("Execute", (context, _) =>
                    {
                        capturedContext = context;
                        return Task.FromResult(new StepExecutionResult
                        {
                            Success = true,
                            Message = "step executed",
                            Outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["status"] = "ready"
                            },
                            Artifacts =
                            [
                                new EvidenceArtifact
                                {
                                    Category = "plugin",
                                    RelativePath = "plugin/result.txt"
                                }
                            ]
                        });
                    })
                ])
        ]));
        using var workspace = new TestWorkspace();
        var executionContext = CreateContext(workspace.GetPath("artifacts"));

        var result = await host.InvokeStepAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Name = "sample.step",
                Plugin = "sample",
                Operation = "Execute",
                Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["user"] = "Ada"
                }
            },
            executionContext,
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("step executed", result.Message);
        Assert.Equal("ready", result.Outputs["status"]);
        Assert.Single(result.Artifacts);
        Assert.NotNull(capturedContext);
        Assert.Equal("flow-1", capturedContext!.FlowId);
        Assert.Equal("sample.step", capturedContext.StepName);
        Assert.Equal("Ada", capturedContext.Inputs["user"]);
        Assert.Equal("test", capturedContext.Variables["environment"]);
        Assert.Equal("fixture-42", capturedContext.Fixtures["customer"]);
        Assert.NotSame(executionContext.Variables, capturedContext.Variables);
        Assert.NotSame(executionContext.Fixtures, capturedContext.Fixtures);
    }

    [Fact]
    public async Task InvokeFixtureAsync_ExecutesDotNetFixtureProvider_UsingFixtureNameAsFallbackAlias()
    {
        FixtureExecutionContext? capturedContext = null;
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) =>
        [
            new FakePluginModule(
                fixtureProviders:
                [
                    new FixtureProviderRegistration("Create", (context, _) =>
                    {
                        capturedContext = context;
                        return Task.FromResult(new FixtureExecutionResult
                        {
                            Success = true,
                            Message = "fixture created",
                            Outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["id"] = "customer-1"
                            }
                        });
                    })
                ])
        ]));
        using var workspace = new TestWorkspace();
        var executionContext = CreateContext(workspace.GetPath("artifacts"));

        var result = await host.InvokeFixtureAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Kind = "setup",
                Name = "fixture.setup",
                Plugin = "sample-fixtures",
                Operation = "Create",
                Fixture = "customer",
                Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tier"] = "gold"
                }
            },
            executionContext,
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("fixture created", result.Message);
        Assert.Equal("customer-1", result.Outputs["id"]);
        Assert.NotNull(capturedContext);
        Assert.Equal("customer", capturedContext!.FixtureAlias);
        Assert.Equal("customer", capturedContext.FixtureName);
        Assert.Equal("gold", capturedContext.Bindings["tier"]);
        Assert.Equal("test", capturedContext.Variables["environment"]);
        Assert.NotSame(executionContext.Variables, capturedContext.Variables);
    }

    [Fact]
    public async Task InvokeStepAsync_ReturnsFailureWhenOperationIsMissing()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) =>
        [
            new FakePluginModule(
                stepHandlers:
                [
                    new StepHandlerRegistration("DifferentOperation", (_, _) => Task.FromResult(new StepExecutionResult { Success = true }))
                ])
        ]));
        using var workspace = new TestWorkspace();

        var result = await host.InvokeStepAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Name = "sample.step",
                Plugin = "sample",
                Operation = "Execute"
            },
            CreateContext(workspace.GetPath("artifacts")),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("plugin-operation-not-found", result.FailureClassification);
        Assert.Contains("Execute", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeStepAsync_ReturnsFailureWhenPluginCannotBeLoaded()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();

        var result = await host.InvokeStepAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Name = "sample.step",
                Plugin = "missing-plugin",
                Operation = "Execute"
            },
            CreateContext(workspace.GetPath("artifacts")),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("plugin-not-found", result.FailureClassification);
        Assert.Contains("missing-plugin", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeFixtureAsync_AllowsMetadataOnlyResolutionWhenNoProviderIsConfigured()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();

        var result = await host.InvokeFixtureAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Fixture = "customer"
            },
            CreateContext(workspace.GetPath("artifacts")),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Contains("metadata-only", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeFixtureAsync_ReturnsFailureWhenPluginCannotBeLoaded()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();

        var result = await host.InvokeFixtureAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Kind = "setup",
                Plugin = "missing-plugin",
                Operation = "Create",
                Fixture = "customer"
            },
            CreateContext(workspace.GetPath("artifacts")),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("fixture-plugin-not-found", result.FailureClassification);
        Assert.Contains("missing-plugin", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeFixtureAsync_ReturnsFailureWhenOperationIsMissing()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) =>
        [
            new FakePluginModule(
                fixtureProviders:
                [
                    new FixtureProviderRegistration("DifferentOperation", (_, _) => Task.FromResult(new FixtureExecutionResult { Success = true }))
                ])
        ]));
        using var workspace = new TestWorkspace();

        var result = await host.InvokeFixtureAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Kind = "setup",
                Plugin = "sample-fixtures",
                Operation = "Create",
                Fixture = "customer"
            },
            CreateContext(workspace.GetPath("artifacts")),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal("fixture-operation-not-found", result.FailureClassification);
        Assert.Contains("Create", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_ReturnsInspectionDiagnosticWhenModuleLoaderThrows()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => throw new InvalidOperationException("bad plugin metadata")));
        using var workspace = new TestWorkspace();

        var diagnostics = host.Probe(CreateCatalog(workspace.RootPath), "sample");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PLG002", diagnostic.Code);
        Assert.Contains("bad plugin metadata", diagnostic.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_ReturnsMissingPluginDiagnosticWhenPluginFolderIsAbsent()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();

        var diagnostics = host.Probe(CreateCatalog(workspace.RootPath), "sample");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PLG001", diagnostic.Code);
        Assert.Contains("sample", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_ReturnsMissingPackageDiagnosticForNodePlugin()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(workspace.GetPath("steps", "node", "sample"));

        var diagnostics = host.Probe(CreateCatalog(workspace.RootPath), "sample");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PLG004", diagnostic.Code);
        Assert.Contains("package.json", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_ReturnsMissingBuildDiagnosticForNodePlugin()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            Path.Combine("steps", "node", "sample", "package.json"),
            """
            {
              "name": "sample",
              "version": "1.0.0",
              "main": "dist/index.js"
            }
            """);

        var diagnostics = host.Probe(CreateCatalog(workspace.RootPath), "sample");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PLG005", diagnostic.Code);
        Assert.Contains("npm install", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_ReturnsNoDiagnosticsForBuiltNodePlugin()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();
        WriteNodePlugin(
            workspace,
            "sample",
            """
            export default {
              steps: [],
              fixtures: []
            };
            """);

        var diagnostics = host.Probe(CreateCatalog(workspace.RootPath), "sample");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task InvokeStepAsync_ExecutesNodePlugin_AndForwardsLogs()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();
        WriteNodePlugin(
            workspace,
            "sample",
            """
            import fs from "node:fs/promises";
            import path from "node:path";

            export default {
              steps: [
                {
                  operation: "Execute",
                  async execute(context) {
                    await fs.mkdir(path.join(context.artifactDirectory, "plugin"), { recursive: true });
                    await fs.writeFile(path.join(context.artifactDirectory, "plugin", "result.txt"), "ok", "utf8");
                    context.logger.info("node-info", { user: context.inputs.user });
                    context.logger.warning("node-warning", { flow: context.flowId });
                    context.logger.error("node-error", { step: context.stepName });
                    return {
                      success: true,
                      message: `hello ${context.inputs.user}`,
                      outputs: {
                        user: context.inputs.user
                      },
                      artifacts: [
                        {
                          category: "plugin",
                          relativePath: "plugin/result.txt",
                          description: "Node result"
                        }
                      ]
                    };
                  }
                }
              ],
              fixtures: []
            };
            """);
        var logger = new CollectingLogger();

        var result = await host.InvokeStepAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Name = "sample.step",
                Plugin = "sample",
                Operation = "Execute",
                Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["user"] = "Ada"
                }
            },
            CreateContext(workspace.GetPath("artifacts"), logger),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("hello Ada", result.Message);
        Assert.Equal("Ada", result.Outputs["user"]);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("plugin\\result.txt", artifact.RelativePath);
        Assert.Equal("Node result", artifact.Description);
        Assert.Collection(
            logger.Entries,
            entry =>
            {
                Assert.Equal("info", entry.Level);
                Assert.Equal("node-info", entry.Message);
                Assert.Equal("Ada", entry.Data!["user"]);
            },
            entry =>
            {
                Assert.Equal("warning", entry.Level);
                Assert.Equal("node-warning", entry.Message);
                Assert.Equal("flow-1", entry.Data!["flow"]);
            },
            entry =>
            {
                Assert.Equal("error", entry.Level);
                Assert.Equal("node-error", entry.Message);
                Assert.Equal("sample.step", entry.Data!["step"]);
            });
    }

    [Fact]
    public async Task InvokeFixtureAsync_ExecutesNodeCleanupPlugin()
    {
        var host = new PluginHost(new FakeDotNetPluginModuleLoader((_, _) => []));
        using var workspace = new TestWorkspace();
        WriteNodePlugin(
            workspace,
            "sample-fixtures",
            """
            export default {
              steps: [],
              fixtures: [
                {
                  operation: "Destroy",
                  async execute(context) {
                    context.logger.warning("cleanup", { alias: context.fixtureAlias });
                    return {
                      success: true,
                      message: `removed ${context.fixtureName}`,
                      outputs: {
                        alias: context.fixtureAlias
                      }
                    };
                  }
                }
              ]
            };
            """);
        var logger = new CollectingLogger();

        var result = await host.InvokeFixtureAsync(
            CreateCatalog(workspace.RootPath),
            new PlanAction
            {
                Kind = "cleanup",
                Plugin = "sample-fixtures",
                Operation = "Destroy",
                Fixture = "customer",
                Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alias"] = "customer-42"
                }
            },
            CreateContext(workspace.GetPath("artifacts"), logger),
            CancellationToken.None);

        Assert.Equal(RunOutcome.Passed, result.Outcome);
        Assert.Equal("removed customer", result.Message);
        Assert.Equal("customer-42", result.Outputs["alias"]);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("warning", entry.Level);
        Assert.Equal("cleanup", entry.Message);
        Assert.Equal("customer-42", entry.Data!["alias"]);
    }

    private static ProjectCatalog CreateCatalog(string projectRoot)
        => new()
        {
            ProjectRoot = projectRoot,
            EffectiveConfig = new EffectiveConfig
            {
                ActiveProfile = "local",
                Config = new CressConfig
                {
                    Paths = new PathsConfig()
                },
                Profile = new CressProfile
                {
                    Profile = "local",
                    BaseUrl = "https://example.test"
                }
            }
        };

    private static FlowExecutionContext CreateContext(string artifactRoot, ICressLogger? logger = null)
        => new()
        {
            FlowId = "flow-1",
            FlowName = "Sample flow",
            ArtifactRoot = artifactRoot,
            EffectiveConfig = new EffectiveConfig
            {
                ActiveProfile = "local",
                Profile = new CressProfile
                {
                    Profile = "local",
                    BaseUrl = "https://example.test"
                }
            },
            EvidenceStore = new EvidenceStore(artifactRoot),
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["environment"] = "test"
            },
            Fixtures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer"] = "fixture-42"
            },
            Logger = logger ?? NullCressLogger.Instance
        };

    private static void WriteNodePlugin(TestWorkspace workspace, string pluginName, string moduleSource)
    {
        workspace.WriteFile(
            Path.Combine("steps", "node", pluginName, "package.json"),
            """
            {
              "name": "sample",
              "version": "1.0.0",
              "type": "module",
              "main": "dist/index.js"
            }
            """);
        workspace.WriteFile(Path.Combine("steps", "node", pluginName, "dist", "index.js"), moduleSource);
    }

    private sealed class FakeDotNetPluginModuleLoader : IDotNetPluginModuleLoader
    {
        private readonly Func<string, string?, IReadOnlyList<ICressPluginModule>> _loadModules;

        public FakeDotNetPluginModuleLoader(Func<string, string?, IReadOnlyList<ICressPluginModule>> loadModules)
        {
            _loadModules = loadModules;
        }

        public IReadOnlyList<ICressPluginModule> LoadModules(string projectRoot, string? pluginName)
            => _loadModules(projectRoot, pluginName);
    }

    private sealed class FakePluginModule : ICressPluginModule
    {
        private readonly IReadOnlyList<StepHandlerRegistration> _stepHandlers;
        private readonly IReadOnlyList<FixtureProviderRegistration> _fixtureProviders;

        public FakePluginModule(
            IReadOnlyList<StepHandlerRegistration>? stepHandlers = null,
            IReadOnlyList<FixtureProviderRegistration>? fixtureProviders = null)
        {
            _stepHandlers = stepHandlers ?? [];
            _fixtureProviders = fixtureProviders ?? [];
        }

        public IEnumerable<StepHandlerRegistration> GetStepHandlers() => _stepHandlers;

        public IEnumerable<FixtureProviderRegistration> GetFixtureProviders() => _fixtureProviders;
    }

    private sealed class CollectingLogger : ICressLogger
    {
        public List<LogEntry> Entries { get; } = [];

        public void Info(string message, IReadOnlyDictionary<string, string>? data = null)
            => Entries.Add(new LogEntry("info", message, data));

        public void Warning(string message, IReadOnlyDictionary<string, string>? data = null)
            => Entries.Add(new LogEntry("warning", message, data));

        public void Error(string message, IReadOnlyDictionary<string, string>? data = null)
            => Entries.Add(new LogEntry("error", message, data));
    }

    private sealed record LogEntry(string Level, string Message, IReadOnlyDictionary<string, string>? Data);
}
