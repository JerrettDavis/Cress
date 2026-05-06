using System.Text.Json;
using Cress.Core.Models;

namespace Cress.Execution;

public sealed class RunResultRepository
{
    public IReadOnlyList<StoredRunResult> ListRuns(string projectRoot, string artifactsRelativePath, int maxCount = 25)
    {
        var root = Path.Combine(projectRoot, artifactsRelativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(LoadRunFromArtifactDirectory)
            .Where(run => run is not null)
            .Take(maxCount)
            .Cast<StoredRunResult>()
            .ToList();
    }

    public StoredRunResult? LoadRunFromArtifactDirectory(string artifactDirectory)
    {
        var resultPath = Path.Combine(artifactDirectory, "result.json");
        if (!File.Exists(resultPath))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<RunResult>(File.ReadAllText(resultPath), ExecutionJson.Options);
            return result is null
                ? null
                : new StoredRunResult
                {
                    ArtifactDirectory = artifactDirectory,
                    Result = result
                };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record StoredRunResult
{
    public string ArtifactDirectory { get; init; } = string.Empty;
    public RunResult Result { get; init; } = new();
}
