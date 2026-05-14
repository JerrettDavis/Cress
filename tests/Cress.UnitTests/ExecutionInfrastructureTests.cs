using System.Reflection;
using Cress.Core.Models;
using Cress.Execution;

namespace Cress.UnitTests;

public sealed class ExecutionInfrastructureTests
{
    [Fact]
    public void ReportGenerator_generates_all_formats_and_lists_reports_in_descending_order()
    {
        using var workspace = new TestWorkspace();
        var generator = new ReportGenerator();
        var catalog = CreateCatalog(workspace.GetPath("project"));

        var firstResult = CreateRunResult(
            "run-001",
            workspace.GetPath("project", "artifacts", "runs", "run-001"));
        var secondResult = CreateRunResult(
            "run-002",
            workspace.GetPath("project", "artifacts", "runs", "run-002"));

        var firstReports = generator.Generate(catalog, firstResult, ["html", "json", "junit", "md", "ignored"]);
        var secondReports = generator.Generate(catalog, secondResult, []);
        var listedReports = generator.ListReports(workspace.GetPath("project"), "reports");

        Assert.Equal(["html", "json", "junit", "markdown"], firstReports.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Path.Combine(workspace.GetPath("project"), "reports", "run-002"), listedReports[0]);
        Assert.Equal(Path.Combine(workspace.GetPath("project"), "reports", "run-001"), listedReports[1]);
        Assert.Equal(4, secondReports.Count);

        var html = File.ReadAllText(firstReports["html"]);
        Assert.Contains("REQ-123 / AC-1, AC-2", html, StringComparison.Ordinal);
        Assert.Contains("Something broke", html, StringComparison.Ordinal);

        var json = File.ReadAllText(firstReports["json"]);
        Assert.Contains("\"durationMs\"", json, StringComparison.OrdinalIgnoreCase);

        var junit = File.ReadAllText(firstReports["junit"]);
        Assert.Contains("<failure", junit, StringComparison.Ordinal);
        Assert.Contains("<skipped>", junit, StringComparison.Ordinal);

        var markdown = File.ReadAllText(firstReports["markdown"]);
        Assert.Contains("## Failed flows", markdown, StringComparison.Ordinal);
        Assert.Contains("Rerun start step", markdown, StringComparison.Ordinal);
        Assert.Contains("- html:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportGenerator_list_reports_returns_empty_for_missing_root()
    {
        var generator = new ReportGenerator();

        var reports = generator.ListReports(@"C:\repo\missing", "reports");

        Assert.Empty(reports);
    }

    [Fact]
    public void RepositoryAssetLocator_finds_assets_from_repository_root_and_current_directory()
    {
        using var workspace = new TestWorkspace();
        var repositoryRoot = workspace.GetPath("repo");
        var nestedRoot = Path.Combine(repositoryRoot, "src", "nested");
        Directory.CreateDirectory(nestedRoot);
        var relativeAsset = Path.Combine("node", "tool.mjs");
        var assetPath = Path.Combine(repositoryRoot, relativeAsset);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllText(assetPath, "export {};");

        var locatorType = typeof(ReportGenerator).Assembly.GetType("Cress.Execution.RepositoryAssetLocator", throwOnError: true)!;
        var findAsset = locatorType.GetMethod("FindRepositoryAsset", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var resolveNode = locatorType.GetMethod("ResolveNodeExecutable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(findAsset);
        Assert.NotNull(resolveNode);

        var originalRoot = Environment.GetEnvironmentVariable("CRESS_REPOSITORY_ROOT");
        var originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.SetEnvironmentVariable("CRESS_REPOSITORY_ROOT", nestedRoot);
            Environment.CurrentDirectory = nestedRoot;

            var resolvedAsset = Assert.IsType<string>(findAsset.Invoke(null, [relativeAsset]));
            var missingAsset = findAsset.Invoke(null, [Path.Combine("node", "missing.mjs")]);
            var nodeExecutable = Assert.IsType<string>(resolveNode.Invoke(null, []));

            Assert.Equal(assetPath, resolvedAsset);
            Assert.Null(missingAsset);
            Assert.False(string.IsNullOrWhiteSpace(nodeExecutable));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CRESS_REPOSITORY_ROOT", originalRoot);
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    private static ProjectCatalog CreateCatalog(string projectRoot) => new()
    {
        ProjectRoot = projectRoot,
        EffectiveConfig = new EffectiveConfig
        {
            Config = new CressConfig
            {
                Project = new ProjectConfig
                {
                    Name = "Execution Infra"
                },
                Paths = new PathsConfig
                {
                    Reports = "reports"
                }
            },
            ActiveProfile = "local"
        }
    };

    private static RunResult CreateRunResult(string runId, string artifactRoot) => new()
    {
        Metadata = new RunMetadata
        {
            RunId = runId,
            ArtifactRoot = artifactRoot,
            ProjectName = "Execution Infra",
            Profile = "local",
            StartedAt = new DateTimeOffset(2026, 5, 14, 22, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 5, 14, 22, 1, 0, TimeSpan.Zero),
            DurationMs = 60000
        },
        Invocation = new RunInvocation
        {
            Trigger = "manual",
            RetryCount = 2,
            ScreenshotPolicy = "on-failure",
            StartFromStep = "step-2"
        },
        Flows =
        [
            new FlowRunResult
            {
                FlowId = "flow-failed",
                Name = "Failed flow",
                CapabilityId = "capability-a",
                Outcome = RunOutcome.Failed,
                FailureClassification = "assertion-failed",
                FailureMessage = "Something broke",
                DurationMs = 1000,
                Drivers = ["http"],
                Traceability = new TraceabilityInfo
                {
                    Requirement = "REQ-123",
                    AcceptanceCriteria = ["AC-1", "AC-2"]
                },
                Steps =
                [
                    new StepRunResult
                    {
                        Kind = "when",
                        Name = "step-1",
                        Outcome = RunOutcome.Failed,
                        Attempt = 2,
                        DurationMs = 250,
                        Artifacts =
                        [
                            new EvidenceArtifact
                            {
                                Category = "screenshots",
                                RelativePath = "screenshots\\shot.png"
                            }
                        ]
                    }
                ]
            },
            new FlowRunResult
            {
                FlowId = "flow-skipped",
                Name = "Skipped flow",
                Outcome = RunOutcome.Skipped,
                FailureMessage = "Skipped for maintenance",
                DurationMs = 10,
                Drivers = ["http"]
            }
        ]
    };
}
