using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Execution.Drivers;
using Cress.ProjectSystem;
using Cress.Specs;

namespace Cress.UnitTests;

public sealed class ExecutionPipelineTests
{
    [Fact]
    public void PlanGenerator_ResolvesFixturesAndSteps()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "fixture.flow.yaml"), """
        version: 1
        id: fixture-flow
        name: Fixture flow
        fixtures:
          medication:
            use: medication.refillable
            for: patient
        when:
          - step: custom.action
            with:
              medication: medication
        then:
          - expect: custom.expectation
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "custom.yaml"), """
        version: 1
        steps:
          - name: custom.action
            inputs:
              medication:
                type: FixtureRef
                required: true
            retrySafe: true
            implementation:
              plugin: custom-action
              operation: Execute
          - name: custom.expectation
            retrySafe: true
            implementation:
              plugin: custom-expectation
              operation: Execute
        """);
        workspace.WriteFile(Path.Combine("project", "fixtures", "fixtures.yaml"), """
        version: 1
        fixtures:
          medication.refillable:
            type: domain.medication
            strategy: generated
            cleanup: always
        """);

        var catalog = CreateCatalogService().Load(workspace.GetPath("project"));
        var planGenerator = new PlanGenerator();

        var plan = planGenerator.Generate(catalog.Value!, catalog.Value!.NormalizedFlows);

        Assert.True(plan.Diagnostics.Count == 0);
        Assert.Single(plan.Plans);
        Assert.Collection(plan.Plans[0].Actions,
            action => Assert.Equal("setup", action.Kind),
            action => Assert.Equal("action", action.Kind),
            action => Assert.Equal("expectation", action.Kind),
            action => Assert.Equal("cleanup", action.Kind));
    }

    [Fact]
    public async Task RuntimeOrchestrator_RunsHttpFlowAndGeneratesReports()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: """
        headers:
          X-Test: integration
        """);
        workspace.WriteFile(Path.Combine("project", "flows", "http.flow.yaml"), """
        version: 1
        id: http-flow
        name: HTTP flow
        when:
          - step: api.request
            with:
              method: GET
              path: /health
        then:
          - expect: api.status_ok
            with:
              status: "200"
          - expect: api.body_ready
            with:
              text: ready
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "http.yaml"), """
        version: 1
        steps:
          - name: api.request
            drivers:
              - http
            retrySafe: true
            implementation:
              plugin: builtin.http
              operation: request
          - name: api.status_ok
            drivers:
              - http
            retrySafe: true
            implementation:
              plugin: builtin.http
              operation: assert-status
          - name: api.body_ready
            drivers:
              - http
            retrySafe: true
            implementation:
              plugin: builtin.http
              operation: assert-body-contains
        """);

        var orchestrator = CreateRuntimeOrchestrator(() => new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ready", Encoding.UTF8, "text/plain")
        }));

        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions
        {
            ReportFormats = ["html", "json", "junit", "markdown"]
        });

        Assert.True(result.Passed);
        Assert.Single(result.Flows);
        Assert.True(File.Exists(Path.Combine(result.Metadata.ArtifactRoot, "result.json")));
        Assert.True(Directory.Exists(Path.Combine(result.Metadata.ArtifactRoot, "api")));
        Assert.Contains("html", result.Reports.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("json", result.Reports.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("junit", result.Reports.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("markdown", result.Reports.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeOrchestrator_RunsRealPlaywrightFlow()
    {
        using var server = new SimpleHttpServer();
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, server.BaseUrl, profileExtras: """
        playwright:
          browser: chromium
          headless: true
        evidence:
          mode: full
          screenshots: true
        """);
        workspace.WriteFile(Path.Combine("project", "flows", "playwright.flow.yaml"), $"""
        version: 1
        id: playwright-flow
        name: Playwright flow
        when:
          - step: browser.open
            with:
              path: /home
          - step: browser.fill
            with:
              label: Name
              value: Ada
          - step: browser.click
            with:
              role: button
              name: Continue
          - step: browser.capture
            with:
              name: final-page
        then:
          - expect: browser.url
            with:
              equals: {server.BaseUrl}/done?name=Ada
          - expect: browser.text
            with:
              text: Hello Ada
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "playwright.yaml"), """
        version: 1
        steps:
          - name: browser.open
            drivers:
              - playwright
            retrySafe: true
            implementation:
              plugin: builtin.playwright
              operation: open
          - name: browser.fill
            drivers:
              - playwright
            retrySafe: true
            implementation:
              plugin: builtin.playwright
              operation: fill
          - name: browser.click
            drivers:
              - playwright
            retrySafe: true
            implementation:
              plugin: builtin.playwright
              operation: click
          - name: browser.capture
            drivers:
              - playwright
            retrySafe: true
            implementation:
              plugin: builtin.playwright
              operation: screenshot
          - name: browser.url
            drivers:
              - playwright
            retrySafe: true
            implementation:
              plugin: builtin.playwright
              operation: assert-url
          - name: browser.text
            drivers:
              - playwright
            retrySafe: true
            implementation:
              plugin: builtin.playwright
              operation: assert-text
        """);

        var orchestrator = CreateRuntimeOrchestrator();

        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.True(result.Passed, string.Join(" | ", result.Flows.Select(flow => flow.FailureMessage)));
        Assert.True(Directory.Exists(Path.Combine(result.Metadata.ArtifactRoot, "screenshots")));
        Assert.True(File.Exists(Path.Combine(result.Metadata.ArtifactRoot, "traces", "playwright-flow-trace.zip")));
        Assert.True(Directory.Exists(Path.Combine(result.Metadata.ArtifactRoot, "videos")));
    }

    [Fact]
    public async Task RuntimeOrchestrator_RunsDotNetPluginStep()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "dotnet.flow.yaml"), """
        version: 1
        id: dotnet-plugin-flow
        name: DotNet plugin flow
        when:
          - step: custom.dotnet_action
            with:
              text: hello
        then:
          - expect: custom.dotnet_assert
            with:
              expected: HELLO
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "dotnet.yaml"), """
        version: 1
        steps:
          - name: custom.dotnet_action
            retrySafe: true
            implementation:
              plugin: custom-dotnet
              operation: Execute
          - name: custom.dotnet_assert
            retrySafe: true
            implementation:
              plugin: custom-dotnet
              operation: Assert
        """);

        var pluginRoot = workspace.GetPath("project", "steps", "dotnet", "custom-dotnet");
        Directory.CreateDirectory(pluginRoot);
        var sdkReference = Path.Combine(GetRepositoryRoot(), "src", "Cress.Sdk", "Cress.Sdk.csproj");
        workspace.WriteFile(Path.Combine("project", "steps", "dotnet", "custom-dotnet", "custom-dotnet.csproj"), $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="{{sdkReference}}" />
          </ItemGroup>
        </Project>
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "dotnet", "custom-dotnet", "CustomDotnetModule.cs"), """
        using Cress.Sdk;

        namespace GeneratedSteps;

        public sealed class CustomDotnetModule : ICressPluginModule
        {
            public IEnumerable<StepHandlerRegistration> GetStepHandlers()
            {
                yield return new StepHandlerRegistration("Execute", ExecuteAsync);
                yield return new StepHandlerRegistration("Assert", AssertAsync);
            }

            private static Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken)
            {
                var output = context.GetRequiredInput("text").ToUpperInvariant();
                context.Logger.Info("Uppercased text.", new Dictionary<string, string> { ["value"] = output });
                return Task.FromResult(new StepExecutionResult
                {
                    Success = true,
                    Message = "DotNet action ran.",
                    Outputs = new Dictionary<string, string> { ["dotnetValue"] = output }
                });
            }

            private static Task<StepExecutionResult> AssertAsync(StepExecutionContext context, CancellationToken cancellationToken)
            {
                var expected = context.GetRequiredInput("expected");
                var actual = context.Variables.TryGetValue("dotnetValue", out var value) ? value : string.Empty;
                return Task.FromResult(new StepExecutionResult
                {
                    Success = string.Equals(expected, actual, StringComparison.Ordinal),
                    Message = string.Equals(expected, actual, StringComparison.Ordinal)
                        ? "DotNet assertion passed."
                        : $"Expected '{expected}', but found '{actual}'.",
                    FailureClassification = string.Equals(expected, actual, StringComparison.Ordinal) ? null : "assertion-failed"
                });
            }
        }
        """);

        await RunProcessAsync("dotnet", workspace.GetPath("project"), "build", Path.Combine(pluginRoot, "custom-dotnet.csproj"), "-v", "minimal");

        var orchestrator = CreateRuntimeOrchestrator();
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.True(result.Passed, string.Join(" | ", result.Flows.Select(flow => flow.FailureMessage)));
    }

    [Fact]
    public async Task RuntimeOrchestrator_RunsTypeScriptPluginStep()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "node.flow.yaml"), """
        version: 1
        id: node-plugin-flow
        name: Node plugin flow
        when:
          - step: custom.node_action
            with:
              text: hello
        then:
          - expect: custom.node_assert
            with:
              expected: HELLO
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "node.yaml"), """
        version: 1
        steps:
          - name: custom.node_action
            retrySafe: true
            implementation:
              plugin: custom-node
              operation: Execute
          - name: custom.node_assert
            retrySafe: true
            implementation:
              plugin: custom-node
              operation: Assert
        """);

        var pluginRoot = workspace.GetPath("project", "steps", "node", "custom-node");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "src"));
        var sdkDependency = GetNodeSdkDependency(pluginRoot);
        workspace.WriteFile(Path.Combine("project", "steps", "node", "custom-node", "package.json"), $$"""
        {
          "name": "custom-node",
          "private": true,
          "type": "module",
          "main": "dist/index.js",
          "types": "dist/index.d.ts",
          "scripts": {
            "build": "tsc -p tsconfig.json"
          },
          "dependencies": {
            "@cress/sdk": "{{sdkDependency}}"
          },
          "devDependencies": {
            "typescript": "^5.6.3"
          }
        }
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "node", "custom-node", "tsconfig.json"), """
        {
          "compilerOptions": {
            "target": "ES2022",
            "module": "NodeNext",
            "moduleResolution": "NodeNext",
            "declaration": true,
            "outDir": "dist",
            "strict": true
          },
          "include": ["src/**/*.ts"]
        }
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "node", "custom-node", "src", "index.ts"), """
        import { createPluginModule, createStepResult, defineStep, type StepExecutionContext } from "@cress/sdk";

        const execute = async (context: StepExecutionContext) => {
          const value = context.inputs.text.toUpperCase();
          context.logger.info("Uppercased text.", { value });
          return createStepResult({
            success: true,
            message: "Node action ran.",
            outputs: { nodeValue: value }
          });
        };

        const assertValue = async (context: StepExecutionContext) => {
          const actual = context.variables.nodeValue ?? "";
          const expected = context.inputs.expected;
          return createStepResult({
            success: actual === expected,
            message: actual === expected ? "Node assertion passed." : `Expected '${expected}', but found '${actual}'.`,
            failureClassification: actual === expected ? undefined : "assertion-failed"
          });
        };

        export default createPluginModule({
          steps: [defineStep("Execute", execute), defineStep("Assert", assertValue)]
        });
        """);

        await RunNpmAsync(pluginRoot, "install", "--no-fund", "--no-audit");
        await RunTypeScriptBuildAsync(pluginRoot);

        var orchestrator = CreateRuntimeOrchestrator();
        var result = await orchestrator.ExecuteAsync(workspace.GetPath("project"), new RunOptions());

        Assert.True(result.Passed, string.Join(" | ", result.Flows.Select(flow => flow.FailureMessage)));
    }

    [Fact]
    public async Task StepStubGenerator_GeneratedDotNetStubsBuild()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "generated.flow.yaml"), """
        version: 1
        id: generated-flow
        name: Generated flow
        when:
          - step: custom.generated_step
            with:
              customerId: "42"
        then:
          - expect: custom.generated_step
            with:
              customerId: "42"
        """);

        var catalogService = CreateCatalogService();
        var generator = new StepStubGenerator();
        var catalog = catalogService.Load(workspace.GetPath("project"));
        var generation = generator.Generate(catalog.Value!, catalog.Value!.NormalizedFlows, "dotnet", force: true);

        Assert.True(generation.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error));

        foreach (var projectFile in generation.Value!.Files.Where(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            await RunProcessAsync("dotnet", workspace.GetPath("project"), "build", projectFile, "-v", "minimal");
        }
    }

    [Fact]
    public async Task StepStubGenerator_GeneratedTypeScriptStubsBuild()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "generated.flow.yaml"), """
        version: 1
        id: generated-flow
        name: Generated flow
        when:
          - step: custom.generated_step
            with:
              customerId: "42"
        then:
          - expect: custom.generated_step
            with:
              customerId: "42"
        """);

        var catalogService = CreateCatalogService();
        var generator = new StepStubGenerator();
        var catalog = catalogService.Load(workspace.GetPath("project"));
        var generation = generator.Generate(catalog.Value!, catalog.Value!.NormalizedFlows, "typescript", force: true);

        Assert.True(generation.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error));

        foreach (var packageFile in generation.Value!.Files.Where(file => file.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            var pluginRoot = Path.GetDirectoryName(packageFile)!;
            await RunNpmAsync(pluginRoot, "install", "--no-fund", "--no-audit");
            await RunTypeScriptBuildAsync(pluginRoot);
            Assert.True(File.Exists(Path.Combine(pluginRoot, "dist", "index.js")));
        }
    }

    [Fact]
    public void StepStubGenerator_UnsupportedLanguage_ReturnsDiagnostic()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "generated.flow.yaml"), """
        version: 1
        id: generated-flow
        name: Generated flow
        when:
          - step: custom.generated_step
        """);

        var catalogService = CreateCatalogService();
        var generator = new StepStubGenerator();
        var catalog = catalogService.Load(workspace.GetPath("project"));
        var generation = generator.Generate(catalog.Value!, catalog.Value!.NormalizedFlows, "python", force: false);

        var diagnostic = Assert.Single(generation.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("GEN001", diagnostic.Code);
        Assert.Equal(workspace.GetPath("project"), diagnostic.File);
        Assert.Empty(generation.Value!.Files);
        Assert.Equal(["custom.generated_step"], generation.Value.Steps);
    }

    [Fact]
    public void StepStubGenerator_DotNetStubAlreadyExists_WithoutForce_ReturnsWarning()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "generated.flow.yaml"), """
        version: 1
        id: generated-flow
        name: Generated flow
        when:
          - step: custom.generated_step
        """);

        var pluginRoot = workspace.GetPath("project", "steps", "dotnet", "customgeneratedstep");
        Directory.CreateDirectory(pluginRoot);

        var catalogService = CreateCatalogService();
        var generator = new StepStubGenerator();
        var catalog = catalogService.Load(workspace.GetPath("project"));
        var generation = generator.Generate(catalog.Value!, catalog.Value!.NormalizedFlows, "dotnet", force: false);

        var diagnostic = Assert.Single(generation.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("GEN002", diagnostic.Code);
        Assert.Equal(pluginRoot, diagnostic.File);
        Assert.Empty(generation.Value!.Files);
    }

    [Fact]
    public void StepStubGenerator_TypeScriptStubAlreadyExists_WithoutForce_ReturnsWarning()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "generated.flow.yaml"), """
        version: 1
        id: generated-flow
        name: Generated flow
        when:
          - step: custom.generated_step
        """);

        var pluginRoot = workspace.GetPath("project", "steps", "node", "customgeneratedstep");
        Directory.CreateDirectory(pluginRoot);

        var catalogService = CreateCatalogService();
        var generator = new StepStubGenerator();
        var catalog = catalogService.Load(workspace.GetPath("project"));
        var generation = generator.Generate(catalog.Value!, catalog.Value!.NormalizedFlows, "typescript", force: false);

        var diagnostic = Assert.Single(generation.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("GEN003", diagnostic.Code);
        Assert.Equal(pluginRoot, diagnostic.File);
        Assert.Empty(generation.Value!.Files);
    }

    [Fact]
    public void StepStubGenerator_DotNetStubWithoutInputs_OmitsInputsBlockFromManifest()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace, "http://localhost:5000", profileExtras: string.Empty);
        workspace.WriteFile(Path.Combine("project", "flows", "generated.flow.yaml"), """
        version: 1
        id: generated-flow
        name: Generated flow
        when:
          - step: custom.generated_step
        """);

        var catalogService = CreateCatalogService();
        var generator = new StepStubGenerator();
        var catalog = catalogService.Load(workspace.GetPath("project"));
        var generation = generator.Generate(catalog.Value!, catalog.Value!.NormalizedFlows, "dotnet", force: true);

        Assert.DoesNotContain(generation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var manifestPath = workspace.GetPath("project", "steps", "manifests", "generated", "customgeneratedstep.yaml");
        var manifest = File.ReadAllText(manifestPath);
        Assert.DoesNotContain("inputs:", manifest, StringComparison.Ordinal);
        Assert.Contains("operation: \"Execute\"", manifest, StringComparison.Ordinal);
    }

    private static RuntimeOrchestrator CreateRuntimeOrchestrator(Func<HttpMessageHandler>? httpHandlerFactory = null)
    {
        var configLoader = new ConfigLoader(new ProjectLocator());
        var catalogService = CreateCatalogService();
        return new RuntimeOrchestrator(
            catalogService,
            new PlanGenerator(),
            configLoader,
            new PluginHost(),
            new ReportGenerator(),
            [
                new HttpRuntimeDriver(httpHandlerFactory),
                new PlaywrightRuntimeDriver()
            ]);
    }

    private static ProjectCatalogService CreateCatalogService()
    {
        var locator = new ProjectLocator();
        var configLoader = new ConfigLoader(locator);
        var profileLoader = new ProfileLoader();
        var flowParser = new FlowParser();
        var flowNormalizer = new FlowNormalizer();
        var capabilityParser = new CapabilityParser();
        var stepParser = new StepManifestParser();
        var fixtureParser = new FixtureManifestParser();
        return new ProjectCatalogService(
            locator,
            configLoader,
            profileLoader,
            flowParser,
            flowNormalizer,
            capabilityParser,
            stepParser,
            fixtureParser,
            new StepRegistry());
    }

    private static void WriteProjectFiles(TestWorkspace workspace, string baseUrl, string profileExtras)
    {
        workspace.WriteFile(Path.Combine("project", ".cress", "config.yaml"), """
        version: 1
        project:
          name: Sample Project
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: artifacts/runs
          reports: reports
        defaults:
          timeout: 30000
          retries: 0
          evidence: standard
          cleanup: on-success
        plugins:
          discover:
            - plugins
            - steps
        drivers:
          http:
            enabled: true
          playwright:
            enabled: true
        """);
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "local.yaml"), $$"""
        profile: local
        baseUrl: {{baseUrl}}
        {{profileExtras}}
        variables:
          environment: test
        """);
        Directory.CreateDirectory(workspace.GetPath("project", "steps", "manifests"));
        Directory.CreateDirectory(workspace.GetPath("project", "fixtures"));
    }

    private static async Task RunNpmAsync(string workingDirectory, params string[] npmArguments)
        => await RunProcessAsync(GetNodeExecutablePath(), workingDirectory, [GetNpmCliPath(), .. npmArguments]);

    private static async Task RunTypeScriptBuildAsync(string workingDirectory)
        => await RunProcessAsync(GetNodeExecutablePath(), workingDirectory, Path.Combine(workingDirectory, "node_modules", "typescript", "bin", "tsc"), "-p", "tsconfig.json");

    private static async Task RunProcessAsync(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static string GetNodeSdkDependency(string pluginRoot)
    {
        var sdkRoot = Path.Combine(GetRepositoryRoot(), "node", "cress-sdk");
        var relative = Path.GetRelativePath(pluginRoot, sdkRoot).Replace('\\', '/');
        return relative.StartsWith(".", StringComparison.Ordinal) ? $"file:{relative}" : $"file:./{relative}";
    }

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        return TestWorkspace.ResolveRepositoryRoot(sourceFilePath);
    }

    private static string GetNodeExecutablePath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");

    private static string GetNpmCliPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node_modules", "npm", "bin", "npm-cli.js");

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class SimpleHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serverLoop;

        public SimpleHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUrl = $"http://127.0.0.1:{endpoint.Port}";
            _serverLoop = Task.Run(() => RunAsync(_cts.Token));
        }

        public string BaseUrl { get; }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
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
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }

                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
                {
                }

                var segments = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var target = segments.Length > 1 ? segments[1] : "/";
                var requestUri = new Uri($"{BaseUrl}{target}");
                var responseBody = BuildHtmlResponse(requestUri);
                var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {responseBytes.Length}\r\nConnection: close\r\n\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(header);
                await stream.WriteAsync(headerBytes, cancellationToken);
                await stream.WriteAsync(responseBytes, cancellationToken);
            }
        }

        private static string BuildHtmlResponse(Uri uri)
        {
            if (uri.AbsolutePath.Equals("/done", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractQueryValue(uri.Query, "name");
                return $$"""
                <!DOCTYPE html>
                <html lang="en">
                <body>
                  <h1>Saved</h1>
                  <p>Hello {{WebUtility.HtmlEncode(name)}}</p>
                </body>
                </html>
                """;
            }

            return """
            <!DOCTYPE html>
            <html lang="en">
            <body>
              <h1>Welcome</h1>
              <form action="/done" method="get">
                <label for="name">Name</label>
                <input id="name" name="name" />
                <button type="submit">Continue</button>
              </form>
            </body>
            </html>
            """;
        }

        private static string ExtractQueryValue(string query, string key)
        {
            var trimmed = query.TrimStart('?');
            foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length > 0 && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty);
                }
            }

            return string.Empty;
        }
    }
}
