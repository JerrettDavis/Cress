using System.Text.Json;
using Cress.Core.Models;
using Cress.Sdk;

namespace Cress.Execution;

public sealed class PluginHost
{
    private readonly IDotNetPluginModuleLoader _dotNetPluginModuleLoader;
    private static readonly string? NodeHostScriptPath = RepositoryAssetLocator.FindRepositoryAsset(Path.Combine("node", "cress-plugin-host-node", "host.js"));

    public PluginHost(IDotNetPluginModuleLoader? dotNetPluginModuleLoader = null)
    {
        _dotNetPluginModuleLoader = dotNetPluginModuleLoader ?? new ReflectionDotNetPluginModuleLoader();
    }

    public async Task<DriverExecutionResult> InvokeStepAsync(ProjectCatalog catalog, PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        var modules = _dotNetPluginModuleLoader.LoadModules(catalog.ProjectRoot, action.Plugin);
        if (modules.Count == 0)
        {
            if (TryResolveNodePluginRoot(catalog.ProjectRoot, action.Plugin, out var pluginRoot))
            {
                return await InvokeNodeStepAsync(catalog, pluginRoot!, action, context, cancellationToken);
            }

            return CreateFailureResult($"Plugin '{action.Plugin}' could not be loaded.", "plugin-not-found");
        }

        var handler = FindStepHandler(modules, action.Operation);
        if (handler is null)
        {
            return CreateFailureResult($"Operation '{action.Operation}' was not found in plugin '{action.Plugin}'.", "plugin-operation-not-found");
        }

        var result = await handler.Execute(CreateStepExecutionContext(action, context), cancellationToken);
        return ToDriverResult(result);
    }

    public async Task<DriverExecutionResult> InvokeFixtureAsync(ProjectCatalog catalog, PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.Plugin) || string.IsNullOrWhiteSpace(action.Operation))
        {
            return CreateMetadataOnlyFixtureResult();
        }

        var modules = _dotNetPluginModuleLoader.LoadModules(catalog.ProjectRoot, action.Plugin);
        if (modules.Count == 0)
        {
            if (TryResolveNodePluginRoot(catalog.ProjectRoot, action.Plugin, out var pluginRoot))
            {
                return await InvokeNodeFixtureAsync(catalog, pluginRoot!, action, context, cancellationToken);
            }

            return CreateFailureResult($"Fixture plugin '{action.Plugin}' could not be loaded.", "fixture-plugin-not-found");
        }

        var handler = FindFixtureProvider(modules, action.Operation);
        if (handler is null)
        {
            return CreateFailureResult($"Fixture operation '{action.Operation}' was not found in plugin '{action.Plugin}'.", "fixture-operation-not-found");
        }

        var result = await handler.Execute(CreateFixtureExecutionContext(action, context), cancellationToken);
        return ToDriverResult(result);
    }

    public IReadOnlyList<Diagnostic> Probe(ProjectCatalog catalog, string pluginName)
    {
        try
        {
            var modules = _dotNetPluginModuleLoader.LoadModules(catalog.ProjectRoot, pluginName);
            if (modules.Count > 0)
            {
                return [];
            }

            if (TryResolveNodePluginRoot(catalog.ProjectRoot, pluginName, out var pluginRoot))
            {
                return ProbeNodePlugin(catalog, pluginName, pluginRoot!);
            }

            return
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "PLG001",
                    Message = $"Plugin '{pluginName}' was not found.",
                    File = Path.Combine(catalog.ProjectRoot, catalog.EffectiveConfig.Config.Paths.Steps)
                }
            ];
        }
        catch (Exception ex)
        {
            return
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "PLG002",
                    Message = $"Plugin '{pluginName}' could not be inspected.",
                    File = catalog.ProjectRoot,
                    Details = ex.Message
                }
            ];
        }
    }

    private async Task<DriverExecutionResult> InvokeNodeStepAsync(ProjectCatalog catalog, string pluginRoot, PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        await using var client = CreateNodeClient(catalog, pluginRoot);
        await client.InitializeAsync(catalog.ProjectRoot, catalog.EffectiveConfig.ActiveProfile, cancellationToken);
        var response = await client.InvokeAsync<NodeExecutionResponse>("steps/execute", new
        {
            operation = action.Operation,
            context = new
            {
                flowId = context.FlowId,
                stepName = action.Name,
                artifactDirectory = context.ArtifactRoot,
                baseUrl = context.EffectiveConfig.Profile.BaseUrl,
                inputs = action.Inputs,
                variables = context.Variables,
                fixtures = context.Fixtures,
                drivers = context.Drivers.Snapshot()
            }
        }, cancellationToken);
        WritePluginLogs(context.Logger, response.Logs ?? []);
        return ToDriverResult(response, context.ArtifactRoot);
    }

    private async Task<DriverExecutionResult> InvokeNodeFixtureAsync(ProjectCatalog catalog, string pluginRoot, PlanAction action, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        await using var client = CreateNodeClient(catalog, pluginRoot);
        await client.InitializeAsync(catalog.ProjectRoot, catalog.EffectiveConfig.ActiveProfile, cancellationToken);
        var method = string.Equals(action.Kind, "cleanup", StringComparison.OrdinalIgnoreCase) ? "fixtures/cleanup" : "fixtures/create";
        var response = await client.InvokeAsync<NodeExecutionResponse>(method, new
        {
            operation = action.Operation,
            context = new
            {
                flowId = context.FlowId,
                fixtureAlias = action.Inputs.TryGetValue("alias", out var alias) ? alias : action.Fixture,
                fixtureName = action.Fixture,
                artifactDirectory = context.ArtifactRoot,
                bindings = action.Inputs,
                variables = context.Variables
            }
        }, cancellationToken);
        WritePluginLogs(context.Logger, response.Logs ?? []);
        return ToDriverResult(response, context.ArtifactRoot);
    }

    private IReadOnlyList<Diagnostic> ProbeNodePlugin(ProjectCatalog catalog, string pluginName, string pluginRoot)
    {
        if (NodeHostScriptPath is null)
        {
            return
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "PLG003",
                    Message = "The in-repo Node plugin host is missing.",
                    File = catalog.ProjectRoot
                }
            ];
        }

        var packageJson = Path.Combine(pluginRoot, "package.json");
        var distPath = ResolveNodeEntryPoint(pluginRoot);
        if (!File.Exists(packageJson))
        {
            return
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "PLG004",
                    Message = $"Node plugin '{pluginName}' is missing package.json.",
                    File = pluginRoot
                }
            ];
        }

        if (distPath is null || !File.Exists(distPath))
        {
            return
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "PLG005",
                    Message = $"Node plugin '{pluginName}' has not been built. Run npm install and npm run build in '{pluginRoot}'.",
                    File = pluginRoot
                }
            ];
        }

        return [];
    }

    private static NodeProcessJsonRpcClient CreateNodeClient(ProjectCatalog catalog, string pluginRoot)
    {
        if (NodeHostScriptPath is null)
        {
            throw new InvalidOperationException("The in-repo Node plugin host is missing.");
        }

        var entryPoint = ResolveNodeEntryPoint(pluginRoot);
        if (entryPoint is null || !File.Exists(entryPoint))
        {
            throw new InvalidOperationException($"Node plugin '{Path.GetFileName(pluginRoot)}' has not been built. Run npm install and npm run build in '{pluginRoot}'.");
        }

        return new NodeProcessJsonRpcClient(NodeHostScriptPath, pluginRoot);
    }

    private static string? ResolveNodeEntryPoint(string pluginRoot)
    {
        var packageJsonPath = Path.Combine(pluginRoot, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        if (document.RootElement.TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.String)
        {
            return Path.GetFullPath(Path.Combine(pluginRoot, main.GetString()!));
        }

        var defaultPath = Path.Combine(pluginRoot, "dist", "index.js");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    private static bool TryResolveNodePluginRoot(string projectRoot, string? pluginName, out string? pluginRoot)
    {
        pluginRoot = null;
        if (string.IsNullOrWhiteSpace(pluginName))
        {
            return false;
        }

        var candidate = Path.Combine(projectRoot, "steps", "node", pluginName);
        if (Directory.Exists(candidate))
        {
            pluginRoot = candidate;
            return true;
        }

        return false;
    }

    private static void WritePluginLogs(ICressLogger logger, IReadOnlyList<NodeLogEntry> logs)
    {
        foreach (var entry in logs)
        {
            switch (entry.Level?.Trim().ToLowerInvariant())
            {
                case "error":
                    logger.Error(entry.Message ?? string.Empty, entry.Data);
                    break;
                case "warning":
                    logger.Warning(entry.Message ?? string.Empty, entry.Data);
                    break;
                default:
                    logger.Info(entry.Message ?? string.Empty, entry.Data);
                    break;
            }
        }
    }

    private static DriverExecutionResult ToDriverResult(NodeExecutionResponse response, string artifactRoot)
    {
        var artifacts = (response.Artifacts ?? [])
            .Select(artifact => ValidateArtifact(artifactRoot, artifact))
            .Where(artifact => artifact is not null)
            .Select(artifact => artifact!)
            .ToList();

        return new DriverExecutionResult
        {
            Outcome = string.Equals(response.Status, "passed", StringComparison.OrdinalIgnoreCase) ? RunOutcome.Passed : RunOutcome.Failed,
            Message = response.Message,
            FailureClassification = response.FailureClassification,
            Outputs = response.Outputs ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Artifacts = artifacts
        };
    }

    private static EvidenceArtifact? ValidateArtifact(string artifactRoot, NodeArtifact artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.RelativePath))
        {
            return null;
        }

        var relativePath = artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(artifactRoot, relativePath));
        var root = Path.GetFullPath(artifactRoot);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Plugin artifact '{artifact.RelativePath}' escapes the artifact root.");
        }

        return new EvidenceArtifact
        {
            Category = artifact.Category ?? "plugin",
            RelativePath = relativePath,
            Description = artifact.Description
        };
    }

    private static StepHandlerRegistration? FindStepHandler(IEnumerable<ICressPluginModule> modules, string? operation)
        => modules
            .SelectMany(module => module.GetStepHandlers())
            .FirstOrDefault(item => item.Operation.Equals(operation, StringComparison.OrdinalIgnoreCase));

    private static FixtureProviderRegistration? FindFixtureProvider(IEnumerable<ICressPluginModule> modules, string? operation)
        => modules
            .SelectMany(module => module.GetFixtureProviders())
            .FirstOrDefault(item => item.Operation.Equals(operation, StringComparison.OrdinalIgnoreCase));

    private static StepExecutionContext CreateStepExecutionContext(PlanAction action, FlowExecutionContext context)
        => new()
        {
            FlowId = context.FlowId,
            StepName = action.Name,
            ArtifactDirectory = context.ArtifactRoot,
            BaseUrl = context.EffectiveConfig.Profile.BaseUrl,
            Inputs = action.Inputs,
            Variables = new Dictionary<string, string>(context.Variables, StringComparer.OrdinalIgnoreCase),
            Fixtures = new Dictionary<string, string>(context.Fixtures, StringComparer.OrdinalIgnoreCase),
            Logger = context.Logger,
            Drivers = context.Drivers
        };

    private static FixtureExecutionContext CreateFixtureExecutionContext(PlanAction action, FlowExecutionContext context)
        => new()
        {
            FlowId = context.FlowId,
            FixtureAlias = action.Inputs.TryGetValue("alias", out var alias) ? alias : action.Fixture ?? string.Empty,
            FixtureName = action.Fixture ?? string.Empty,
            ArtifactDirectory = context.ArtifactRoot,
            Bindings = action.Inputs,
            Variables = new Dictionary<string, string>(context.Variables, StringComparer.OrdinalIgnoreCase),
            Logger = context.Logger
        };

    private static DriverExecutionResult CreateMetadataOnlyFixtureResult()
        => new()
        {
            Outcome = RunOutcome.Passed,
            Message = "No fixture provider was configured; using metadata-only fixture resolution."
        };

    private static DriverExecutionResult CreateFailureResult(string message, string failureClassification)
        => new()
        {
            Outcome = RunOutcome.Failed,
            Message = message,
            FailureClassification = failureClassification
        };

    private static DriverExecutionResult ToDriverResult(StepExecutionResult result)
        => new()
        {
            Outcome = result.Success ? RunOutcome.Passed : RunOutcome.Failed,
            Message = result.Message,
            FailureClassification = result.FailureClassification,
            Outputs = result.Outputs,
            Artifacts = result.Artifacts
        };

    private static DriverExecutionResult ToDriverResult(FixtureExecutionResult result)
        => new()
        {
            Outcome = result.Success ? RunOutcome.Passed : RunOutcome.Failed,
            Message = result.Message,
            Outputs = result.Outputs,
            Artifacts = result.Artifacts
        };

    private sealed record NodeExecutionResponse(
        string Status,
        string? Message,
        string? FailureClassification,
        Dictionary<string, string>? Outputs,
        List<NodeArtifact>? Artifacts,
        List<NodeLogEntry>? Logs);

    private sealed record NodeArtifact(string? Category, string RelativePath, string? Description);
    private sealed record NodeLogEntry(string? Level, string? Message, Dictionary<string, string>? Data);
}
