using Cress.Companion;

namespace Cress.Companion.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var useDeterministicTarget = string.Equals(
            Environment.GetEnvironmentVariable("CRESS_COMPANION_E2E_TARGET"),
            "1",
            StringComparison.Ordinal);

        using var coordinator = new DesktopCompanionCoordinator(
            useDeterministicTarget ? new DeterministicCompanionSessionBackendFactory() : new RecordingSessionBackendFactory(),
            useDeterministicTarget ? new DeterministicCompanionTargetCatalog() : new ProcessCompanionTargetCatalog(),
            useDeterministicTarget ? new DeterministicCompanionWindowInspector() : new ProcessWindowInspector(),
            useDeterministicTarget ? new DeterministicCompanionPreviewProvider() : new ScreenPreviewProvider(),
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
