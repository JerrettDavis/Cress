namespace Cress.Testing;

public static class CressTestPaths
{
    public static string ResolveProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A Cress project path is required.", nameof(path));
        }

        if (Path.IsPathRooted(path))
        {
            var rooted = Path.GetFullPath(path);
            EnsureProject(rooted, path);
            return rooted;
        }

        foreach (var root in EnumerateSearchRoots())
        {
            foreach (var parent in EnumerateSelfAndParents(root))
            {
                var candidate = Path.GetFullPath(Path.Combine(parent, path));
                if (File.Exists(Path.Combine(candidate, ".cress", "config.yaml")))
                {
                    return candidate;
                }
            }
        }

        var fallback = Path.GetFullPath(path);
        EnsureProject(fallback, path);
        return fallback;
    }

    public static string ResolveFlowPath(string projectPath, string flowPath)
    {
        if (string.IsNullOrWhiteSpace(flowPath))
        {
            throw new ArgumentException("A Cress flow path is required.", nameof(flowPath));
        }

        return Path.IsPathRooted(flowPath)
            ? Path.GetFullPath(flowPath)
            : Path.GetFullPath(Path.Combine(projectPath, flowPath));
    }

    private static void EnsureProject(string resolvedPath, string originalPath)
    {
        if (!File.Exists(Path.Combine(resolvedPath, ".cress", "config.yaml")))
        {
            throw new DirectoryNotFoundException($"Could not find a Cress project at '{originalPath}' (resolved to '{resolvedPath}').");
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;
    }

    private static IEnumerable<string> EnumerateSelfAndParents(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
