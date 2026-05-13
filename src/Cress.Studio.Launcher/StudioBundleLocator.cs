namespace Cress.Studio.Launcher;

public static class StudioBundleLocator
{
    public const string StudioExecutableName = "Cress.Studio.Web.exe";

    public static string ResolveWebRoot(string? explicitWebRootPath = null, string? baseDirectory = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitWebRootPath))
        {
            candidates.Add(Path.GetFullPath(explicitWebRootPath));
        }

        var resolvedBaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        candidates.Add(Path.Combine(resolvedBaseDirectory, "studio", "web"));

        var currentAncestor = new DirectoryInfo(resolvedBaseDirectory);
        while (currentAncestor is not null)
        {
            candidates.Add(Path.Combine(currentAncestor.FullName, "artifacts", "studio-publish", "web"));
            currentAncestor = currentAncestor.Parent;
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(candidate, StudioExecutableName)))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "Unable to locate the packaged Studio web payload. Run scripts\\Publish-StudioInstaller.ps1 or pass --web-root.");
    }
}
