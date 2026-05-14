namespace Cress.UnitTests;

internal sealed class TestWorkspace : IDisposable
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();

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

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
}
