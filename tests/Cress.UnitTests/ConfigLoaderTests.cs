using Cress.ProjectSystem;

namespace Cress.UnitTests;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_ReturnsConfigModel()
    {
        using var workspace = new TestWorkspace();
        var projectRoot = workspace.GetPath("project");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".cress"));
        File.WriteAllText(Path.Combine(projectRoot, ".cress", "config.yaml"), """
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
  flawright:
    enabled: false
""");

        var loader = new ConfigLoader(new ProjectLocator());

        var result = loader.Load(projectRoot);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("Sample Project", result.Value!.Project.Name);
        Assert.True(result.Value.Drivers["http"].Enabled);
        Assert.False(result.Value.Drivers["flawright"].Enabled);
    }

    [Fact]
    public void LoadFile_uses_empty_config_when_yaml_document_is_empty()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("project", ".cress", "config.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);

        var result = new ConfigLoader(new ProjectLocator()).LoadFile(path);

        Assert.NotNull(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CFG003");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CFG004");
        Assert.Equal(string.Empty, result.Value!.Project.Name);
    }

    [Fact]
    public void Serialize_uses_camel_case_yaml_names()
    {
        var yaml = new ConfigLoader(new ProjectLocator()).Serialize(new
        {
            ProjectName = "Sample",
            DefaultProfile = "local"
        });

        Assert.Contains("projectName: Sample", yaml);
        Assert.Contains("defaultProfile: local", yaml);
    }
}
