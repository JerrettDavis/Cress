using Cress.Studio.Launcher;

namespace Cress.Studio.Web.Tests;

public sealed class StudioLaunchOptionsTests
{
    [Fact]
    public void Parse_DefaultsToDesktopMode()
    {
        var options = StudioLaunchOptions.Parse([]);

        Assert.Equal(StudioLaunchMode.Desktop, options.Mode);
        Assert.True(options.LaunchBrowserClient);
        Assert.Null(options.Port);
        Assert.Null(options.WebRootPath);
    }

    [Fact]
    public void Parse_ReadsBrowserModePortAndWebRoot()
    {
        var options = StudioLaunchOptions.Parse(["--mode", "browser", "--port", "5099", "--web-root", @"C:\studio\web"]);

        Assert.Equal(StudioLaunchMode.Browser, options.Mode);
        Assert.Equal(5099, options.Port);
        Assert.Equal(@"C:\studio\web", options.WebRootPath);
        Assert.True(options.LaunchBrowserClient);
    }

    [Fact]
    public void Parse_DisablesBrowserAutoOpenWhenRequested()
    {
        var options = StudioLaunchOptions.Parse(["--browser", "--no-open-browser"]);

        Assert.Equal(StudioLaunchMode.Browser, options.Mode);
        Assert.False(options.LaunchBrowserClient);
    }

    [Fact]
    public void Parse_ThrowsUsageExceptionForHelp()
    {
        Assert.Throws<StudioUsageException>(() => StudioLaunchOptions.Parse(["--help"]));
        Assert.Throws<StudioUsageException>(() => StudioLaunchOptions.Parse(["-h"]));
        Assert.Throws<StudioUsageException>(() => StudioLaunchOptions.Parse(["/?"]));
    }

    [Theory]
    [InlineData("--mode", "service")]
    [InlineData("--port", "0")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "not-a-number")]
    public void Parse_RejectsInvalidValues(string option, string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => StudioLaunchOptions.Parse([option, value]));

        Assert.NotEmpty(exception.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownOption()
    {
        var exception = Assert.Throws<ArgumentException>(() => StudioLaunchOptions.Parse(["--wat"]));

        Assert.Contains("--wat", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveWebRoot_UsesExplicitPathWhenBundleExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(tempRoot, "bundle");
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, StudioBundleLocator.StudioExecutableName), string.Empty);

        try
        {
            var resolved = StudioBundleLocator.ResolveWebRoot(webRoot, baseDirectory: tempRoot);

            Assert.Equal(webRoot, resolved);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveWebRoot_FallsBackToStudioFolderUnderBaseDirectory()
    {
        using var tempRoot = new TemporaryBundleRoot();
        var studioWebRoot = tempRoot.CreateBundle("studio", "web");

        var resolved = StudioBundleLocator.ResolveWebRoot(baseDirectory: tempRoot.RootPath);

        Assert.Equal(studioWebRoot, resolved);
    }

    [Fact]
    public void ResolveWebRoot_FallsBackToArtifactsFolderInAncestorDirectory()
    {
        using var tempRoot = new TemporaryBundleRoot();
        var nestedBaseDirectory = Path.Combine(tempRoot.RootPath, "one", "two", "three");
        Directory.CreateDirectory(nestedBaseDirectory);
        var artifactsWebRoot = tempRoot.CreateBundle("artifacts", "studio-publish", "web");

        var resolved = StudioBundleLocator.ResolveWebRoot(baseDirectory: nestedBaseDirectory);

        Assert.Equal(artifactsWebRoot, resolved);
    }

    [Fact]
    public void ResolveWebRoot_ThrowsWhenNoBundleCanBeFound()
    {
        using var tempRoot = new TemporaryBundleRoot();

        var exception = Assert.Throws<DirectoryNotFoundException>(() => StudioBundleLocator.ResolveWebRoot(baseDirectory: tempRoot.RootPath));

        Assert.Contains("Publish-StudioInstaller.ps1", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryBundleRoot : IDisposable
    {
        public TemporaryBundleRoot()
        {
            RootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateBundle(params string[] segments)
        {
            var bundlePath = Path.Combine([RootPath, .. segments]);
            Directory.CreateDirectory(bundlePath);
            File.WriteAllText(Path.Combine(bundlePath, StudioBundleLocator.StudioExecutableName), string.Empty);
            return bundlePath;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
