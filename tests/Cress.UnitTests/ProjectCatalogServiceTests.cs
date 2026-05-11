using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Specs;

namespace Cress.UnitTests;

public sealed class ProjectCatalogServiceTests
{
    [Fact]
    public void Load_ReturnsDiagnosticWhenProjectRootCannotBeLocated()
    {
        using var workspace = new TestWorkspace();

        var result = CreateCatalogService().Load(workspace.RootPath);

        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("PRJ001", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Load_ReportsDuplicateFlowsCapabilitiesFixturesAndSteps()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "capabilities", "orders.md"), CapabilityMarkdown("orders", "Order placement"));
        workspace.WriteFile(Path.Combine("project", "capabilities", "orders-copy.md"), CapabilityMarkdown("orders", "Duplicate order placement"));
        workspace.WriteFile(Path.Combine("project", "flows", "checkout.flow.yaml"), FlowYaml("checkout", "Checkout", "orders", "smoke"));
        workspace.WriteFile(Path.Combine("project", "flows", "checkout-copy.flow.yaml"), FlowYaml("checkout", "Checkout duplicate", "orders", "regression"));
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "browser.yaml"), """
        version: 1
        steps:
          - name: browser.open
            aliases:
              - open
            implementation:
              plugin: sample
              operation: Execute
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "browser-duplicate.yaml"), """
        version: 1
        steps:
          - name: browser.open
            aliases:
              - open
            implementation:
              plugin: sample
              operation: Execute
          - name: browser.click
            aliases:
              - open
            implementation:
              plugin: sample
              operation: Execute
        """);
        workspace.WriteFile(Path.Combine("project", "fixtures", "customer.yaml"), """
        version: 1
        fixtures:
          customer:
            description: Customer fixture
            plugin: sample
            operation: Create
        """);
        workspace.WriteFile(Path.Combine("project", "fixtures", "customer-duplicate.yaml"), """
        version: 1
        fixtures:
          customer:
            description: Duplicate customer fixture
            plugin: sample
            operation: Create
        """);

        var result = CreateCatalogService().Load(workspace.GetPath("project"));

        Assert.NotNull(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FLW011");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CAP006");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FIX005");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REG001");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REG002");
        Assert.True(result.Value!.StepRegistry.TryResolve("open", out var definition));
        Assert.Equal("browser.open", definition.Name);
    }

    [Fact]
    public void SelectFlows_FiltersByPathAndTag_AndReportsMissingPaths()
    {
        using var workspace = new TestWorkspace();
        WriteProjectFiles(workspace);
        workspace.WriteFile(Path.Combine("project", "capabilities", "orders.md"), CapabilityMarkdown("orders", "Order placement"));
        workspace.WriteFile(Path.Combine("project", "flows", "checkout.flow.yaml"), FlowYaml("checkout", "Checkout", "orders", "smoke"));
        workspace.WriteFile(Path.Combine("project", "flows", "returns.flow.yaml"), FlowYaml("returns", "Returns", "orders", "regression"));
        workspace.WriteFile(Path.Combine("project", "steps", "manifests", "browser.yaml"), """
        version: 1
        steps:
          - name: browser.open
            implementation:
              plugin: sample
              operation: Execute
          - name: browser.done
            implementation:
              plugin: sample
              operation: Execute
        """);

        var catalog = CreateCatalogService().Load(workspace.GetPath("project")).Value!;
        var missingDiagnostics = new List<Diagnostic>();

        var byRelativePath = CreateCatalogService().SelectFlows(catalog, Path.Combine("flows", "checkout.flow.yaml"), "smoke");
        var byAbsolutePath = CreateCatalogService().SelectFlows(catalog, workspace.GetPath("project", "flows", "returns.flow.yaml"), null);
        var missing = CreateCatalogService().SelectFlows(catalog, Path.Combine("flows", "missing.flow.yaml"), null, missingDiagnostics);

        var checkout = Assert.Single(byRelativePath);
        Assert.Equal("checkout", checkout.FlowId);

        var returns = Assert.Single(byAbsolutePath);
        Assert.Equal("returns", returns.FlowId);

        Assert.Empty(missing);
        var diagnostic = Assert.Single(missingDiagnostics);
        Assert.Equal("SEL001", diagnostic.Code);
    }

    private static string CapabilityMarkdown(string id, string title)
        => $$"""
        ---
        version: 1
        id: {{id}}
        owner: QE
        risk: high
        ---

        # Capability: {{title}}

        ## Acceptance Criteria

        ### {{id.ToUpperInvariant()}}-1

        Works.
        """;

    private static string FlowYaml(string id, string name, string capability, string tag)
        => $$"""
        version: 1
        id: {{id}}
        name: {{name}}
        capability: {{capability}}
        tags:
          - {{tag}}
        when:
          - step: browser.open
        then:
          - expect: browser.done
        """;

    private static void WriteProjectFiles(TestWorkspace workspace)
    {
        workspace.WriteFile(Path.Combine("project", ".cress", "config.yaml"), """
        version: 1
        project:
          name: Catalog Project
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
        drivers:
          http:
            enabled: true
        """);
        workspace.WriteFile(Path.Combine("project", ".cress", "profiles", "local.yaml"), """
        profile: local
        baseUrl: http://localhost:5000
        variables:
          environment: local
        """);
        Directory.CreateDirectory(workspace.GetPath("project", "capabilities"));
        Directory.CreateDirectory(workspace.GetPath("project", "flows"));
        Directory.CreateDirectory(workspace.GetPath("project", "steps", "manifests"));
        Directory.CreateDirectory(workspace.GetPath("project", "fixtures"));
    }

    private static ProjectCatalogService CreateCatalogService()
    {
        var locator = new ProjectLocator();
        return new ProjectCatalogService(
            locator,
            new ConfigLoader(locator),
            new ProfileLoader(),
            new FlowParser(),
            new FlowNormalizer(),
            new CapabilityParser(),
            new StepManifestParser(),
            new FixtureManifestParser(),
            new StepRegistry());
    }
}
