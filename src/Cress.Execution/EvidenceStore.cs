using System.Text;
using System.Text.Json;
using Cress.Core.Models;

namespace Cress.Execution;

public sealed class EvidenceStore
{
    private readonly object _sync = new();
    private readonly ArtifactIndex _index = new();
    private readonly List<string> _directories =
    [
        "logs",
        "screenshots",
        "videos",
        "traces",
        "network",
        "dom",
        "accessibility",
        "api",
        "fixtures"
    ];

    public EvidenceStore(string artifactRoot)
    {
        ArtifactRoot = artifactRoot;
        Directory.CreateDirectory(ArtifactRoot);
        foreach (var directory in _directories)
        {
            Directory.CreateDirectory(Path.Combine(ArtifactRoot, directory));
        }
    }

    public string ArtifactRoot { get; }

    public ArtifactIndex SnapshotIndex()
    {
        lock (_sync)
        {
            return new ArtifactIndex
            {
                Entries = _index.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    public EvidenceArtifact WriteJson(string relativePath, object value, string category, string? description = null)
    {
        var fullPath = GetFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(value, ExecutionJson.Options), Encoding.UTF8);
        return Register(category, relativePath, description);
    }

    public EvidenceArtifact WriteText(string relativePath, string value, string category, string? description = null)
    {
        var fullPath = GetFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, value, Encoding.UTF8);
        return Register(category, relativePath, description);
    }

    public EvidenceArtifact WriteFile(string relativePath, Action<string> writer, string category, string? description = null)
    {
        var fullPath = GetFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        writer(fullPath);
        return Register(category, relativePath, description);
    }

    public string MakeRelativePath(string category, string fileName)
        => Path.Combine(category, SanitizeFileName(fileName));

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return sanitized.Replace(' ', '-');
    }

    private string GetFullPath(string relativePath) => Path.Combine(ArtifactRoot, relativePath);

    private EvidenceArtifact Register(string category, string relativePath, string? description)
    {
        var fullPath = GetFullPath(relativePath);
        var fileInfo = File.Exists(fullPath) ? new FileInfo(fullPath) : null;
        lock (_sync)
        {
            if (!_index.Entries.TryGetValue(category, out var entries))
            {
                entries = [];
                _index.Entries[category] = entries;
            }

            entries.Add(relativePath);
        }

        return new EvidenceArtifact
        {
            Category = category,
            RelativePath = relativePath,
            Description = description,
            MediaType = InferMediaType(relativePath),
            SizeBytes = fileInfo?.Exists == true ? fileInfo.Length : null
        };
    }

    private static string InferMediaType(string relativePath)
        => Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".json" => "application/json",
            ".log" or ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".yaml" or ".yml" => "application/yaml",
            ".xml" => "application/xml",
            ".html" => "text/html",
            _ => "application/octet-stream"
        };
}
