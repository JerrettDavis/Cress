using System.Windows;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private StudioThemeManager? _themeManager;

    public StudioThemeManager? ThemeManager => _themeManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _themeManager = new StudioThemeManager(this);
        _themeManager.Start();
        _serviceProvider = ConfigureServices();
        var window = _serviceProvider.GetRequiredService<MainWindow>();
        if (window.DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Initialize(e.Args.FirstOrDefault());
        }

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _themeManager?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection()
            .AddCressStudioBackend()
            .AddSingleton<MainWindowViewModel>()
            .AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
