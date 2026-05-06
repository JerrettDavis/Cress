using System.Windows;
using Cress.Studio.Services;

namespace Cress.Studio.E2ETests;

public sealed class StudioThemeManagerTests
{
    [Theory]
    [InlineData(null, StudioTheme.Light)]
    [InlineData(1, StudioTheme.Light)]
    [InlineData(0, StudioTheme.Dark)]
    public void ResolveTheme_maps_registry_values(int? appsUseLightTheme, StudioTheme expected)
    {
        Assert.Equal(expected, StudioThemeManager.ResolveTheme(appsUseLightTheme));
    }

    [Fact]
    public void ApplyTheme_replaces_existing_theme_dictionary()
    {
        var resources = new ResourceDictionary();

        StudioThemeManager.ApplyTheme(resources, StudioTheme.Light);

        Assert.Equal("Light", resources["StudioThemeName"]);

        StudioThemeManager.ApplyTheme(resources, StudioTheme.Dark);

        Assert.Equal("Dark", resources["StudioThemeName"]);
        Assert.Single(resources.MergedDictionaries);
    }
}
