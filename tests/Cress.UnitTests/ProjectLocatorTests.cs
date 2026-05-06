using Cress.ProjectSystem;

namespace Cress.UnitTests;

public sealed class ProjectLocatorTests
{
    [Fact]
    public void FindProjectRoot_WalksUpDirectoryTree()
    {
        using var workspace = new TestWorkspace();
        var projectRoot = workspace.GetPath("sample");
        var nested = Path.Combine(projectRoot, "flows", "example");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".cress"));
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(projectRoot, ".cress", "config.yaml"), "version: 1");

        var locator = new ProjectLocator();

        var result = locator.FindProjectRoot(nested);

        Assert.Equal(projectRoot, result);
    }
}
