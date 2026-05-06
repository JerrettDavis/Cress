namespace Cress.ProjectSystem;

public sealed class ProjectLocator
{
    public const string ConfigRelativePath = ".cress\\config.yaml";

    public string? FindProjectRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            var configPath = Path.Combine(current.FullName, ".cress", "config.yaml");
            if (File.Exists(configPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public bool TryFindProjectRoot(string startDirectory, out string projectRoot)
    {
        projectRoot = FindProjectRoot(startDirectory) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(projectRoot);
    }

    public string GetConfigPath(string projectRoot) => Path.Combine(projectRoot, ".cress", "config.yaml");
}
