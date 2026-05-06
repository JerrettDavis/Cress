using System.Windows;
using Cress.ServiceDefaults;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cress.Studio;

public partial class App : Application
{
    private IHost? _host;
    private StudioThemeManager? _themeManager;

    public StudioThemeManager? ThemeManager => _themeManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _themeManager = new StudioThemeManager(this);
        _themeManager.Start();
        _host = ConfigureHost(e.Args);
        _host.StartAsync().GetAwaiter().GetResult();
        var window = _host.Services.GetRequiredService<MainWindow>();
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
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static IHost ConfigureHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddServiceDefaults();
        builder.Services
            .AddCressStudioBackend()
            .AddSingleton<MainWindowViewModel>()
            .AddSingleton<MainWindow>();

        return builder.Build();
    }
}
