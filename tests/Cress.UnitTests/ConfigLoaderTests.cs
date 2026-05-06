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
  flaui:
    enabled: false
""");

        var loader = new ConfigLoader(new ProjectLocator());

        var result = loader.Load(projectRoot);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("Sample Project", result.Value!.Project.Name);
        Assert.True(result.Value.Drivers["http"].Enabled);
        Assert.False(result.Value.Drivers["flaui"].Enabled);
    }
}
