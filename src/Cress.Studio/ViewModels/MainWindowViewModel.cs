using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Studio.Services;

namespace Cress.Studio.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly StudioProjectService _projectService;
    private readonly FlowDocumentService _flowDocumentService;
    private readonly RuntimeOrchestrator _runtimeOrchestrator;
    private readonly StudioAuthoringService _authoringService;
    private readonly StudioRunInsightsService _runInsightsService;

    private StudioProjectSnapshot? _snapshot;
    private FlowDocumentViewModel? _selectedFlow;
    private string _projectRoot = "Open a Cress project to begin.";
    private string _statusMessage = "Ready.";
    private string _selectedProfile = "local";
    private string _retryCountOverrideText = "0";
    private string _selectedScreenshotPolicy = "on-failure";
    private string _explorerFilter = string.Empty;
    private string _runFilter = string.Empty;
    private string _lastRunSummary = "No runs yet";
    private string _selectedAssetSummary = "Select a flow, capability, fixture, step, or run artifact.";
    private string _sourceEditorText = string.Empty;
    private string _liveRunHeadline = "No run in progress.";
    private RunItemViewModel? _selectedRun;
    private RunFlowItemViewModel? _selectedRunFlow;
    private StepRunResult? _selectedRunStep;
    private ArtifactItemViewModel? _selectedArtifact;
    private string _previewText = string.Empty;
    private ImageSource? _previewImage;
    private FlowEditorAnalysis _flowAnalysis = new();
    private string? _selectedQuickActionId;
    private string _selectedRunComparison = "No run selected.";

    public MainWindowViewModel(
        StudioProjectService projectService,
        FlowDocumentService flowDocumentService,
        RuntimeOrchestrator runtimeOrchestrator,
        StudioAuthoringService authoringService,
        StudioRunInsightsService runInsightsService)
    {
        _projectService = projectService;
        _flowDocumentService = flowDocumentService;
        _runtimeOrchestrator = runtimeOrchestrator;
        _authoringService = authoringService;
        _runInsightsService = runInsightsService;

        OpenProjectCommand = new RelayCommand(OpenProject);
        RefreshProjectCommand = new RelayCommand(RefreshProject, () => _snapshot is not null);
        NewFlowCommand = new RelayCommand(CreateNewFlow, () => _snapshot is not null);
        SaveFlowCommand = new RelayCommand(SaveSelectedFlow, () => SelectedFlow is not null);
        ApplySourceCommand = new RelayCommand(ApplySource, () => SelectedFlow is not null);
        RebuildSourceCommand = new RelayCommand(RebuildSource, () => SelectedFlow is not null);
        ApplyQuickActionCommand = new RelayCommand(ApplyQuickAction, () => SelectedFlow is not null && !string.IsNullOrWhiteSpace(SelectedQuickActionId));
        RunSelectedCommand = new AsyncRelayCommand(RunSelectedAsync, () => _snapshot is not null && SelectedFlow is not null);
        RunAllCommand = new AsyncRelayCommand(RunAllAsync, () => _snapshot is not null);
        RerunFailedCommand = new AsyncRelayCommand(RerunFailedAsync, () => SelectedRun is not null);
        RerunFromStepCommand = new AsyncRelayCommand(RerunFromStepAsync, () => SelectedRunFlow?.Flow.SourceFile is not null);
        OpenSelectedSourceCommand = new RelayCommand(OpenSelectedSource, () => !string.IsNullOrWhiteSpace(SelectedFlow?.FilePath));
        OpenSelectedArtifactCommand = new RelayCommand(OpenSelectedArtifact, () => SelectedArtifact is not null);
    }

    public ObservableCollection<ExplorerNodeViewModel> ExplorerNodes { get; } = [];
    public ObservableCollection<string> AvailableProfiles { get; } = [];
    public ObservableCollection<string> CapabilityOptions { get; } = [];
    public ObservableCollection<CapabilityCoverageItemViewModel> CapabilityCoverage { get; } = [];
    public ObservableCollection<FlowQuickAction> QuickActions { get; } = [];
    public ObservableCollection<StudioRecentActivityItem> RecentActivity { get; } = [];
    public ObservableCollection<StudioFlowHealthItem> FlakyFlows { get; } = [];
    public ObservableCollection<string> LiveEvents { get; } = [];
    public ObservableCollection<RunItemViewModel> Runs { get; } = [];
    public ObservableCollection<RunFlowItemViewModel> SelectedRunFlows { get; } = [];
    public ObservableCollection<StepRunResult> SelectedRunSteps { get; } = [];
    public ObservableCollection<ArtifactItemViewModel> SelectedRunArtifacts { get; } = [];
    public ObservableCollection<DiagnosticItemViewModel> Diagnostics { get; } = [];
    public IReadOnlyList<string> ScreenshotPolicyOptions { get; } = ["on-failure", "every-step", "off"];

    public RelayCommand OpenProjectCommand { get; }
    public RelayCommand RefreshProjectCommand { get; }
    public RelayCommand NewFlowCommand { get; }
    public RelayCommand SaveFlowCommand { get; }
    public RelayCommand ApplySourceCommand { get; }
    public RelayCommand RebuildSourceCommand { get; }
    public RelayCommand ApplyQuickActionCommand { get; }
    public AsyncRelayCommand RunSelectedCommand { get; }
    public AsyncRelayCommand RunAllCommand { get; }
    public AsyncRelayCommand RerunFailedCommand { get; }
    public AsyncRelayCommand RerunFromStepCommand { get; }
    public RelayCommand OpenSelectedSourceCommand { get; }
    public RelayCommand OpenSelectedArtifactCommand { get; }

    public string ProjectRoot
    {
        get => _projectRoot;
        private set => SetProperty(ref _projectRoot, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SelectedProfile
    {
        get => _selectedProfile;
        set => SetProperty(ref _selectedProfile, value);
    }

    public string RetryCountOverrideText
    {
        get => _retryCountOverrideText;
        set => SetProperty(ref _retryCountOverrideText, value);
    }

    public string SelectedScreenshotPolicy
    {
        get => _selectedScreenshotPolicy;
        set => SetProperty(ref _selectedScreenshotPolicy, value);
    }

    public string ExplorerFilter
    {
        get => _explorerFilter;
        set
        {
            if (SetProperty(ref _explorerFilter, value))
            {
                RebuildExplorerNodes();
            }
        }
    }

    public string RunFilter
    {
        get => _runFilter;
        set
        {
            if (SetProperty(ref _runFilter, value))
            {
                RefreshRunsCollection();
                RebuildExplorerNodes();
            }
        }
    }

    public string LastRunSummary
    {
        get => _lastRunSummary;
        private set => SetProperty(ref _lastRunSummary, value);
    }

    public string SelectedAssetSummary
    {
        get => _selectedAssetSummary;
        private set => SetProperty(ref _selectedAssetSummary, value);
    }

    public FlowDocumentViewModel? SelectedFlow
    {
        get => _selectedFlow;
        private set
        {
            if (SetProperty(ref _selectedFlow, value))
            {
                OnPropertyChanged(nameof(HasSelectedFlow));
                SaveFlowCommand.RaiseCanExecuteChanged();
                ApplySourceCommand.RaiseCanExecuteChanged();
                RebuildSourceCommand.RaiseCanExecuteChanged();
                ApplyQuickActionCommand.RaiseCanExecuteChanged();
                OpenSelectedSourceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedFlow => SelectedFlow is not null;

    public string SourceEditorText
    {
        get => _sourceEditorText;
        set => SetProperty(ref _sourceEditorText, value);
    }

    public string LiveRunHeadline
    {
        get => _liveRunHeadline;
        private set => SetProperty(ref _liveRunHeadline, value);
    }

    public int FlowCount => _snapshot?.Catalog.NormalizedFlows.Count ?? 0;
    public int CapabilityCount => _snapshot?.Catalog.Capabilities.Count ?? 0;
    public int FixtureCount => _snapshot?.Catalog.FixtureDefinitions.Count ?? 0;
    public int RunCount => Runs.Count;

    public RunItemViewModel? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (SetProperty(ref _selectedRun, value))
            {
                PopulateRunDetails();
                UpdateRunComparison();
                RerunFailedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RunFlowItemViewModel? SelectedRunFlow
    {
        get => _selectedRunFlow;
        set
        {
            if (SetProperty(ref _selectedRunFlow, value))
            {
                _selectedRunStep = null;
                OnPropertyChanged(nameof(SelectedRunStep));
                PopulateArtifacts();
            }
        }
    }

    public StepRunResult? SelectedRunStep
    {
        get => _selectedRunStep;
        set
        {
            if (SetProperty(ref _selectedRunStep, value))
            {
                PopulateArtifacts();
                RerunFromStepCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ArtifactItemViewModel? SelectedArtifact
    {
        get => _selectedArtifact;
        set
        {
            if (SetProperty(ref _selectedArtifact, value))
            {
                OpenSelectedArtifactCommand.RaiseCanExecuteChanged();
                UpdatePreview();
            }
        }
    }

    public string PreviewText
    {
        get => _previewText;
        private set
        {
            if (SetProperty(ref _previewText, value))
            {
                OnPropertyChanged(nameof(HasPreviewText));
            }
        }
    }

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (SetProperty(ref _previewImage, value))
            {
                OnPropertyChanged(nameof(HasPreviewImage));
            }
        }
    }

    public bool HasPreviewText => !string.IsNullOrWhiteSpace(PreviewText);
    public bool HasPreviewImage => PreviewImage is not null;

    public FlowEditorAnalysis FlowAnalysis
    {
        get => _flowAnalysis;
        private set
        {
            if (SetProperty(ref _flowAnalysis, value))
            {
                OnPropertyChanged(nameof(FlowAnalysisDetails));
            }
        }
    }

    public string? SelectedQuickActionId
    {
        get => _selectedQuickActionId;
        set
        {
            if (SetProperty(ref _selectedQuickActionId, value))
            {
                ApplyQuickActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedRunComparison
    {
        get => _selectedRunComparison;
        private set => SetProperty(ref _selectedRunComparison, value);
    }

    public string FlowAnalysisDetails
        => FlowAnalysis.Diagnostics.Count == 0
            ? FlowAnalysis.Summary
            : string.Join(Environment.NewLine, FlowAnalysis.Diagnostics.Select(item => $"{item.Severity}: {item.Message}"));

    public void Initialize(string? startupPath)
    {
        var target = string.IsNullOrWhiteSpace(startupPath) ? Environment.CurrentDirectory : startupPath;
        LoadProject(target);
        if (string.Equals(Environment.GetEnvironmentVariable("CRESS_STUDIO_AUTO_SELECT_FIRST_FLOW"), "1", StringComparison.Ordinal))
        {
            var sourceFile = _snapshot?.Catalog.NormalizedFlows.FirstOrDefault()?.SourceFile;
            if (!string.IsNullOrWhiteSpace(sourceFile))
            {
                LoadFlow(sourceFile);
            }
        }
    }

    public void SelectNode(ExplorerNodeViewModel node)
    {
        switch (node.Kind)
        {
            case "flow" when node.Path is not null:
                LoadFlow(node.Path);
                break;
            case "capability" when node.Payload is CressCapability capability:
                SelectedAssetSummary = BuildCapabilitySummary(capability);
                SourceEditorText = capability.SourceFile is not null && File.Exists(capability.SourceFile)
                    ? File.ReadAllText(capability.SourceFile)
                    : string.Empty;
                SelectedFlow = null;
                break;
            case "fixture" when node.Payload is KeyValuePair<string, FixtureDefinition> fixture:
                SelectedAssetSummary = BuildFixtureSummary(fixture.Key, fixture.Value);
                SourceEditorText = fixture.Value.SourceFile is not null && File.Exists(fixture.Value.SourceFile)
                    ? File.ReadAllText(fixture.Value.SourceFile)
                    : string.Empty;
                SelectedFlow = null;
                break;
            case "step" when node.Payload is StepDefinition step:
                SelectedAssetSummary = BuildStepSummary(step);
                SourceEditorText = step.SourceFile is not null && File.Exists(step.SourceFile)
                    ? File.ReadAllText(step.SourceFile)
                    : string.Empty;
                SelectedFlow = null;
                break;
            case "run" when node.Payload is RunItemViewModel run:
                SelectedRun = run;
                SelectedAssetSummary = $"Run {run.StoredRun.Result.Metadata.RunId}{Environment.NewLine}{run.StoredRun.Result.Metadata.ArtifactRoot}";
                SelectedFlow = null;
                break;
        }
    }

    private void OpenProject()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Cress config (*.yaml)|*.yaml",
            Title = "Open Cress project config",
            FileName = "config.yaml"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadProject(Path.GetDirectoryName(Path.GetDirectoryName(dialog.FileName)) ?? dialog.FileName);
        }
    }

    private void RefreshProject()
    {
        if (_snapshot is not null)
        {
            LoadProject(_snapshot.Catalog.ProjectRoot, SelectedProfile);
        }
    }

    private void LoadProject(string startDirectory, string? profile = null)
    {
        var result = _projectService.Load(startDirectory, profile);
        Diagnostics.Clear();
        foreach (var diagnostic in result.Diagnostics.Select(ToDiagnosticItem))
        {
            Diagnostics.Add(diagnostic);
        }

        if (result.Value is null)
        {
            StatusMessage = result.Diagnostics.Count == 0 ? "Could not open project." : result.Diagnostics[0].Message;
            return;
        }

        _snapshot = result.Value;
        ProjectRoot = _snapshot.Catalog.ProjectRoot;
        StatusMessage = $"Loaded {_snapshot.Catalog.EffectiveConfig.Config.Project.Name}.";
        SelectedProfile = _snapshot.Catalog.EffectiveConfig.ActiveProfile;
        RetryCountOverrideText = _snapshot.Catalog.EffectiveConfig.Config.Defaults.Retries.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SelectedScreenshotPolicy = _snapshot.Catalog.EffectiveConfig.Profile.Evidence?.ScreenshotPolicy ?? "on-failure";

        AvailableProfiles.Clear();
        foreach (var profileName in _snapshot.Catalog.Profiles.Select(item => item.Profile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AvailableProfiles.Add(profileName);
        }

        CapabilityOptions.Clear();
        CapabilityOptions.Add(string.Empty);
        foreach (var capability in _snapshot.Catalog.Capabilities.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            CapabilityOptions.Add(capability.Id);
        }

        CapabilityCoverage.Clear();
        foreach (var item in _snapshot.CapabilityCoverage)
        {
            CapabilityCoverage.Add(new CapabilityCoverageItemViewModel
            {
                CapabilityId = item.CapabilityId,
                CapabilityName = item.CapabilityName,
                FlowCount = item.FlowCount,
                LatestOutcome = item.LatestOutcome,
                AcceptanceCriteriaCount = item.AcceptanceCriteriaCount
            });
        }

        RecentActivity.Clear();
        foreach (var item in _snapshot.RunInsights.RecentActivity)
        {
            RecentActivity.Add(item);
        }

        FlakyFlows.Clear();
        foreach (var item in _snapshot.RunInsights.FlakyFlows)
        {
            FlakyFlows.Add(item);
        }

        RebuildExplorerNodes();
        RefreshRunsCollection();

        LastRunSummary = Runs.FirstOrDefault()?.Summary ?? "No runs yet";
        SelectedRun = Runs.FirstOrDefault();
        SelectedAssetSummary = $"Project root: {_snapshot.Catalog.ProjectRoot}{Environment.NewLine}Active profile: {_snapshot.Catalog.EffectiveConfig.ActiveProfile}";
        SourceEditorText = string.Empty;
        SelectedFlow = null;
        FlowAnalysis = new();
        OnPropertyChanged(nameof(FlowCount));
        OnPropertyChanged(nameof(CapabilityCount));
        OnPropertyChanged(nameof(FixtureCount));
        OnPropertyChanged(nameof(RunCount));
        RefreshCommandStates();
    }

    private ExplorerNodeViewModel BuildFlowGroup(StudioProjectSnapshot snapshot)
    {
        var root = new ExplorerNodeViewModel { DisplayName = $"Flows ({snapshot.Catalog.NormalizedFlows.Count})", Kind = "group" };
        foreach (var flow in snapshot.Catalog.NormalizedFlows
                     .Where(flow => MatchesExplorerFilter(flow.Name, flow.FlowId))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            root.Children.Add(new ExplorerNodeViewModel
            {
                DisplayName = $"{flow.Name} ({flow.FlowId})",
                Kind = "flow",
                Path = flow.SourceFile,
                Payload = flow
            });
        }

        return root;
    }

    private ExplorerNodeViewModel BuildCapabilityGroup(StudioProjectSnapshot snapshot)
    {
        var root = new ExplorerNodeViewModel { DisplayName = $"Capabilities ({snapshot.Catalog.Capabilities.Count})", Kind = "group" };
        foreach (var capability in snapshot.Catalog.Capabilities
                     .Where(capability => MatchesExplorerFilter(capability.Name, capability.Id))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            root.Children.Add(new ExplorerNodeViewModel
            {
                DisplayName = $"{capability.Name} ({capability.Id})",
                Kind = "capability",
                Path = capability.SourceFile,
                Payload = capability
            });
        }

        return root;
    }

    private ExplorerNodeViewModel BuildFixtureGroup(StudioProjectSnapshot snapshot)
    {
        var root = new ExplorerNodeViewModel { DisplayName = $"Fixtures ({snapshot.Catalog.FixtureDefinitions.Count})", Kind = "group" };
        foreach (var fixture in snapshot.Catalog.FixtureDefinitions
                     .Where(item => MatchesExplorerFilter(item.Key, item.Value.Type))
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            root.Children.Add(new ExplorerNodeViewModel
            {
                DisplayName = fixture.Key,
                Kind = "fixture",
                Path = fixture.Value.SourceFile,
                Payload = fixture
            });
        }

        return root;
    }

    private ExplorerNodeViewModel BuildStepGroup(StudioProjectSnapshot snapshot)
    {
        var root = new ExplorerNodeViewModel { DisplayName = $"Steps ({snapshot.Catalog.StepRegistry.Definitions.Count})", Kind = "group" };
        foreach (var step in snapshot.Catalog.StepRegistry.Definitions.Values
                     .Where(step => MatchesExplorerFilter(step.Name, step.Description))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            root.Children.Add(new ExplorerNodeViewModel
            {
                DisplayName = step.Name,
                Kind = "step",
                Path = step.SourceFile,
                Payload = step
            });
        }

        return root;
    }

    private ExplorerNodeViewModel BuildRunGroup(StudioProjectSnapshot snapshot)
    {
        var root = new ExplorerNodeViewModel { DisplayName = $"Runs ({snapshot.Runs.Count})", Kind = "group" };
        foreach (var run in snapshot.Runs
                     .Select(item => new RunItemViewModel { StoredRun = item })
                     .Where(run => MatchesRunFilter(run)))
        {
            root.Children.Add(new ExplorerNodeViewModel
            {
                DisplayName = run.DisplayName,
                Kind = "run",
                Payload = run
            });
        }

        return root;
    }

    private void LoadFlow(string filePath)
    {
        var result = _flowDocumentService.Load(filePath);
        foreach (var diagnostic in result.Diagnostics.Select(ToDiagnosticItem))
        {
            Diagnostics.Add(diagnostic);
        }

        if (result.Value is null)
        {
            StatusMessage = $"Could not load {filePath}.";
            return;
        }

        SelectedFlow = FlowDocumentViewModel.FromDocument(result.Value);
        SourceEditorText = SelectedFlow.SourceText;
        SelectedAssetSummary = $"Editing {SelectedFlow.Name}{Environment.NewLine}{SelectedFlow.FilePath}";
        RefreshFlowAnalysis();
        RefreshCommandStates();
    }

    private void CreateNewFlow()
    {
        if (_snapshot is null)
        {
            return;
        }

        var document = _flowDocumentService.CreateNew(_snapshot.Catalog.ProjectRoot, _snapshot.Catalog.EffectiveConfig.Config.Paths.Flows);
        SelectedFlow = FlowDocumentViewModel.FromDocument(document);
        SourceEditorText = SelectedFlow.SourceText;
        SelectedAssetSummary = $"Creating {SelectedFlow.Name}{Environment.NewLine}{SelectedFlow.FilePath}";
        StatusMessage = "New flow created in the designer.";
        RefreshFlowAnalysis();
        RefreshCommandStates();
    }

    private void SaveSelectedFlow()
    {
        if (SelectedFlow is null)
        {
            return;
        }

        var document = SelectedFlow.ToDocument();
        var result = _flowDocumentService.Save(document);
        foreach (var diagnostic in result.Diagnostics.Select(ToDiagnosticItem))
        {
            Diagnostics.Add(diagnostic);
        }

        if (result.Value is null)
        {
            StatusMessage = "Flow save failed. Review diagnostics.";
            return;
        }

        SelectedFlow.SourceText = result.Value;
        SourceEditorText = result.Value;
        StatusMessage = $"Saved {SelectedFlow.Name}.";
        RefreshProject();
        LoadFlow(document.FilePath);
    }

    private void ApplySource()
    {
        if (SelectedFlow is null)
        {
            return;
        }

        var result = _flowDocumentService.LoadFromSource(SourceEditorText, SelectedFlow.FilePath);
        foreach (var diagnostic in result.Diagnostics.Select(ToDiagnosticItem))
        {
            Diagnostics.Add(diagnostic);
        }

        if (result.Value is null)
        {
            StatusMessage = "Source could not be parsed. Review diagnostics.";
            return;
        }

        result.Value.SourceText = SourceEditorText;
        SelectedFlow = FlowDocumentViewModel.FromDocument(result.Value);
        SelectedAssetSummary = $"Applied source changes to {SelectedFlow.Name}.";
        StatusMessage = "Source applied to designer.";
        RefreshFlowAnalysis();
    }

    private void RebuildSource()
    {
        if (SelectedFlow is null)
        {
            return;
        }

        SourceEditorText = _flowDocumentService.Serialize(SelectedFlow.ToDocument());
        SelectedFlow.SourceText = SourceEditorText;
        StatusMessage = "Source regenerated from designer.";
        RefreshFlowAnalysis();
    }

    private async Task RunSelectedAsync()
    {
        if (_snapshot is null)
        {
            return;
        }

        await RunAsync(new RunOptions
        {
            FlowPath = SelectedFlow?.FilePath,
            Profile = SelectedProfile,
            RetryCountOverride = ParseRetryOverride(),
            ScreenshotPolicyOverride = SelectedScreenshotPolicy
        });
    }

    private async Task RunAllAsync()
    {
        if (_snapshot is null)
        {
            return;
        }

        await RunAsync(new RunOptions
        {
            Profile = SelectedProfile,
            RetryCountOverride = ParseRetryOverride(),
            ScreenshotPolicyOverride = SelectedScreenshotPolicy
        });
    }

    private async Task RerunFailedAsync()
    {
        if (_snapshot is null || SelectedRun is null)
        {
            return;
        }

        var failedFlows = SelectedRun.StoredRun.Result.Flows
            .Where(flow => flow.Outcome is RunOutcome.Failed or RunOutcome.Errored)
            .Select(flow => flow.SourceFile)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();
        if (failedFlows.Count == 0)
        {
            StatusMessage = "The selected run has no failed flows to rerun.";
            return;
        }

        await RunAsync(new RunOptions
        {
            FlowPaths = failedFlows,
            Profile = SelectedProfile,
            RetryCountOverride = ParseRetryOverride(),
            ScreenshotPolicyOverride = SelectedScreenshotPolicy,
            Trigger = "rerun-failed"
        });
    }

    private async Task RerunFromStepAsync()
    {
        if (_snapshot is null || SelectedRunFlow?.Flow.SourceFile is null)
        {
            return;
        }

        await RunAsync(new RunOptions
        {
            FlowPath = SelectedRunFlow.Flow.SourceFile,
            Profile = SelectedProfile,
            StartFromStep = SelectedRunStep?.Name,
            RetryCountOverride = ParseRetryOverride(),
            ScreenshotPolicyOverride = SelectedScreenshotPolicy,
            Trigger = "rerun-from-step"
        });
    }

    private async Task RunAsync(RunOptions options)
    {
        if (_snapshot is null)
        {
            return;
        }

        LiveEvents.Clear();
        LiveRunHeadline = "Run in progress...";
        var progress = new Progress<RuntimeProgressUpdate>(update =>
        {
            var message = update.Kind switch
            {
                RuntimeProgressKind.RunStarted => $"[{update.RunId}] {update.Message}",
                RuntimeProgressKind.FlowStarted => $"[{update.FlowId}] {update.Message}",
                RuntimeProgressKind.StepCompleted => $"[{update.FlowId}] {update.Step?.Name} - {update.Step?.Outcome}",
                RuntimeProgressKind.FlowCompleted => $"[{update.FlowId}] {update.Flow?.Outcome}",
                RuntimeProgressKind.RunCompleted => $"[{update.RunId}] {update.Message}",
                _ => update.Message ?? update.Kind.ToString()
            };
            LiveEvents.Insert(0, message);
            LiveRunHeadline = message;
        });

        var result = await _runtimeOrchestrator.ExecuteAsync(_snapshot.Catalog.ProjectRoot, options, progress);

        LastRunSummary = result.Passed
            ? $"{result.Metadata.RunId} passed"
            : $"{result.Metadata.RunId} finished with failures";

        LiveRunHeadline = LastRunSummary;
        LoadProject(_snapshot.Catalog.ProjectRoot, SelectedProfile);
        SelectedRun = Runs.FirstOrDefault(run => string.Equals(run.StoredRun.Result.Metadata.RunId, result.Metadata.RunId, StringComparison.OrdinalIgnoreCase)) ?? Runs.FirstOrDefault();
        foreach (var diagnostic in result.Diagnostics.Select(ToDiagnosticItem))
        {
            Diagnostics.Add(diagnostic);
        }
    }

    private void PopulateRunDetails()
    {
        SelectedRunFlows.Clear();
        SelectedRunSteps.Clear();
        SelectedRunArtifacts.Clear();
        SelectedArtifact = null;

        if (SelectedRun is null)
        {
            return;
        }

        foreach (var flow in SelectedRun.StoredRun.Result.Flows)
        {
            SelectedRunFlows.Add(new RunFlowItemViewModel { Flow = flow });
        }

        SelectedRunFlow = SelectedRunFlows.FirstOrDefault();
        SelectedAssetSummary = $"Run {SelectedRun.StoredRun.Result.Metadata.RunId}{Environment.NewLine}Artifacts: {SelectedRun.StoredRun.Result.Metadata.ArtifactRoot}";
    }

    private void PopulateArtifacts()
    {
        SelectedRunSteps.Clear();
        SelectedRunArtifacts.Clear();
        SelectedArtifact = null;

        if (SelectedRun is null)
        {
            return;
        }

        var run = SelectedRun.StoredRun.Result;
        if (SelectedRunFlow is not null)
        {
            foreach (var step in SelectedRunFlow.Flow.Steps)
            {
                SelectedRunSteps.Add(step);
            }

            var stepArtifacts = SelectedRunStep is not null
                ? SelectedRunStep.Artifacts
                : SelectedRunFlow.Flow.Steps.SelectMany(step => step.Artifacts);
            foreach (var artifact in stepArtifacts)
            {
                SelectedRunArtifacts.Add(new ArtifactItemViewModel
                {
                    DisplayName = $"{artifact.Category}: {Path.GetFileName(artifact.RelativePath)}",
                    Detail = $"{artifact.Description ?? artifact.RelativePath}{FormatArtifactSize(artifact.SizeBytes)}",
                    Path = Path.Combine(run.Metadata.ArtifactRoot, artifact.RelativePath)
                });
            }
        }

        foreach (var report in run.Reports)
        {
            SelectedRunArtifacts.Add(new ArtifactItemViewModel
            {
                DisplayName = $"report: {report.Key}",
                Detail = report.Value,
                Path = report.Value
            });
        }

        SelectedArtifact = SelectedRunArtifacts.FirstOrDefault();
    }

    private void UpdatePreview()
    {
        PreviewText = string.Empty;
        PreviewImage = null;

        if (SelectedArtifact is null || !File.Exists(SelectedArtifact.Path))
        {
            return;
        }

        var extension = Path.GetExtension(SelectedArtifact.Path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
        {
            PreviewImage = new BitmapImage(new Uri(SelectedArtifact.Path));
            return;
        }

        if (extension is ".json" or ".txt" or ".log" or ".md" or ".yaml" or ".yml" or ".html" or ".xml")
        {
            PreviewText = File.ReadAllText(SelectedArtifact.Path);
            return;
        }

        PreviewText = $"Preview is not available for {extension} files.{Environment.NewLine}{SelectedArtifact.Path}";
    }

    private void OpenSelectedSource()
    {
        if (!string.IsNullOrWhiteSpace(SelectedFlow?.FilePath))
        {
            OpenPath(SelectedFlow.FilePath);
        }
    }

    private void OpenSelectedArtifact()
    {
        if (SelectedArtifact is not null)
        {
            OpenPath(SelectedArtifact.Path);
        }
    }

    private void ApplyQuickAction()
    {
        if (SelectedFlow is null || string.IsNullOrWhiteSpace(SelectedQuickActionId))
        {
            return;
        }

        var updated = _authoringService.ApplyQuickAction(_snapshot?.Catalog, SelectedFlow.ToDocument(), SelectedQuickActionId);
        updated.SourceText = _flowDocumentService.Serialize(updated);
        SelectedFlow = FlowDocumentViewModel.FromDocument(updated);
        SourceEditorText = updated.SourceText;
        RefreshFlowAnalysis();
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void RefreshCommandStates()
    {
        RefreshProjectCommand.RaiseCanExecuteChanged();
        NewFlowCommand.RaiseCanExecuteChanged();
        SaveFlowCommand.RaiseCanExecuteChanged();
        ApplySourceCommand.RaiseCanExecuteChanged();
        RebuildSourceCommand.RaiseCanExecuteChanged();
        ApplyQuickActionCommand.RaiseCanExecuteChanged();
        RunSelectedCommand.RaiseCanExecuteChanged();
        RunAllCommand.RaiseCanExecuteChanged();
        RerunFailedCommand.RaiseCanExecuteChanged();
        RerunFromStepCommand.RaiseCanExecuteChanged();
        OpenSelectedSourceCommand.RaiseCanExecuteChanged();
    }

    private void RefreshFlowAnalysis()
    {
        FlowAnalysis = SelectedFlow is null
            ? new FlowEditorAnalysis()
            : _authoringService.Analyze(_snapshot?.Catalog, SelectedFlow.ToDocument());

        QuickActions.Clear();
        foreach (var action in FlowAnalysis.QuickActions)
        {
            QuickActions.Add(action);
        }

        SelectedQuickActionId ??= QuickActions.FirstOrDefault()?.Id;
    }

    private void RebuildExplorerNodes()
    {
        if (_snapshot is null)
        {
            return;
        }

        ExplorerNodes.Clear();
        ExplorerNodes.Add(BuildFlowGroup(_snapshot));
        ExplorerNodes.Add(BuildCapabilityGroup(_snapshot));
        ExplorerNodes.Add(BuildFixtureGroup(_snapshot));
        ExplorerNodes.Add(BuildStepGroup(_snapshot));
        ExplorerNodes.Add(BuildRunGroup(_snapshot));
    }

    private void RefreshRunsCollection()
    {
        Runs.Clear();
        if (_snapshot is null)
        {
            return;
        }

        foreach (var run in _snapshot.Runs.Select(item => new RunItemViewModel { StoredRun = item }).Where(MatchesRunFilter))
        {
            Runs.Add(run);
        }

        OnPropertyChanged(nameof(RunCount));
    }

    private bool MatchesExplorerFilter(params string?[] values)
        => string.IsNullOrWhiteSpace(ExplorerFilter)
            || values.Any(value => !string.IsNullOrWhiteSpace(value) && value.Contains(ExplorerFilter, StringComparison.OrdinalIgnoreCase));

    private bool MatchesRunFilter(RunItemViewModel run)
        => string.IsNullOrWhiteSpace(RunFilter)
            || run.DisplayName.Contains(RunFilter, StringComparison.OrdinalIgnoreCase)
            || run.Summary.Contains(RunFilter, StringComparison.OrdinalIgnoreCase);

    private int? ParseRetryOverride()
        => int.TryParse(RetryCountOverrideText, out var value) && value >= 0
            ? value
            : null;

    private void UpdateRunComparison()
    {
        if (SelectedRun is null)
        {
            SelectedRunComparison = "No run selected.";
            return;
        }

        var currentIndex = Runs.IndexOf(SelectedRun);
        var previous = currentIndex >= 0 && currentIndex + 1 < Runs.Count ? Runs[currentIndex + 1].StoredRun : null;
        SelectedRunComparison = _runInsightsService.Compare(SelectedRun.StoredRun, previous).Summary;
    }

    private static string FormatArtifactSize(long? sizeBytes)
        => sizeBytes is null ? string.Empty : $" • {sizeBytes.Value / 1024d:0.#} KB";

    private static DiagnosticItemViewModel ToDiagnosticItem(Diagnostic diagnostic)
    {
        var location = string.IsNullOrWhiteSpace(diagnostic.File)
            ? "project"
            : diagnostic.Line is null
                ? diagnostic.File!
                : $"{diagnostic.File}:{diagnostic.Line}:{diagnostic.Column}";

        return new DiagnosticItemViewModel(
            $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}",
            $"{location}{(string.IsNullOrWhiteSpace(diagnostic.Details) ? string.Empty : $"{Environment.NewLine}{diagnostic.Details}")}");
    }

    private static string BuildCapabilitySummary(CressCapability capability)
    {
        var acceptanceCriteria = capability.AcceptanceCriteria is null || capability.AcceptanceCriteria.Count == 0
            ? "None"
            : string.Join(Environment.NewLine, capability.AcceptanceCriteria.Select(item => $"- {item.Id}: {item.Description}"));

        return $"Capability: {capability.Name}{Environment.NewLine}" +
               $"Id: {capability.Id}{Environment.NewLine}" +
               $"Owner: {capability.Owner}{Environment.NewLine}" +
               $"Risk: {capability.Risk}{Environment.NewLine}" +
               $"Rules: {string.Join(", ", capability.Rules ?? [])}{Environment.NewLine}{Environment.NewLine}" +
               $"Acceptance criteria:{Environment.NewLine}{acceptanceCriteria}";
    }

    private static string BuildFixtureSummary(string name, FixtureDefinition fixture)
        => $"Fixture: {name}{Environment.NewLine}" +
           $"Type: {fixture.Type}{Environment.NewLine}" +
           $"Strategy: {fixture.Strategy}{Environment.NewLine}" +
           $"Cleanup: {fixture.Cleanup}{Environment.NewLine}" +
           $"Traits: {string.Join(", ", fixture.Traits)}";

    private static string BuildStepSummary(StepDefinition step)
        => $"Step: {step.Name}{Environment.NewLine}" +
           $"Description: {step.Description}{Environment.NewLine}" +
           $"Drivers: {string.Join(", ", step.Drivers)}{Environment.NewLine}" +
           $"Plugin: {step.Implementation?.Plugin}{Environment.NewLine}" +
           $"Operation: {step.Implementation?.Operation}";
}
