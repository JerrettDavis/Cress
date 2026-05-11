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

    private static DocumentBuilder CreateBuilder()
        => new(
            new RunResultRepository(),
            new RunMetricsService(),
            new FlowParser(),
            new ConfigLoader(new ProjectLocator()));

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
