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
}
