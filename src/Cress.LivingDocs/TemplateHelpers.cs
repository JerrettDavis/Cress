namespace Cress.LivingDocs;

/// <summary>
/// Static helper functions exposed to all Scriban living-doc templates.
/// Method names use snake_case to match Scriban's naming convention.
/// </summary>
internal static class TemplateHelpers
{
    // These method names are the exact names used in templates (no renaming applied).
    // ReSharper disable InconsistentNaming
#pragma warning disable IDE1006 // Naming styles

    public static string format_duration(object ts)
    {
        TimeSpan t;
        if (ts is TimeSpan span)
        {
            t = span;
        }
        else
        {
            try { t = TimeSpan.FromMilliseconds(Convert.ToDouble(ts)); }
            catch { return ts?.ToString() ?? string.Empty; }
        }

        if (t.TotalHours >= 1) return $"{t.TotalHours:F1}h";
        if (t.TotalMinutes >= 1) return $"{t.TotalMinutes:F1}m";
        if (t.TotalSeconds >= 1) return $"{t.TotalSeconds:F1}s";
        return $"{t.TotalMilliseconds:F0}ms";
    }

    public static string format_percent(double value) => $"{value:P1}";

    public static string status_badge_class(string status) => status?.ToLowerInvariant() switch
    {
        "passing" => "badge-pass",
        "failing" => "badge-fail",
        "flaky" => "badge-flaky",
        _ => "badge-unknown"
    };

    public static string status_emoji(string status) => status?.ToLowerInvariant() switch
    {
        "passing" => "✓",
        "failing" => "✗",
        "flaky" => "~",
        _ => "?"
    };

#pragma warning restore IDE1006
    // ReSharper restore InconsistentNaming
}
