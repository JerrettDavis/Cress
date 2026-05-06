namespace Cress.LivingDocs;

/// <summary>Options controlling what <see cref="DocumentBuilder"/> collects.</summary>
public sealed record DocumentBuildOptions
{
    /// <summary>Absolute path to the Cress project root.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Branding overrides applied on top of project config defaults.</summary>
    public DocumentBranding Branding { get; init; } = new DocumentBranding("Cress Living Doc", null, "#6366f1");

    /// <summary>Maximum run-history entries to include in the model.</summary>
    public int MaxRecentRuns { get; init; } = 10;

    /// <summary>Maximum screenshot evidence entries to include.</summary>
    public int MaxScreenshots { get; init; } = 20;

    /// <summary>Git SHA to stamp into the document (empty string if not available).</summary>
    public string GitSha { get; init; } = string.Empty;
}
