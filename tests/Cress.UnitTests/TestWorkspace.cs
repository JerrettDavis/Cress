namespace Cress.UnitTests;

internal sealed class TestWorkspace : IDisposable
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    public string RootPath { get; }

    public TestWorkspace()
    {
        Environment.SetEnvironmentVariable("CRESS_REPOSITORY_ROOT", RepositoryRoot);
        RootPath = Path.Combine(AppContext.BaseDirectory, "test-workspaces", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string GetPath(params string[] segments)
    {
        var parts = new[] { RootPath }.Concat(segments).ToArray();
        return Path.Combine(parts);
    }

    public void WriteFile(string relativePath, string content)
    {
        var path = GetPath(relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    internal static string ResolveRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var existing = Environment.GetEnvironmentVariable("CRESS_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(existing) && File.Exists(Path.Combine(existing, "Cress.sln")))
        {
            return existing;
        }

        var githubWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(githubWorkspace) && File.Exists(Path.Combine(githubWorkspace, "Cress.sln")))
        {
            return githubWorkspace;
        }

        if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            var callerFileRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
            if (File.Exists(Path.Combine(callerFileRoot, "Cress.sln")))
            {
                return callerFileRoot;
            }
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Cress.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the current test workspace.");
    }
}
