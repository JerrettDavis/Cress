using Cress.Core.Models;
using Cress.Exporters.TestFrameworks;

namespace Cress.Exporters.Tests;

public sealed class DotNetFrameworkExporterTests
{
    [Fact]
    public void XunitExporter_EmitsFactAndCressTestEngineCall()
    {
        var flow = CreateFlow(@"C:\repo\specs\httpbin-smoke\flows\httpbin\get-smoke.flow.yaml");

        var output = new XunitExporter().Export(flow, @"C:\repo\specs\httpbin-smoke", new DotNetTestExportOptions
        {
            Namespace = "Contoso.Tests",
            ProjectPath = @"specs\httpbin-smoke",
            Profile = "ci"
        });

        Assert.Contains("using Xunit;", output);
        Assert.Contains("[Fact]", output);
        Assert.Contains("await CressTestEngine.RunFlowAsync(", output);
        Assert.Contains(@"projectPath: CressTestPaths.ResolveProjectPath(@""specs\httpbin-smoke"")", output);
        Assert.Contains(@"flowPath: @""flows\httpbin\get-smoke.flow.yaml""", output);
        Assert.Contains(@"profile: @""ci""", output);
    }

    [Fact]
    public void NUnitExporter_EmitsTestFixtureAndTest()
    {
        var flow = CreateFlow(@"C:\repo\specs\calc-smoke\flows\calc-add.flow.yaml");

        var output = new NUnitExporter().Export(flow, @"C:\repo\specs\calc-smoke", new DotNetTestExportOptions
        {
            Namespace = "Contoso.Desktop.Tests",
            ProjectPath = @"specs\calc-smoke"
        });

        Assert.Contains("using NUnit.Framework;", output);
        Assert.Contains("[TestFixture]", output);
        Assert.Contains("[Test]", output);
        Assert.Contains("public sealed class CalcAddNUnitTests", output);
        Assert.DoesNotContain("profile:", output);
    }

    [Fact]
    public void MsTestExporter_EmitsTestClassAndTestMethod()
    {
        var flow = CreateFlow(@"C:\repo\specs\web-smoke\flows\example.flow.yaml");

        var output = new MsTestExporter().Export(flow, @"C:\repo\specs\web-smoke", new DotNetTestExportOptions
        {
            Namespace = "Contoso.Web.Tests",
            ProjectPath = @"specs\web-smoke",
            ClassName = "PortalFlowTests"
        });

        Assert.Contains("using Microsoft.VisualStudio.TestTools.UnitTesting;", output);
        Assert.Contains("[TestClass]", output);
        Assert.Contains("[TestMethod]", output);
        Assert.Contains("public sealed class PortalFlowTests", output);
    }

    private static NormalizedFlow CreateFlow(string sourceFile) => new()
    {
        FlowId = Path.GetFileNameWithoutExtension(sourceFile).Replace(".flow", string.Empty, StringComparison.OrdinalIgnoreCase),
        Name = Path.GetFileNameWithoutExtension(sourceFile).Replace(".flow", string.Empty, StringComparison.OrdinalIgnoreCase),
        SourceFile = sourceFile
    };
}
