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

    [Fact]
    public void FindProjectRoot_ReturnsNullForBlankStartDirectory()
    {
        var locator = new ProjectLocator();

        Assert.Null(locator.FindProjectRoot(""));
        Assert.Null(locator.FindProjectRoot("   "));
    }

    [Fact]
    public void TryFindProjectRoot_ReturnsFalseAndEmptyValueWhenProjectIsMissing()
    {
        using var workspace = new TestWorkspace();
        var locator = new ProjectLocator();

        var found = locator.TryFindProjectRoot(workspace.GetPath("missing"), out var projectRoot);

        Assert.False(found);
        Assert.Equal(string.Empty, projectRoot);
    }

    [Fact]
    public void GetConfigPath_ReturnsCressConfigLocation()
    {
        var locator = new ProjectLocator();

        Assert.Equal(
            Path.Combine(@"C:\repo\sample", ".cress", "config.yaml"),
            locator.GetConfigPath(@"C:\repo\sample"));
    }
}
