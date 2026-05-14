using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cress.Core.Models;
using Cress.Execution;
using Cress.LivingDocs;
using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Studio.Services;

namespace Cress.LivingDocs.Tests;

public sealed class DocumentBuilderTests
{
    [Fact]
    public async Task BuildAsync_returns_empty_model_when_no_runs_exist()
    {
        using var workspace = new TemporaryWorkspace();
        var projectPath = workspace.CreateProjectRoot();
        var builder = CreateBuilder();

        var model = await builder.BuildAsync(new DocumentBuildOptions
        {
            ProjectPath = projectPath
        });

        Assert.Equal(Path.GetFileName(projectPath), model.Meta.ProjectName);
        Assert.Empty(model.RecentRuns);
        Assert.Empty(model.Flows);
        Assert.Empty(model.Screenshots);
        Assert.Empty(model.Diagnostics);
        Assert.Null(model.Metrics);
        Assert.Equal(0, model.Suite.TotalFlows);
    }

    [Fact]
    public async Task BuildAsync_aggregates_run_history_metrics_and_screenshots()
    {
        using var workspace = new TemporaryWorkspace();
        var projectPath = workspace.CreateProjectRoot();
        workspace.WriteFile(Path.Combine(projectPath, ".cress", "config.yaml"), """
        version: 1
        project:
          name: Living docs sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        """);

        WriteRunArtifact(
            workspace,
            projectPath,
            "2026-05-07-001",
            new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-001",
                    ArtifactRoot = "artifact-root-001",
                    ProjectName = "Living docs sample",
                    Profile = "local",
                    StartedAt = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero),
                    EndedAt = new DateTimeOffset(2026, 5, 7, 10, 1, 0, TimeSpan.Zero),
                    DurationMs = 60_000
                },
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        File = "flows\\search.flow.yaml",
                        Message = "First warning"
                    }
                ],
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        Outcome = RunOutcome.Failed,
                        DurationMs = 1_500,
                        Steps =
                        [
                            new StepRunResult
                            {
                                Name = "browser.open",
                                Outcome = RunOutcome.Passed,
                                DurationMs = 300,
                                Artifacts =
                                [
                                    new EvidenceArtifact
                                    {
                                        Category = "screenshot",
                                        RelativePath = "shots\\search.png",
                                        MediaType = "image/png"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

        WriteRunArtifact(
            workspace,
            projectPath,
            "2026-05-07-002",
            new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-002",
                    ArtifactRoot = "artifact-root-002",
                    ProjectName = "Living docs sample",
                    Profile = "local",
                    StartedAt = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero),
                    EndedAt = new DateTimeOffset(2026, 5, 7, 11, 0, 30, TimeSpan.Zero),
                    DurationMs = 30_000
                },
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        File = "flows\\search.flow.yaml",
                        Message = "Latest warning"
                    }
                ],
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.search",
                        Name = "Search flow",
                        Outcome = RunOutcome.Passed,
                        DurationMs = 900,
                        Steps =
                        [
                            new StepRunResult
                            {
                                Name = "browser.open",
                                Outcome = RunOutcome.Passed,
                                DurationMs = 250,
                                Artifacts =
                                [
                                    new EvidenceArtifact
                                    {
                                        Category = "screenshot",
                                        RelativePath = "shots\\search-pass.png",
                                        MediaType = "image/png"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

        var builder = CreateBuilder();

        var model = await builder.BuildAsync(new DocumentBuildOptions
        {
            ProjectPath = projectPath,
            GitSha = "abc123",
            MaxRecentRuns = 2,
            MaxScreenshots = 2,
            Branding = new DocumentBranding("Executive view", null, "#4f46e5")
        });

        Assert.Equal("Living docs sample", model.Meta.ProjectName);
        Assert.Equal("abc123", model.Meta.GitSha);
        Assert.Equal("Executive view", model.Branding.Title);
        Assert.NotNull(model.Metrics);
        Assert.Equal(2, model.RecentRuns.Count);
        Assert.Equal("run-002", model.RecentRuns[0].RunId);
        Assert.Single(model.Flows);
        Assert.Equal("Search flow", model.Flows[0].Name);
        Assert.Equal("flaky", model.Flows[0].Status);
        Assert.Equal(1, model.Suite.TotalFlows);
        Assert.Equal(1, model.Suite.PassedRuns);
        Assert.Equal(1, model.Suite.FailedRuns);
        Assert.Equal(2, model.Screenshots.Count);
        Assert.Contains(model.Screenshots, shot => shot.FilePath.EndsWith("search-pass.png", StringComparison.OrdinalIgnoreCase));
        Assert.Single(model.Diagnostics);
        Assert.Equal("Latest warning", model.Diagnostics[0].Message);
    }

    [Fact]
    public async Task BuildAsync_honors_cancellation()
    {
        using var workspace = new TemporaryWorkspace();
        var builder = CreateBuilder();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => builder.BuildAsync(
            new DocumentBuildOptions { ProjectPath = workspace.CreateProjectRoot() },
            cts.Token));
    }

    [Fact]
    public async Task BuildAsync_falls_back_to_project_folder_name_when_config_has_no_name()
    {
        using var workspace = new TemporaryWorkspace();
        var projectPath = workspace.CreateProjectRoot();
        workspace.WriteFile(Path.Combine(projectPath, ".cress", "config.yaml"), """
        version: 1
        project:
          name: ""
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        """);

        var builder = CreateBuilder();

        var model = await builder.BuildAsync(new DocumentBuildOptions
        {
            ProjectPath = projectPath
        });

        Assert.Equal("project", model.Meta.ProjectName);
    }

    [Fact]
    public async Task BuildAsync_detects_jpg_screenshots_without_media_type_and_honors_limit()
    {
        using var workspace = new TemporaryWorkspace();
        var projectPath = workspace.CreateProjectRoot();
        workspace.WriteFile(Path.Combine(projectPath, ".cress", "config.yaml"), """
        version: 1
        project:
          name: Screenshot sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        """);

        WriteRunArtifact(
            workspace,
            projectPath,
            "2026-05-07-003",
            new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-003",
                    ArtifactRoot = "artifact-root-003",
                    ProjectName = "Screenshot sample",
                    Profile = "local",
                    StartedAt = new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero),
                    EndedAt = new DateTimeOffset(2026, 5, 7, 12, 0, 30, TimeSpan.Zero),
                    DurationMs = 30_000
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.gallery",
                        Name = "Gallery flow",
                        Outcome = RunOutcome.Passed,
                        Steps =
                        [
                            new StepRunResult
                            {
                                Name = "capture.first",
                                Outcome = RunOutcome.Passed,
                                Artifacts =
                                [
                                    new EvidenceArtifact
                                    {
                                        Category = "screenshot",
                                        RelativePath = "shots\\first.jpg"
                                    }
                                ]
                            },
                            new StepRunResult
                            {
                                Name = "capture.second",
                                Outcome = RunOutcome.Passed,
                                Artifacts =
                                [
                                    new EvidenceArtifact
                                    {
                                        Category = "screenshot",
                                        RelativePath = "shots\\second.jpg"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

        var builder = CreateBuilder();

        var model = await builder.BuildAsync(new DocumentBuildOptions
        {
            ProjectPath = projectPath,
            MaxScreenshots = 1
        });

        var screenshot = Assert.Single(model.Screenshots);
        Assert.EndsWith("first.jpg", screenshot.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_uses_flow_id_when_run_name_is_missing_and_collects_image_media_type_artifact()
    {
        using var workspace = new TemporaryWorkspace();
        var projectPath = workspace.CreateProjectRoot();
        workspace.WriteFile(Path.Combine(projectPath, ".cress", "config.yaml"), """
        version: 1
        project:
          name: Media sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        """);

        WriteRunArtifact(
            workspace,
            projectPath,
            "2026-05-07-004",
            new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-004",
                    ArtifactRoot = "artifact-root-004",
                    ProjectName = "Media sample",
                    Profile = "local",
                    StartedAt = new DateTimeOffset(2026, 5, 7, 13, 0, 0, TimeSpan.Zero),
                    EndedAt = new DateTimeOffset(2026, 5, 7, 13, 0, 30, TimeSpan.Zero),
                    DurationMs = 30_000
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.unnamed",
                        Name = "",
                        Outcome = RunOutcome.Passed,
                        DurationMs = 250,
                        Steps =
                        [
                            new StepRunResult
                            {
                                Name = "capture.preview",
                                Outcome = RunOutcome.Passed,
                                Artifacts =
                                [
                                    new EvidenceArtifact
                                    {
                                        Category = "screenshot",
                                        RelativePath = "shots\\preview.bin",
                                        MediaType = "image/gif"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

        var builder = CreateBuilder();

        var model = await builder.BuildAsync(new DocumentBuildOptions
        {
            ProjectPath = projectPath,
            MaxScreenshots = 2
        });

        Assert.Single(model.Flows);
        Assert.Equal("flow.unnamed", model.Flows[0].Name);
        var screenshot = Assert.Single(model.Screenshots);
        Assert.EndsWith("preview.bin", screenshot.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFlowSummaries_falls_back_to_latest_run_when_metrics_are_missing()
    {
        var latestRun = CreateStoredRun(
            new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-latest",
                    StartedAt = new DateTimeOffset(2026, 5, 7, 14, 0, 0, TimeSpan.Zero)
                },
                Flows =
                [
                    new FlowRunResult
                    {
                        FlowId = "flow.pass",
                        Name = "Passing flow",
                        Outcome = RunOutcome.Passed,
                        DurationMs = 1200
                    },
                    new FlowRunResult
                    {
                        FlowId = "flow.fail",
                        Name = "Failing flow",
                        Outcome = RunOutcome.Failed,
                        DurationMs = 450
                    }
                ]
            });

        var summaries = InvokePrivateStatic<IReadOnlyList<FlowSummary>>(
            "BuildFlowSummaries",
            null,
            new List<StoredRunResult> { latestRun });

        Assert.Equal(2, summaries.Count);
        Assert.Collection(
            summaries,
            first =>
            {
                Assert.Equal("flow.pass", first.Id);
                Assert.Equal("Passing flow", first.Name);
                Assert.Equal("passing", first.Status);
                Assert.Equal(1.0, first.PassRate);
                Assert.Equal(TimeSpan.FromMilliseconds(1200), first.AvgDuration);
            },
            second =>
            {
                Assert.Equal("flow.fail", second.Id);
                Assert.Equal("Failing flow", second.Name);
                Assert.Equal("failing", second.Status);
                Assert.Equal(0.0, second.PassRate);
                Assert.Equal(TimeSpan.FromMilliseconds(450), second.AvgDuration);
            });
    }

    [Fact]
    public void BuildSuiteSummary_falls_back_to_latest_run_statistics_when_metrics_are_missing()
    {
        var latestRun = CreateStoredRun(
            new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-suite",
                    StartedAt = new DateTimeOffset(2026, 5, 7, 15, 0, 0, TimeSpan.Zero)
                },
                Flows =
                [
                    new FlowRunResult { FlowId = "flow.one", Name = "Flow One", Outcome = RunOutcome.Passed, DurationMs = 1000 },
                    new FlowRunResult { FlowId = "flow.two", Name = "Flow Two", Outcome = RunOutcome.Failed, DurationMs = 500 }
                ]
            });

        var suite = InvokePrivateStatic<SuiteSummary>(
            "BuildSuiteSummary",
            null,
            new List<StoredRunResult> { latestRun },
            Array.Empty<FlowSummary>());

        Assert.Equal(2, suite.TotalFlows);
        Assert.Equal(1, suite.PassedRuns);
        Assert.Equal(1, suite.FailedRuns);
        Assert.Equal(0.5, suite.PassRate);
        Assert.Equal(TimeSpan.FromMilliseconds(750), suite.AvgDuration);
    }

    [Fact]
    public void BuildSuiteSummary_handles_latest_run_with_no_flows()
    {
        var latestRun = CreateStoredRun(
            new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-empty",
                    StartedAt = new DateTimeOffset(2026, 5, 7, 16, 0, 0, TimeSpan.Zero)
                },
                Flows = []
            });

        var suite = InvokePrivateStatic<SuiteSummary>(
            "BuildSuiteSummary",
            null,
            new List<StoredRunResult> { latestRun },
            Array.Empty<FlowSummary>());

        Assert.Equal(0, suite.TotalFlows);
        Assert.Equal(0, suite.PassedRuns);
        Assert.Equal(0, suite.FailedRuns);
        Assert.Equal(0.0, suite.PassRate);
        Assert.Equal(TimeSpan.Zero, suite.AvgDuration);
    }

    [Fact]
    public void CollectDiagnostics_uses_empty_flow_id_when_file_is_missing()
    {
        var latestRun = CreateStoredRun(
            new RunResult
            {
                Metadata = new RunMetadata
                {
                    RunId = "run-diagnostics",
                    StartedAt = new DateTimeOffset(2026, 5, 7, 17, 0, 0, TimeSpan.Zero)
                },
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        File = null,
                        Message = "Missing file context"
                    }
                ]
            });

        var diagnostics = InvokePrivateStatic<IReadOnlyList<DiagnosticEntry>>(
            "CollectDiagnostics",
            new List<StoredRunResult> { latestRun });

        var entry = Assert.Single(diagnostics);
        Assert.Equal("Error", entry.Severity);
        Assert.Equal(string.Empty, entry.FlowId);
        Assert.Equal("Missing file context", entry.Message);
    }

    private static DocumentBuilder CreateBuilder()
        => new(
            new RunResultRepository(),
            new RunMetricsService(),
            new FlowParser(),
            new ConfigLoader(new ProjectLocator()));

    private static StoredRunResult CreateStoredRun(RunResult result)
        => new()
        {
            Result = result,
            ArtifactDirectory = Path.Combine(Path.GetTempPath(), "cress-livingdocs-tests", Guid.NewGuid().ToString("N"))
        };

    private static T InvokePrivateStatic<T>(string methodName, params object?[] arguments)
    {
        var method = typeof(DocumentBuilder).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<T>(method.Invoke(null, arguments));
    }

    private static void WriteRunArtifact(TemporaryWorkspace workspace, string projectPath, string directoryName, RunResult result)
    {
        var artifactDirectory = Path.Combine(projectPath, ".cress", "artifacts", directoryName);
        Directory.CreateDirectory(artifactDirectory);

        foreach (var artifact in result.Flows.SelectMany(flow => flow.Steps).SelectMany(step => step.Artifacts))
        {
            var path = Path.Combine(artifactDirectory, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "image-bytes");
        }

        File.WriteAllText(
            Path.Combine(artifactDirectory, "result.json"),
            JsonSerializer.Serialize(result, SerializerOptions));
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cress-livingdocs-tests", Guid.NewGuid().ToString("N"));

        public string CreateProjectRoot()
        {
            Directory.CreateDirectory(_root);
            return Path.Combine(_root, "project");
        }

        public void WriteFile(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
    }
}
