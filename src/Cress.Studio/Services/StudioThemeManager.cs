using System.Windows;
using Microsoft.Win32;

namespace Cress.Studio.Services;

public enum StudioTheme
{
    Light,
    Dark
}

public sealed class StudioThemeManager : IDisposable
{
    public const string SharedThemePath = "/Cress.Studio;component/Themes/StudioTheme.Shared.xaml";
    public const string LightThemePath = "/Cress.Studio;component/Themes/StudioTheme.Light.xaml";
    public const string DarkThemePath = "/Cress.Studio;component/Themes/StudioTheme.Dark.xaml";

    private readonly Application _application;
    private StudioTheme _currentTheme;

    /// <summary>
    /// Raised on the UI thread whenever the active theme changes.
    /// </summary>
    public event EventHandler<StudioTheme>? ThemeChanged;

    public StudioTheme CurrentTheme => _currentTheme;

    public StudioThemeManager(Application application)
    {
        _application = application;
    }

    public void Start()
    {
        _currentTheme = DetectSystemTheme();
        ApplyTheme(_application.Resources, _currentTheme);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    public static StudioTheme ResolveTheme(int? appsUseLightTheme)
        => appsUseLightTheme == 0 ? StudioTheme.Dark : StudioTheme.Light;

    public static StudioTheme DetectSystemTheme()
    {
        const string personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        using var key = Registry.CurrentUser.OpenSubKey(personalizeKey);
        int? appsUseLightTheme = key?.GetValue("AppsUseLightTheme") is int value ? value : null;
        return ResolveTheme(appsUseLightTheme);
    }

    public static string GetThemeDictionaryPath(StudioTheme theme)
        => theme == StudioTheme.Light ? LightThemePath : DarkThemePath;

    public static void ApplyTheme(ResourceDictionary resources, StudioTheme theme)
    {
        var existingTheme = resources.MergedDictionaries
            .FirstOrDefault(dictionary => dictionary.Source is not null
                && dictionary.Source.OriginalString.Contains("StudioTheme.", StringComparison.OrdinalIgnoreCase)
                && !dictionary.Source.OriginalString.Contains("StudioTheme.Shared", StringComparison.OrdinalIgnoreCase));

        if (existingTheme is null)
        {
            existingTheme = resources.MergedDictionaries
                .FirstOrDefault(dictionary => dictionary.Contains("StudioThemeName"));
        }

        if (existingTheme is not null)
        {
            resources.MergedDictionaries.Remove(existingTheme);
        }

        var themeDictionary = (ResourceDictionary)Application.LoadComponent(new Uri(GetThemeDictionaryPath(theme), UriKind.Relative));
        resources.MergedDictionaries.Add(themeDictionary);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        _application.Dispatcher.Invoke(() =>
        {
            var newTheme = DetectSystemTheme();
            ApplyTheme(_application.Resources, newTheme);
            if (newTheme != _currentTheme)
            {
                _currentTheme = newTheme;
                ThemeChanged?.Invoke(this, newTheme);
            }
        });
    }
}
