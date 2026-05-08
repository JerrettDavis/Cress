using Cress.AppHost;

namespace Cress.AppHost.Tests;

public sealed class AppHostCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cress-apphost-tests", Guid.NewGuid().ToString("N"));

    public AppHostCompositionTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void IsDesktopStudioDisabled_parses_expected_values(string? value, bool expected)
        => Assert.Equal(expected, CressAppHostComposition.IsDesktopStudioDisabled(value));

    [Fact]
    public void FindRepoRoot_walks_up_to_solution_file()
    {
        var repoRoot = Path.Combine(_root, "repo");
        var nested = Path.Combine(repoRoot, "src", "Cress.AppHost", "bin", "Debug");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(repoRoot, "Cress.sln"), string.Empty);

        var resolved = CressAppHostComposition.FindRepoRoot(nested);

        Assert.Equal(repoRoot, resolved);
    }

    [Fact]
    public void ResolveStudioExecutablePath_prefers_release_output()
    {
        var studioProject = Path.Combine(_root, "src", "Cress.Studio");
        var debugExe = Path.Combine(studioProject, "bin", "Debug", "net10.0-windows", "Cress.Studio.exe");
        var releaseExe = Path.Combine(studioProject, "bin", "Release", "net10.0-windows", "Cress.Studio.exe");

        Directory.CreateDirectory(Path.GetDirectoryName(debugExe)!);
        Directory.CreateDirectory(Path.GetDirectoryName(releaseExe)!);
        File.WriteAllText(debugExe, "debug");
        File.WriteAllText(releaseExe, "release");

        var resolved = CressAppHostComposition.ResolveStudioExecutablePath(studioProject);

        Assert.Equal(releaseExe, resolved);
    }

    [Fact]
    public void ResolveSettings_can_disable_desktop_studio_for_headless_test_hosts()
    {
        var repoRoot = Path.Combine(_root, "repo");
        var studioProject = Path.Combine(repoRoot, "src", "Cress.Studio");
        var startDirectory = Path.Combine(repoRoot, "src", "Cress.AppHost");
        var studioExe = Path.Combine(studioProject, "bin", "Debug", "net10.0-windows", "Cress.Studio.exe");

        Directory.CreateDirectory(startDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(studioExe)!);
        File.WriteAllText(Path.Combine(repoRoot, "Cress.sln"), string.Empty);
        File.WriteAllText(studioExe, "debug");

        var settings = CressAppHostComposition.ResolveSettings(startDirectory, "1");

        Assert.Equal(repoRoot, settings.RepoRoot);
        Assert.Equal(studioProject, settings.StudioProjectDirectory);
        Assert.Equal(studioExe, settings.StudioExecutablePath);
        Assert.False(settings.IncludeDesktopStudio);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
