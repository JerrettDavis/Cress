using Cress.Core.Models;
using Cress.Execution;

namespace Cress.UnitTests;

public sealed class StepRegistryTests
{
    [Fact]
    public void Build_CollectsDuplicateStepErrors_AndAliasWarnings()
    {
        var registry = new StepRegistry();
        var manifests = new[]
        {
            new StepManifest
            {
                Steps =
                [
                    new StepDefinition
                    {
                        Name = "browser.open",
                        SourceFile = "steps\\browser.yaml",
                        Aliases = ["open", "visit"]
                    },
                    new StepDefinition
                    {
                        Name = "browser.click",
                        SourceFile = "steps\\actions.yaml",
                        Aliases = ["open", "click"]
                    }
                ]
            },
            new StepManifest
            {
                Steps =
                [
                    new StepDefinition
                    {
                        Name = "browser.open",
                        SourceFile = "steps\\duplicate.yaml"
                    }
                ]
            }
        };

        var result = registry.Build(manifests);

        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Diagnostics.Count);

        var duplicate = Assert.Single(result.Diagnostics, d => d.Code == "REG001");
        Assert.Equal(DiagnosticSeverity.Error, duplicate.Severity);
        Assert.Contains("browser.open", duplicate.Message, StringComparison.Ordinal);
        Assert.Equal("steps\\duplicate.yaml", duplicate.File);

        var alias = Assert.Single(result.Diagnostics, d => d.Code == "REG002");
        Assert.Equal(DiagnosticSeverity.Warning, alias.Severity);
        Assert.Contains("'open'", alias.Message, StringComparison.Ordinal);
        Assert.Equal("steps\\actions.yaml", alias.File);
    }

    [Fact]
    public void TryResolve_SupportsDefinitionAliasAndMisses()
    {
        var definition = new StepDefinition
        {
            Name = "browser.open",
            SourceFile = "steps\\browser.yaml",
            Aliases = ["open"]
        };

        var snapshot = new StepRegistry().Build(
        [
            new StepManifest
            {
                Steps = [definition]
            }
        ]).Value!;

        Assert.True(snapshot.TryResolve("browser.open", out var byName));
        Assert.Same(definition, byName);

        Assert.True(snapshot.TryResolve("open", out var byAlias));
        Assert.Same(definition, byAlias);

        Assert.False(snapshot.TryResolve("missing", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void EmptySnapshot_DoesNotResolveUnknownSteps()
    {
        Assert.False(StepRegistrySnapshot.Empty.TryResolve("anything", out var definition));
        Assert.Null(definition);
        Assert.Empty(StepRegistrySnapshot.Empty.Definitions);
        Assert.Empty(StepRegistrySnapshot.Empty.Aliases);
    }
}
