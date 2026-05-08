using Aspire.Hosting;

namespace Cress.AppHost;

internal static class CressAppHostComposition
{
    public static void Configure(IDistributedApplicationBuilder builder, string startDirectory, string? disableDesktopStudioValue)
    {
        var settings = ResolveSettings(startDirectory, disableDesktopStudioValue);

        builder.AddProject<Projects.Cress_Studio_Web>("studio-web")
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithExternalHttpEndpoints();

        if (settings.IncludeDesktopStudio)
        {
            builder.AddExecutable("studio", settings.StudioExecutablePath, settings.StudioProjectDirectory)
                .WithEnvironment("DOTNET_ENVIRONMENT", "Development");
        }
    }

    internal static CressAppHostSettings ResolveSettings(string startDirectory, string? disableDesktopStudioValue)
    {
        var repoRoot = FindRepoRoot(startDirectory);
        var studioProjectDirectory = Path.Combine(repoRoot, "src", "Cress.Studio");

        return new CressAppHostSettings(
            RepoRoot: repoRoot,
            StudioProjectDirectory: studioProjectDirectory,
            StudioExecutablePath: ResolveStudioExecutablePath(studioProjectDirectory),
            IncludeDesktopStudio: !IsDesktopStudioDisabled(disableDesktopStudioValue));
    }

    internal static bool IsDesktopStudioDisabled(string? value)
        => value is not null &&
           (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

    internal static string ResolveStudioExecutablePath(string studioProjectDirectory)
    {
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var candidate = Path.Combine(
                studioProjectDirectory,
                "bin",
                configuration,
                "net10.0-windows",
                "Cress.Studio.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(
            studioProjectDirectory,
            "bin",
            "Debug",
            "net10.0-windows",
            "Cress.Studio.exe");
    }

    internal static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cress.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return startDirectory;
    }
}

internal sealed record CressAppHostSettings(
    string RepoRoot,
    string StudioProjectDirectory,
    string StudioExecutablePath,
    bool IncludeDesktopStudio);
