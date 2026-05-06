using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Cress.Studio.Converters;

/// <summary>
/// Returns a small margin when the window is maximized to compensate for WPF's
/// WindowChrome overflow beyond the screen edge.
/// </summary>
[ValueConversion(typeof(WindowState), typeof(Thickness))]
public sealed class WindowStateToMarginConverter : IValueConverter
{
    public static readonly WindowStateToMarginConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is WindowState state && state == WindowState.Maximized)
        {
            // Standard 7 px compensation for maximized WPF windows with custom chrome.
            return new Thickness(7);
        }

        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
