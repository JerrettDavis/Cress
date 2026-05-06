namespace Cress.UnitTests;

internal sealed class TestWorkspace : IDisposable
{
    public string RootPath { get; }

    public TestWorkspace()
    {
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
}
