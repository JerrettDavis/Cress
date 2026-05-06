using System.Reflection;
using Scriban;
using Scriban.Runtime;

namespace Cress.LivingDocs;

/// <summary>
/// Renders a Scriban template (from disk or embedded resource) against a <see cref="DocumentModel"/>.
/// </summary>
public sealed class TemplateRenderer
{
    private static readonly Assembly _assembly = typeof(TemplateRenderer).Assembly;

    /// <summary>Snake_case renamer: <c>PassRate</c> → <c>pass_rate</c>, <c>RecentRuns</c> → <c>recent_runs</c>.</summary>
    private static readonly MemberRenamerDelegate SnakeCaseRenamer = m => ToSnakeCase(m.Name);

    /// <summary>Identity renamer — keeps method names exactly as declared (for <see cref="TemplateHelpers"/>).</summary>
    private static readonly MemberRenamerDelegate IdentityRenamer = m => m.Name;

    /// <summary>Render an on-disk template file.</summary>
    public string Render(string templatePath, DocumentModel model)
    {
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Living doc template not found: {templatePath}");
        }

        var source = File.ReadAllText(templatePath);
        return RenderSource(source, model);
    }

    /// <summary>Render one of the three built-in embedded templates by short name:
    /// <c>executive</c>, <c>technical</c>, or <c>public</c>.</summary>
    public string RenderEmbedded(string templateName, DocumentModel model)
    {
        var source = LoadEmbedded(templateName);
        return RenderSource(source, model);
    }

    // ------------------------------------------------------------------ Internal

    private static string RenderSource(string source, DocumentModel model)
    {
        var template = Template.Parse(source);
        if (template.HasErrors)
        {
            var errors = string.Join("; ", template.Messages.Select(m => m.ToString()));
            throw new InvalidOperationException($"Scriban template parse error: {errors}");
        }

        var scriptObject = new ScriptObject();
        scriptObject.Import(model, renamer: SnakeCaseRenamer);

        // Expose static helper functions under their exact snake_case names
        scriptObject.Import(typeof(TemplateHelpers), renamer: IdentityRenamer);

        var ctx = new TemplateContext { MemberRenamer = SnakeCaseRenamer };
        ctx.PushGlobal(scriptObject);

        return template.Render(ctx);
    }

    private static string LoadEmbedded(string name)
    {
        var resourceName = $"Cress.LivingDocs.Templates.{name}.scriban-html";
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException($"Embedded template '{name}' not found. Available names: executive, technical, public.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Convert PascalCase/camelCase to snake_case. E.g. <c>RecentRuns</c> → <c>recent_runs</c>.</summary>
    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
