using Cress.Companion;

namespace Cress.Companion.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var coordinator = new DesktopCompanionCoordinator(
            new RecordingSessionBackendFactory(),
            new ProcessCompanionTargetCatalog(),
            new ProcessWindowInspector(),
            new ScreenPreviewProvider(),
            new SystemCompanionClock());

        using var bridgeServer = new CompanionBridgeServer(coordinator, ResolvePort());
        bridgeServer.StartAsync().GetAwaiter().GetResult();

        using var appContext = new CompanionApplicationContext(
            coordinator,
            bridgeServer.BaseAddress,
            ResolveStudioUrl());

        Application.Run(appContext);
    }

    private static int ResolvePort()
        => int.TryParse(Environment.GetEnvironmentVariable("CRESS_COMPANION_PORT"), out var port) && port > 0
            ? port
            : 7321;

    private static string ResolveStudioUrl()
        => Environment.GetEnvironmentVariable("CRESS_STUDIO_URL")?.Trim()
           ?? "http://127.0.0.1:5076";
}
