using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cress.Core.Models;

namespace Cress.Importers.Postman;

/// <summary>
/// Converts a Postman Collection v2.1 JSON file into one or more <see cref="CressFlow"/> objects.
/// Each request becomes a separate flow by default (<paramref name="singleFlow"/> = false),
/// or all requests are combined into a single multi-step flow when <paramref name="singleFlow"/> = true.
/// </summary>
/// <remarks>
/// Supports:
/// <list type="bullet">
///   <item>HTTP methods: GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS</item>
///   <item>URL from <c>request.url.raw</c> (preferred) or reconstructed from host/path</item>
///   <item>Headers → <c>with.headers.*</c> key/value pairs</item>
///   <item>Raw body → <c>with.body</c></item>
///   <item>Postman variable syntax <c>{{varName}}</c> is preserved verbatim</item>
///   <item>Folder hierarchy → flow <c>tags</c></item>
/// </list>
/// Does NOT support:
/// <list type="bullet">
///   <item>Pre-request scripts or test scripts (Postman JS hooks) — emitted as TODO comment steps</item>
///   <item>Body modes other than "raw" — emitted as TODO comment steps</item>
/// </list>
/// </remarks>
public sealed class PostmanImporter
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parse a Postman Collection v2.1 JSON string and return one <see cref="CressFlow"/> per request.
    /// </summary>
    /// <param name="collectionJson">Full JSON content of the Postman collection.</param>
    /// <param name="singleFlow">
    /// When <see langword="true"/>, all requests are combined into one flow with sequential steps.
    /// When <see langword="false"/> (default), one flow is produced per request.
    /// </param>
    /// <returns>A list of <see cref="CressFlow"/> objects ready for YAML serialization.</returns>
    public IReadOnlyList<CressFlow> Import(string collectionJson, bool singleFlow = false)
    {
        using var doc = JsonDocument.Parse(collectionJson);
        var root = doc.RootElement;

        // Extract collection name from info.name
        var collectionName = root.TryGetProperty("info", out var info) &&
                             info.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString() ?? "postman-import"
            : "postman-import";

        // Walk item tree depth-first, collecting requests with their folder path
        var requests = new List<(string[] FolderPath, JsonElement RequestEl, string RequestName)>();
        if (root.TryGetProperty("item", out var items))
        {
            WalkItems(items, [], requests);
        }

        if (singleFlow)
        {
            return [BuildSingleFlow(collectionName, requests)];
        }

        return requests
            .Select(r => BuildFlowForRequest(r.RequestName, r.FolderPath, r.RequestEl))
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Item tree walker (recursive, depth-first)
    // -------------------------------------------------------------------------

    private static void WalkItems(
        JsonElement items,
        string[] folderPath,
        List<(string[] FolderPath, JsonElement RequestEl, string RequestName)> results)
    {
        foreach (var item in items.EnumerateArray())
        {
            var itemName = item.TryGetProperty("name", out var n) ? n.GetString() ?? "unnamed" : "unnamed";

            // Folder: has an "item" array but no "request" at the top level
            if (item.TryGetProperty("item", out var subItems))
            {
                var newPath = folderPath.Append(itemName).ToArray();
                WalkItems(subItems, newPath, results);
                continue;
            }

            // Leaf request
            if (item.TryGetProperty("request", out var req))
            {
                results.Add((folderPath, req, itemName));
            }
        }
    }

    // -------------------------------------------------------------------------
    // Flow builders
    // -------------------------------------------------------------------------

    private static CressFlow BuildFlowForRequest(
        string requestName,
        string[] folderPath,
        JsonElement requestEl)
    {
        var step = BuildStepForRequest(requestName, requestEl);
        var tags = BuildTags(folderPath, ["postman-import"]);
        var id = SlugifyName(requestName);

        return new CressFlow
        {
            Version = 1,
            Id = id,
            Name = requestName,
            Summary = $"Imported from Postman collection on {DateTimeOffset.UtcNow:yyyy-MM-dd}",
            Tags = tags,
            When = [step],
            Then = []
        };
    }

    private static CressFlow BuildSingleFlow(
        string collectionName,
        List<(string[] FolderPath, JsonElement RequestEl, string RequestName)> requests)
    {
        var steps = requests
            .Select(r => BuildStepForRequest(r.RequestName, r.RequestEl))
            .ToList();

        return new CressFlow
        {
            Version = 1,
            Id = SlugifyName(collectionName),
            Name = collectionName,
            Summary = $"Imported from Postman collection on {DateTimeOffset.UtcNow:yyyy-MM-dd}",
            Tags = ["postman-import"],
            When = steps,
            Then = []
        };
    }

    // -------------------------------------------------------------------------
    // Step builder
    // -------------------------------------------------------------------------

    private static FlowAction BuildStepForRequest(string requestName, JsonElement requestEl)
    {
        // Method
        var method = requestEl.TryGetProperty("method", out var methodEl)
            ? methodEl.GetString()?.ToUpperInvariant() ?? "GET"
            : "GET";

        var stepOp = MapMethodToStep(method);

        // URL
        var url = ResolveUrl(requestEl);

        var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["url"] = url
        };

        // Headers
        if (requestEl.TryGetProperty("header", out var headers) &&
            headers.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in headers.EnumerateArray())
            {
                if (!header.TryGetProperty("key", out var keyEl) ||
                    !header.TryGetProperty("value", out var valEl))
                {
                    continue;
                }

                // Skip headers marked disabled
                if (header.TryGetProperty("disabled", out var disabledEl) &&
                    disabledEl.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var key = keyEl.GetString();
                var val = valEl.GetString();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    with[$"headers.{key}"] = val ?? string.Empty;
                }
            }
        }

        // Body
        if (requestEl.TryGetProperty("body", out var body))
        {
            var mode = body.TryGetProperty("mode", out var modeEl)
                ? modeEl.GetString()?.ToLowerInvariant()
                : null;

            if (mode == "raw")
            {
                var rawBody = body.TryGetProperty("raw", out var rawEl)
                    ? rawEl.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    with["body"] = rawBody!;
                }
            }
            else if (mode is "formdata" or "urlencoded" or "file" or "graphql")
            {
                // Unsupported body modes — emit as a TODO comment via a special key
                // TODO: implement formdata/urlencoded body conversion in a future version
                with["body"] = $"# TODO: body mode '{mode}' is not yet supported by the Postman importer";
            }
        }

        return new FlowAction
        {
            Step = stepOp,
            With = with
        };
    }

    // -------------------------------------------------------------------------
    // URL resolution
    // -------------------------------------------------------------------------

    private static string ResolveUrl(JsonElement requestEl)
    {
        if (!requestEl.TryGetProperty("url", out var urlEl))
        {
            return string.Empty;
        }

        // url can be a plain string in some older formats
        if (urlEl.ValueKind == JsonValueKind.String)
        {
            return urlEl.GetString() ?? string.Empty;
        }

        // Prefer raw (preserves {{variables}} verbatim)
        if (urlEl.TryGetProperty("raw", out var rawEl))
        {
            var raw = rawEl.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }
        }

        // Reconstruct from host + path
        var sb = new StringBuilder();

        if (urlEl.TryGetProperty("protocol", out var protoEl))
        {
            sb.Append(protoEl.GetString());
            sb.Append("://");
        }

        if (urlEl.TryGetProperty("host", out var hostEl) && hostEl.ValueKind == JsonValueKind.Array)
        {
            sb.Append(string.Join(".", hostEl.EnumerateArray().Select(h => h.GetString() ?? "")));
        }

        if (urlEl.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var segment in pathEl.EnumerateArray())
            {
                sb.Append('/');
                sb.Append(segment.GetString() ?? "");
            }
        }

        if (urlEl.TryGetProperty("query", out var queryEl) && queryEl.ValueKind == JsonValueKind.Array)
        {
            var pairs = queryEl
                .EnumerateArray()
                .Where(q => !(q.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True))
                .Select(q =>
                {
                    var k = q.TryGetProperty("key", out var ke) ? ke.GetString() : null;
                    var v = q.TryGetProperty("value", out var ve) ? ve.GetString() : null;
                    return $"{k}={v}";
                });

            var queryString = string.Join("&", pairs);
            if (!string.IsNullOrEmpty(queryString))
            {
                sb.Append('?');
                sb.Append(queryString);
            }
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // HTTP method → Cress step name
    // -------------------------------------------------------------------------

    private static string MapMethodToStep(string method) => method switch
    {
        "GET" => "http.get",
        "POST" => "http.post",
        "PUT" => "http.put",
        "DELETE" => "http.delete",
        "PATCH" => "http.patch",
        "HEAD" => "http.head",
        "OPTIONS" => "http.options",
        _ => "http.get" // safe fallback
    };

    // -------------------------------------------------------------------------
    // Tags
    // -------------------------------------------------------------------------

    private static List<string> BuildTags(string[] folderPath, IEnumerable<string> baseTags)
    {
        var tags = new List<string>(baseTags);

        // Convert each folder name to a tag slug
        foreach (var folder in folderPath)
        {
            var slug = SlugifyName(folder);
            if (!string.IsNullOrWhiteSpace(slug))
            {
                tags.Add(slug);
            }
        }

        return tags;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Convert a human-readable name to a kebab-slug suitable for flow IDs and tags.</summary>
    internal static string SlugifyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "imported-flow";
        }

        var slug = Regex.Replace(name.ToLowerInvariant(), @"[:\s]+", "-")
                        .Replace(",", string.Empty)
                        .Replace("'", string.Empty)
                        .Replace("\"", string.Empty);
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);
        slug = Regex.Replace(slug, @"-{2,}", "-");
        slug = slug.Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "imported-flow" : slug;
    }
}
