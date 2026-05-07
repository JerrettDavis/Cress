using System.Text;
using Cress.Core.Models;

namespace Cress.Exporters.TestFrameworks;

public sealed class MsTestExporter
{
    public string Export(NormalizedFlow flow, string projectRoot, DotNetTestExportOptions? options = null)
    {
        options ??= new DotNetTestExportOptions();
        var className = DotNetTestExporterSupport.BuildClassName(flow, "MsTestTests", options.ClassName);
        var methodName = DotNetTestExporterSupport.BuildMethodName(flow);
        var flowPath = DotNetTestExporterSupport.GetRelativeFlowPath(flow, projectRoot);

        var builder = new StringBuilder()
            .AppendLine("// Generated from a Cress flow. Add package references to Cress.Testing and MSTest.TestFramework in your test project.")
            .AppendLine("using System.Threading.Tasks;")
            .AppendLine("using Cress.Testing;")
            .AppendLine("using Microsoft.VisualStudio.TestTools.UnitTesting;")
            .AppendLine()
            .Append("namespace ").Append(options.Namespace).AppendLine(";")
            .AppendLine()
            .AppendLine("[TestClass]")
            .Append("public sealed class ").Append(className).AppendLine()
            .AppendLine("{")
            .AppendLine("    [TestMethod]")
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
