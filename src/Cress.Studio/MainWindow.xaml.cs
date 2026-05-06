using System.Windows;
using System.Windows.Interop;
using Cress.Studio.Interop;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;

namespace Cress.Studio;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        SourceInitialized += OnSourceInitialized;
        StateChanged += OnWindowStateChanged;
    }

    // ── Window control commands ────────────────────────────────────────────
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => SystemCommands.CloseWindow(this);

    // ── DWM dark mode wiring ───────────────────────────────────────────────
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyDwmDarkMode();

        if (Application.Current is App app && app.ThemeManager is { } mgr)
        {
            mgr.ThemeChanged += OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, StudioTheme theme)
        => ApplyDwmDarkMode();

    private void ApplyDwmDarkMode()
    {
        var isDark = false;
        if (Application.Current is App app && app.ThemeManager is { } mgr)
        {
            isDark = mgr.CurrentTheme == StudioTheme.Dark;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            DwmInterop.SetImmersiveDarkMode(hwnd, isDark);
        }
    }

    // ── Maximize margin update ─────────────────────────────────────────────
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // The binding on RootGrid.Margin handles this via converter,
        // but we raise the property on the window for any manual adjustments.
    }

    // ── Explorer tree ──────────────────────────────────────────────────────
    private void ProjectTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainWindowViewModel viewModel && e.NewValue is ExplorerNodeViewModel node)
        {
            viewModel.SelectNode(node);
        }
    }
}
