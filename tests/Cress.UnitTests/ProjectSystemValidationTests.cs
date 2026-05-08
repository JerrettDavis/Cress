using Cress.Core.Models;
using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Validation;

namespace Cress.UnitTests;

public sealed class ProjectSystemValidationTests
{
    [Fact]
    public void ConfigLoader_LoadFile_reports_missing_file()
    {
        using var workspace = new TestWorkspace();
        var loader = new ConfigLoader(new ProjectLocator());

        var result = loader.LoadFile(workspace.GetPath("missing", "config.yaml"));

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CFG001", diagnostic.Code);
    }

    [Fact]
    public void ConfigLoader_LoadFile_reports_invalid_yaml_location()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("project", ".cress", "config.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "version: [");

        var loader = new ConfigLoader(new ProjectLocator());

        var result = loader.LoadFile(path);

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CFG002", diagnostic.Code);
        Assert.NotNull(diagnostic.Line);
        Assert.NotNull(diagnostic.Column);
        Assert.Contains("config.yaml", diagnostic.File, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigLoader_LoadFile_reports_required_field_errors()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("project", ".cress", "config.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        version: 0
        project:
          name: ""
          defaultProfile: ""
        paths:
          capabilities: ""
          flows: ""
          models: ""
          fixtures: ""
          steps: ""
          artifacts: ""
          reports: ""
        """);

        var loader = new ConfigLoader(new ProjectLocator());

        var result = loader.LoadFile(path);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CFG003");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CFG004");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CFG005");
        Assert.Equal(7, result.Diagnostics.Count(diagnostic => diagnostic.Code == "CFG006"));
    }

    [Fact]
    public void ProfileLoader_LoadAll_reports_missing_directory()
    {
        using var workspace = new TestWorkspace();
        var loader = new ProfileLoader();

        var results = loader.LoadAll(workspace.GetPath("project"));

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Equal("PRF001", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void ProfileLoader_Load_uses_file_name_when_profile_name_is_blank()
    {
        using var workspace = new TestWorkspace();
        var profilePath = workspace.GetPath("project", ".cress", "profiles", "qa.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(profilePath, """
        baseUrl: https://example.test
        playwright:
          browser: chromium
          headless: true
        """);

        var loader = new ProfileLoader();

        var result = loader.Load(workspace.GetPath("project"), "qa");

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("qa", result.Value!.Profile);
        Assert.Equal("https://example.test", result.Value.BaseUrl);
        Assert.Equal("chromium", result.Value.Playwright!.Browser);
        Assert.True(result.Value.Playwright.Headless ?? false);
    }

    [Fact]
    public void ProjectValidator_reports_missing_project_root()
    {
        var validator = CreateValidator();

        var result = validator.Validate(Path.GetTempPath());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("PRJ001", diagnostic.Code);
    }

    [Fact]
    public void ProjectValidator_reports_duplicate_flow_step_and_fixture_definitions()
    {
        using var workspace = new TestWorkspace();
        WriteProjectLayout(workspace);
        workspace.WriteFile(Path.Combine("project", "flows", "first.flow.yaml"), """
        version: 1
        id: duplicate-flow
        name: First flow
        when:
          - step: http.get
            with:
              url: https://example.test/one
        then:
          - expect: http.assert-status
            with:
              status: "200"
        """);
        workspace.WriteFile(Path.Combine("project", "flows", "second.flow.yaml"), """
        version: 1
        id: duplicate-flow
        name: Second flow
        when:
          - step: http.get
            with:
              url: https://example.test/two
        then:
          - expect: http.assert-status
            with:
              status: "200"
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "http-a.yaml"), """
        version: 1
        steps:
          - name: shared.step
            implementation:
              plugin: builtin.http
              operation: get
        """);
        workspace.WriteFile(Path.Combine("project", "steps", "http-b.yaml"), """
        version: 1
        steps:
          - name: shared.step
            implementation:
              plugin: builtin.http
              operation: status
        """);
        workspace.WriteFile(Path.Combine("project", "fixtures", "a.yaml"), """
        version: 1
        fixtures:
          shared.fixture:
            type: seed.customer
            strategy: static
        """);
        workspace.WriteFile(Path.Combine("project", "fixtures", "b.yaml"), """
        version: 1
        fixtures:
          shared.fixture:
            type: seed.order
            strategy: static
        """);

        var validator = CreateValidator();

        var result = validator.Validate(workspace.GetPath("project"));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PRF001");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FLW011");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REG001");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FIX005");
    }

    private static ProjectValidator CreateValidator()
    {
        var locator = new ProjectLocator();
        return new ProjectValidator(
            locator,
            new ConfigLoader(locator),
            new ProfileLoader(),
            new FlowParser(),
            new CapabilityParser(),
            new StepManifestParser(),
            new FixtureManifestParser());
    }

    private static void WriteProjectLayout(TestWorkspace workspace)
    {
        workspace.WriteFile(Path.Combine("project", ".cress", "config.yaml"), """
        version: 1
        project:
          name: Validation sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: artifacts
          reports: reports
        """);
        workspace.WriteFile(Path.Combine("project", "capabilities", "search.md"), """
        ---
        version: 1
        id: capability.search
        owner: qa
        risk: medium
        ---

        # Search capability

        ## Rules
        - Return relevant results.
        """);
    }
}
