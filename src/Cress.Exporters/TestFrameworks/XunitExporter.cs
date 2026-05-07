using System.Text;
using Cress.Core.Models;

namespace Cress.Exporters.TestFrameworks;

public sealed class XunitExporter
{
    public string Export(NormalizedFlow flow, string projectRoot, DotNetTestExportOptions? options = null)
    {
        options ??= new DotNetTestExportOptions();
        var className = DotNetTestExporterSupport.BuildClassName(flow, "XunitTests", options.ClassName);
        var methodName = DotNetTestExporterSupport.BuildMethodName(flow);
        var flowPath = DotNetTestExporterSupport.GetRelativeFlowPath(flow, projectRoot);

        var builder = new StringBuilder()
            .AppendLine("// Generated from a Cress flow. Add package references to Cress.Testing and xunit in your test project.")
            .AppendLine("using System.Threading.Tasks;")
            .AppendLine("using Cress.Testing;")
            .AppendLine("using Xunit;")
            .AppendLine()
            .Append("namespace ").Append(options.Namespace).AppendLine(";")
            .AppendLine()
            .Append("public sealed class ").Append(className).AppendLine()
            .AppendLine("{")
            .AppendLine("    [Fact]")
            .Append("    public async Task ").Append(methodName).AppendLine("()")
            .AppendLine("    {")
            .AppendLine("        await CressTestEngine.RunFlowAsync(")
            .Append("            projectPath: CressTestPaths.ResolveProjectPath(").Append(DotNetTestExporterSupport.ToVerbatimLiteral(options.ProjectPath)).AppendLine("),")
            .Append("            flowPath: ").Append(DotNetTestExporterSupport.ToVerbatimLiteral(flowPath));

        if (!string.IsNullOrWhiteSpace(options.Profile))
        {
            builder.AppendLine(",")
                .Append("            profile: ").Append(DotNetTestExporterSupport.ToVerbatimLiteral(options.Profile!)).AppendLine(");");
        }
        else
        {
            builder.AppendLine(");");
        }

        builder.AppendLine("    }")
            .AppendLine("}");

        return builder.ToString();
    }
}
