using Cress.Core.Models;
using Cress.Exporters.TestFrameworks;
using System.Reflection;

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

    [Fact]
    public void XunitExporter_UsesDefaultOptions_WhenOptionsAreOmitted()
    {
        var flow = CreateFlow(@"C:\repo\specs\basic\flows\hello.flow.yaml");

        var output = new XunitExporter().Export(flow, @"C:\repo\specs\basic");

        Assert.Contains("namespace Cress.Generated;", output);
        Assert.Contains("public sealed class HelloXunitTests", output);
        Assert.DoesNotContain("profile:", output);
    }

    [Fact]
    public void NUnitExporter_EmitsOptionalProfile_WhenProvided()
    {
        var flow = CreateFlow(@"C:\repo\specs\calc-smoke\flows\calc-add.flow.yaml");

        var output = new NUnitExporter().Export(flow, @"C:\repo\specs\calc-smoke", new DotNetTestExportOptions
        {
            Namespace = "Contoso.Desktop.Tests",
            ProjectPath = @"specs\calc-smoke",
            Profile = "desktop"
        });

        Assert.Contains(@"profile: @""desktop""", output);
    }

    [Fact]
    public void MsTestExporter_EmitsOptionalProfile_WhenProvided()
    {
        var flow = CreateFlow(@"C:\repo\specs\web-smoke\flows\example.flow.yaml");

        var output = new MsTestExporter().Export(flow, @"C:\repo\specs\web-smoke", new DotNetTestExportOptions
        {
            Namespace = "Contoso.Web.Tests",
            ProjectPath = @"specs\web-smoke",
            Profile = "smoke"
        });

        Assert.Contains(@"profile: @""smoke""", output);
    }

    [Fact]
    public void Support_helpers_cover_edge_cases()
    {
        var supportType = typeof(XunitExporter).Assembly.GetType("Cress.Exporters.TestFrameworks.DotNetTestExporterSupport", throwOnError: true)!;
        var flowWithoutSource = new NormalizedFlow
        {
            FlowId = "!!!",
            Name = "",
            SourceFile = null
        };

        var getRelativeFlowPath = supportType.GetMethod("GetRelativeFlowPath", BindingFlags.Public | BindingFlags.Static)!;
        var sanitizeIdentifier = supportType.GetMethod("SanitizeIdentifier", BindingFlags.Public | BindingFlags.Static)!;
        var toVerbatimLiteral = supportType.GetMethod("ToVerbatimLiteral", BindingFlags.Public | BindingFlags.Static)!;

        var exception = Assert.Throws<TargetInvocationException>(() => getRelativeFlowPath.Invoke(null, [flowWithoutSource, @"C:\repo"]));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal("Fallback", sanitizeIdentifier.Invoke(null, ["   ", "Fallback"]));
        Assert.Equal("Fallback", sanitizeIdentifier.Invoke(null, ["!!!", "Fallback"]));
        Assert.Equal("_123Flow", sanitizeIdentifier.Invoke(null, ["123 flow", "Fallback"]));
        Assert.Equal("@\"say \"\"hi\"\"\"", toVerbatimLiteral.Invoke(null, ["say \"hi\""]));
    }

    private static NormalizedFlow CreateFlow(string sourceFile) => new()
    {
        FlowId = Path.GetFileNameWithoutExtension(sourceFile).Replace(".flow", string.Empty, StringComparison.OrdinalIgnoreCase),
        Name = Path.GetFileNameWithoutExtension(sourceFile).Replace(".flow", string.Empty, StringComparison.OrdinalIgnoreCase),
        SourceFile = sourceFile
    };
}
