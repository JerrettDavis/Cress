using System.Text;
using Cress.Core.Models;
using Cress.Execution;
using Cress.ProjectSystem;
using Cress.Recorder;
using Cress.Recorder.Inference;
using Cress.Studio.Services;
using Cress.Studio.ViewModels;

namespace Cress.Studio.Web.Services;

public sealed class StudioWorkspaceState : IDisposable
{
    private readonly StudioProjectService _projectService;
    private readonly FlowDocumentService _flowDocumentService;
    private readonly RuntimeOrchestrator _runtimeOrchestrator;
    private readonly StudioSuiteService _suiteService;
    private readonly StudioAuthoringService _authoringService;
    private readonly StudioRunInsightsService _runInsightsService;
    private readonly RunMetricsService _runMetricsService;
    private readonly IStudioRecorderService _recorderService;
    private readonly ProjectLocator _projectLocator;

    private static readonly MetricsOptions DefaultMetricsOptions = new(TimeSpan.FromDays(30), 100);

    public StudioWorkspaceState(
        StudioProjectService projectService,
        FlowDocumentService flowDocumentService,
        RuntimeOrchestrator runtimeOrchestrator,
        StudioSuiteService suiteService,
        StudioAuthoringService authoringService,
        StudioRunInsightsService runInsightsService,
        RunMetricsService runMetricsService,
        IStudioRecorderService recorderService,
        ProjectLocator projectLocator)
    {
        _projectService = projectService;
        _flowDocumentService = flowDocumentService;
        _runtimeOrchestrator = runtimeOrchestrator;
        _suiteService = suiteService;
        _authoringService = authoringService;
        _runInsightsService = runInsightsService;
        _runMetricsService = runMetricsService;
        _recorderService = recorderService;
        _projectLocator = projectLocator;

        _recorderService.StateChanged += OnRecorderStateChanged;

        SuggestedWorkspacePath = ResolveSuggestedWorkspacePath(projectLocator) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(SuggestedWorkspacePath))
        {
            ProjectPathInput = SuggestedWorkspacePath;
            StatusMessage = "Suggested workspace ready. Load it, browse for another folder, or open a demo.";
        }

        DemoWorkspaces = BuildDemoWorkspaces();
        RunnerNodes = BuildRunnerNodes();
        SelectedRunnerNodeId = RunnerNodes.FirstOrDefault()?.Id ?? StudioRunnerNode.LocalNodeId;
    }

    public event Action? Changed;

    // -------------------------------------------------------------------------
    // Recording state (pass-throughs to IStudioRecorderService)
    // -------------------------------------------------------------------------

    /// <summary>Exposed so components (and tests) can call ListAvailableTargetsAsync.</summary>
    public IStudioRecorderService RecorderService => _recorderService;

    public bool IsRecording => _recorderService.IsRecording;
    public int RecordedEventCount => _recorderService.CapturedEventCount;
    public TimeSpan RecordedElapsed => _recorderService.Elapsed;
    public string? RecordingTargetName => _recorderService.CurrentTarget?.MainWindowTitle
                                         ?? _recorderService.CurrentTarget?.ProcessName;
    public IReadOnlyList<RecordedEvent> CurrentEvents => _recorderService.CurrentEvents;
    public IReadOnlyList<InferredStep> CurrentInferredSteps => _recorderService.CurrentInferredSteps;

    /// <summary>True when the target picker modal should be visible.</summary>
    public bool IsRecorderPickerOpen { get; private set; }

    /// <summary>True when the save panel should be visible.</summary>
    public bool IsRecorderSavePanelOpen { get; private set; }

    /// <summary>The last completed recording result (available until next recording starts).</summary>
    public RecordingResult? LastRecordingResult { get; private set; }

    /// <summary>
    /// Non-null when the last recording operation failed. Auto-clears after 8 seconds.
    /// </summary>
    public string? RecordingError { get; private set; }

    public string SuggestedWorkspacePath { get; }
    public List<string> RecentWorkspaces { get; } = [];
    public IReadOnlyList<StudioDemoWorkspace> DemoWorkspaces { get; }
    public IReadOnlyList<StudioRunnerNode> RunnerNodes { get; private set; }
    public string SelectedRunnerNodeId { get; set; } = StudioRunnerNode.LocalNodeId;
    public StudioRunnerNode? SelectedRunnerNode
        => RunnerNodes.FirstOrDefault(node => string.Equals(node.Id, SelectedRunnerNodeId, StringComparison.OrdinalIgnoreCase));

    public bool IsWorkspacePickerOpen { get; private set; }
    public string WorkspaceBrowserCurrentPath { get; private set; } = string.Empty;
    public string WorkspaceBrowserLocationLabel { get; private set; } = "This machine";
    public string? WorkspaceBrowserError { get; private set; }
    public List<StudioDirectoryEntry> WorkspaceBrowserEntries { get; } = [];

    // Used to cancel the auto-clear timer when a new error arrives before the 8 s expires.
    private CancellationTokenSource? _errorClearCts;

    public void OpenRecorderPicker()
    {
        IsRecorderPickerOpen = true;
        NotifyChanged();
    }

    public void CloseRecorderPicker()
    {
        IsRecorderPickerOpen = false;
        NotifyChanged();
    }

    public void OpenSavePanel()
    {
        IsRecorderSavePanelOpen = true;
        NotifyChanged();
    }

    public void CloseSavePanel()
    {
        IsRecorderSavePanelOpen = false;
        LastRecordingResult = null;
        NotifyChanged();
    }

    public async Task BeginRecordingAsync(int processId)
    {
        IsRecorderPickerOpen = false;
        ClearRecordingError();
        try
        {
            await _recorderService.StartRecordingAsync(processId);
            // StateChanged callback will fire NotifyChanged.
        }
        catch (Exception ex)
        {
            SetRecordingError(FormatRecordingError(ex));
        }
    }

    public async Task BeginWebRecordingAsync(string url, string browserType)
    {
        IsRecorderPickerOpen = false;
        ClearRecordingError();
        try
        {
            await _recorderService.StartWebRecordingAsync(url, browserType);
            // StateChanged callback will fire NotifyChanged.
        }
        catch (Exception ex)
        {
            SetRecordingError(FormatWebRecordingError(ex));
        }
    }

    public async Task<RecordingResult> EndRecordingAsync()
    {
        ClearRecordingError();
        try
        {
            var result = await _recorderService.StopRecordingAsync();
            LastRecordingResult = result;
            IsRecorderSavePanelOpen = true;
            NotifyChanged();
            return result;
        }
        catch (Exception ex)
        {
            SetRecordingError(FormatRecordingError(ex));
            NotifyChanged();
            return new RecordingResult();
        }
    }

    public void Dispose()
    {
        _recorderService.StateChanged -= OnRecorderStateChanged;
        _errorClearCts?.Cancel();
        _errorClearCts?.Dispose();
    }

    private void ClearRecordingError()
    {
        _errorClearCts?.Cancel();
        _errorClearCts?.Dispose();
        _errorClearCts = null;
        RecordingError = null;
    }

    private void SetRecordingError(string message)
    {
        _errorClearCts?.Cancel();
        _errorClearCts?.Dispose();
        RecordingError = message;
        NotifyChanged();

        var cts = new CancellationTokenSource();
        _errorClearCts = cts;
        _ = Task.Delay(TimeSpan.FromSeconds(8), cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            RecordingError = null;
            NotifyChanged();
        }, TaskScheduler.Default);
    }

    private static string FormatRecordingError(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => "Access denied — the target process may be elevated (run Studio as Administrator).",
            InvalidOperationException when ex.Message.Contains("exited") => "The target process exited before recording could start.",
            InvalidOperationException when ex.Message.Contains("exit") => "The target process exited during recording.",
            ArgumentException => $"Invalid target: {ex.Message}",
            System.Runtime.InteropServices.COMException com => $"UIA COM error (0x{com.HResult:X8}) — the target may be elevated or incompatible.",
            _ => $"Recording error: {ex.Message}"
        };
    }

    private static string FormatWebRecordingError(Exception ex)
    {
        return ex switch
        {
            InvalidOperationException when ex.Message.Contains("Node.js not found") => ex.Message,
            InvalidOperationException when ex.Message.Contains("node") => $"Web recording error: {ex.Message}",
            _ => $"Web recording error: {ex.Message}"
        };
    }

    private void OnRecorderStateChanged()
        => NotifyChanged();

    // -------------------------------------------------------------------------
    // End recording state
    // -------------------------------------------------------------------------

    public StudioProjectSnapshot? Snapshot { get; private set; }

    /// <summary>
    /// Aggregated run metrics computed from <see cref="Runs"/> using a 30-day/100-run window.
    /// Null until the first project load.
    /// </summary>
    public RunMetrics? CurrentMetrics { get; private set; }

    public List<StudioSuiteDocument> Suites { get; } = [];
    public List<StoredRunResult> Runs { get; } = [];
    public List<StepRunResult> SelectedRunSteps { get; } = [];
    public List<StudioArtifactItem> SelectedRunArtifacts { get; } = [];
    public List<string> LiveEvents { get; } = [];
    public List<Diagnostic> Diagnostics { get; } = [];

    public string ProjectPathInput { get; set; } = string.Empty;
    public string ExplorerFilter { get; set; } = string.Empty;
    public string RunFilter { get; set; } = string.Empty;
    public string ArtifactFilter { get; set; } = string.Empty;
    public string SelectedProfile { get; set; } = "local";
    public string RetryCountOverrideText { get; set; } = "0";
    public string ScreenshotPolicy { get; set; } = "on-failure";
    public string StatusMessage { get; private set; } = "Ready.";
    public string SelectedAssetSummary { get; private set; } = "Load a project to begin.";
    public string SelectionHeadline { get; private set; } = "No selection";
    public string LiveRunHeadline { get; private set; } = "No run in progress.";
    public string SourceEditorText { get; set; } = string.Empty;
    public string PreviewText { get; private set; } = string.Empty;
    public string PreviewImageDataUrl { get; private set; } = string.Empty;
    public bool IsBusy { get; private set; }

    public FlowDocumentViewModel? SelectedFlow { get; private set; }
    public StudioSuiteEditorModel? SelectedSuite { get; private set; }
    public StoredRunResult? SelectedRun { get; private set; }
    public FlowRunResult? SelectedRunFlow { get; private set; }
    public StepRunResult? SelectedRunStep { get; private set; }
    public StudioArtifactItem? SelectedArtifact { get; private set; }
    public FlowEditorAnalysis FlowAnalysis { get; private set; } = new();
    public StudioRunComparison SelectedRunComparison { get; private set; } = new();

    public bool HasLoadedProject => Snapshot is not null;
    public bool HasSelectedFlow => SelectedFlow is not null;
    public bool HasSelectedSuite => SelectedSuite is not null;
    public bool HasRecentWorkspaces => RecentWorkspaces.Count > 0;
    public int FlowCount => Snapshot?.Catalog.NormalizedFlows.Count ?? 0;
    public int CapabilityCount => Snapshot?.Catalog.Capabilities.Count ?? 0;
    public int FixtureCount => Snapshot?.Catalog.FixtureDefinitions.Count ?? 0;
    public int StepCount => Snapshot?.Catalog.StepRegistry.Definitions.Count ?? 0;
    public IReadOnlyList<string> AvailableProfiles
        => Snapshot?.Catalog.Profiles.Select(item => item.Profile).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? ["local"];
    public IReadOnlyList<string> CapabilityOptions
        => Snapshot?.Catalog.Capabilities.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => item.Id).ToList() ?? [];
    public IReadOnlyList<NormalizedFlow> AvailableFlows
        => Snapshot?.Catalog.NormalizedFlows ?? [];
    public StudioRunInsights RunInsights => Snapshot?.RunInsights ?? new();

    public void LoadProject()
    {
        var startDirectory = ResolveProjectStartDirectory();
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            StatusMessage = "Choose a workspace path, browse for a folder, or load one of the demos to continue.";
            NotifyChanged();
            return;
        }

        LoadProjectCore(startDirectory, string.IsNullOrWhiteSpace(SelectedProfile) ? null : SelectedProfile, SelectedFlow?.FilePath, SelectedSuite?.FilePath);
    }

    public void RefreshProject()
    {
        if (Snapshot is not null)
        {
            LoadProjectCore(Snapshot.Catalog.ProjectRoot, SelectedProfile, SelectedFlow?.FilePath, SelectedSuite?.FilePath);
        }
    }

    public void SelectFlow(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var result = _flowDocumentService.Load(filePath);
        AddDiagnostics(result.Diagnostics);
        if (result.Value is null)
        {
            StatusMessage = $"Could not load {filePath}.";
            NotifyChanged();
            return;
        }

        SelectedFlow = FlowDocumentViewModel.FromDocument(result.Value);
        SelectedSuite = null;
        SourceEditorText = SelectedFlow.SourceText;
        SelectedAssetSummary = $"Editing {SelectedFlow.Name}{Environment.NewLine}{SelectedFlow.FilePath}";
        SelectionHeadline = SelectedFlow.Name;
        StatusMessage = $"Loaded flow {SelectedFlow.Name}.";
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
    }

    public void SelectSuite(StudioSuiteDocument suite)
    {
        SelectedSuite = StudioSuiteEditorModel.FromDocument(suite);
        SelectedFlow = null;
        SelectedArtifact = null;
        PreviewText = string.Empty;
        PreviewImageDataUrl = string.Empty;
        SourceEditorText = File.Exists(suite.FilePath) ? File.ReadAllText(suite.FilePath) : string.Empty;
        SelectedAssetSummary = BuildSuiteSummary(SelectedSuite);
        SelectionHeadline = SelectedSuite.Name;
        StatusMessage = $"Loaded suite {SelectedSuite.Name}.";
        NotifyChanged();
    }

    public void SelectCapability(CressCapability capability)
    {
        ClearDesignSelection();
        SourceEditorText = ReadSource(capability.SourceFile);
        SelectedAssetSummary = BuildCapabilitySummary(capability);
        SelectionHeadline = capability.Name;
        NotifyChanged();
    }

    public void SelectFixture(string fixtureName)
    {
        if (Snapshot is null || !Snapshot.Catalog.FixtureDefinitions.TryGetValue(fixtureName, out var fixture))
        {
            return;
        }

        ClearDesignSelection();
        SourceEditorText = ReadSource(fixture.SourceFile);
        SelectedAssetSummary = BuildFixtureSummary(fixtureName, fixture);
        SelectionHeadline = fixtureName;
        NotifyChanged();
    }

    public void SelectStep(string stepName)
    {
        if (Snapshot is null || !Snapshot.Catalog.StepRegistry.Definitions.TryGetValue(stepName, out var step))
        {
            return;
        }

        ClearDesignSelection();
        SourceEditorText = ReadSource(step.SourceFile);
        SelectedAssetSummary = BuildStepSummary(step);
        SelectionHeadline = step.Name;
        NotifyChanged();
    }

    public void SelectRun(StoredRunResult run)
    {
        SelectedRun = run;
        SelectedRunFlow = run.Result.Flows.FirstOrDefault();
        SelectedRunStep = SelectedRunFlow?.Steps.FirstOrDefault();
        SelectedAssetSummary = $"Run {run.Result.Metadata.RunId}{Environment.NewLine}Artifacts: {run.Result.Metadata.ArtifactRoot}";
        SelectionHeadline = run.Result.Metadata.RunId;
        PopulateArtifacts();
        UpdateRunComparison();
        NotifyChanged();
    }

    public void SetProjectPath(string? path)
    {
        ProjectPathInput = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFullPath(path.Trim());
        StatusMessage = string.IsNullOrWhiteSpace(ProjectPathInput)
            ? "Choose a workspace path, browse for a folder, or open a demo."
            : $"Ready to load {ProjectPathInput}.";
        NotifyChanged();
    }

    public void SetRecentWorkspaces(IEnumerable<string> paths)
    {
        RecentWorkspaces.Clear();
        foreach (var path in paths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(path => Path.GetFullPath(path.Trim()))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(Directory.Exists)
                     .Take(8))
        {
            RecentWorkspaces.Add(path);
        }

        if (string.IsNullOrWhiteSpace(ProjectPathInput))
        {
            ProjectPathInput = RecentWorkspaces.FirstOrDefault() ?? SuggestedWorkspacePath;
        }

        NotifyChanged();
    }

    public void UseSuggestedWorkspace()
    {
        if (!string.IsNullOrWhiteSpace(SuggestedWorkspacePath))
        {
            SetProjectPath(SuggestedWorkspacePath);
        }
    }

    public void LoadRecentWorkspace(string path)
    {
        SetProjectPath(path);
        LoadProject();
    }

    public void LoadDemoWorkspace(string demoId)
    {
        var demo = DemoWorkspaces.FirstOrDefault(item => string.Equals(item.Id, demoId, StringComparison.OrdinalIgnoreCase));
        if (demo is null)
        {
            StatusMessage = $"Demo '{demoId}' is not available on this machine.";
            NotifyChanged();
            return;
        }

        if (!string.IsNullOrWhiteSpace(demo.PreferredProfile))
        {
            SelectedProfile = demo.PreferredProfile;
        }

        ProjectPathInput = demo.ProjectPath;
        LoadProjectCore(demo.ProjectPath, string.IsNullOrWhiteSpace(SelectedProfile) ? null : SelectedProfile, null, null);
    }

    public void OpenWorkspacePicker()
    {
        IsWorkspacePickerOpen = true;
        BrowseWorkspacePath(DetermineWorkspaceBrowserStartPath());
    }

    public void CloseWorkspacePicker()
    {
        IsWorkspacePickerOpen = false;
        WorkspaceBrowserError = null;
        NotifyChanged();
    }

    public void BrowseWorkspacePath(string? path)
    {
        WorkspaceBrowserEntries.Clear();
        WorkspaceBrowserError = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            WorkspaceBrowserCurrentPath = string.Empty;
            WorkspaceBrowserLocationLabel = "This machine";
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady).OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase))
            {
                WorkspaceBrowserEntries.Add(new StudioDirectoryEntry(
                    drive.Name,
                    drive.RootDirectory.FullName,
                    IsCressWorkspace: Directory.Exists(Path.Combine(drive.RootDirectory.FullName, ".cress")),
                    ItemCountLabel: drive.VolumeLabel));
            }

            NotifyChanged();
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                WorkspaceBrowserError = $"The directory '{fullPath}' no longer exists.";
                NotifyChanged();
                return;
            }

            WorkspaceBrowserCurrentPath = fullPath;
            WorkspaceBrowserLocationLabel = fullPath;
            foreach (var directory in Directory.GetDirectories(fullPath).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = directory;
                }

                WorkspaceBrowserEntries.Add(new StudioDirectoryEntry(
                    name,
                    directory,
                    IsCressWorkspace: File.Exists(Path.Combine(directory, ".cress", "config.yaml")),
                    ItemCountLabel: BuildChildCountLabel(directory)));
            }
        }
        catch (Exception ex)
        {
            WorkspaceBrowserError = $"Could not open '{path}': {ex.Message}";
        }

        NotifyChanged();
    }

    public void BrowseWorkspaceParent()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceBrowserCurrentPath))
        {
            return;
        }

        var parent = Directory.GetParent(WorkspaceBrowserCurrentPath);
        BrowseWorkspacePath(parent?.FullName);
    }

    public void ChooseWorkspaceFromPicker(string path, bool loadImmediately)
    {
        SetProjectPath(path);
        IsWorkspacePickerOpen = false;
        WorkspaceBrowserError = null;

        if (loadImmediately)
        {
            LoadProject();
            return;
        }

        NotifyChanged();
    }

    public void SelectRunFlow(FlowRunResult flow)
    {
        SelectedRunFlow = flow;
        SelectedRunStep = flow.Steps.FirstOrDefault();
        PopulateArtifacts();
        NotifyChanged();
    }

    public void SelectArtifact(StudioArtifactItem artifact)
    {
        SelectedArtifact = artifact;
        UpdatePreview();
        NotifyChanged();
    }

    public void CreateNewFlow()
    {
        if (Snapshot is null)
        {
            return;
        }

        var document = _flowDocumentService.CreateNew(Snapshot.Catalog.ProjectRoot, Snapshot.Catalog.EffectiveConfig.Config.Paths.Flows);
        SelectedFlow = FlowDocumentViewModel.FromDocument(document);
        SelectedSuite = null;
        SourceEditorText = SelectedFlow.SourceText;
        SelectionHeadline = SelectedFlow.Name;
        SelectedAssetSummary = $"Creating {SelectedFlow.Name}{Environment.NewLine}{SelectedFlow.FilePath}";
        StatusMessage = "New flow created.";
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
    }

    public void SaveSelectedFlow()
    {
        if (SelectedFlow is null)
        {
            return;
        }

        var document = SelectedFlow.ToDocument();
        var result = _flowDocumentService.Save(document);
        AddDiagnostics(result.Diagnostics);
        if (result.Value is null)
        {
            StatusMessage = "Flow save failed. Review diagnostics.";
            NotifyChanged();
            return;
        }

        SourceEditorText = result.Value;
        StatusMessage = $"Saved {SelectedFlow.Name}.";
        LoadProjectCore(Snapshot?.Catalog.ProjectRoot ?? Path.GetDirectoryName(document.FilePath) ?? ProjectPathInput, SelectedProfile, document.FilePath, null);
    }

    public void ApplySource()
    {
        if (SelectedFlow is null)
        {
            return;
        }

        var result = _flowDocumentService.LoadFromSource(SourceEditorText, SelectedFlow.FilePath);
        AddDiagnostics(result.Diagnostics);
        if (result.Value is null)
        {
            StatusMessage = "Source could not be parsed. Review diagnostics.";
            NotifyChanged();
            return;
        }

        result.Value.SourceText = SourceEditorText;
        SelectedFlow = FlowDocumentViewModel.FromDocument(result.Value);
        SelectedAssetSummary = $"Applied source changes to {SelectedFlow.Name}.";
        SelectionHeadline = SelectedFlow.Name;
        StatusMessage = "Source applied.";
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
    }

    public void RebuildSource()
    {
        if (SelectedFlow is null)
        {
            return;
        }

        SourceEditorText = _flowDocumentService.Serialize(SelectedFlow.ToDocument());
        SelectedFlow.SourceText = SourceEditorText;
        StatusMessage = "Source regenerated from the designer.";
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
    }

    public void CreateNewSuite()
    {
        if (Snapshot is null)
        {
            return;
        }

        SelectedSuite = StudioSuiteEditorModel.FromDocument(_suiteService.CreateNew(Snapshot.Catalog.ProjectRoot));
        SelectedFlow = null;
        SourceEditorText = string.Empty;
        SelectionHeadline = SelectedSuite.Name;
        SelectedAssetSummary = BuildSuiteSummary(SelectedSuite);
        StatusMessage = "New suite created.";
        NotifyChanged();
    }

    public void SaveSelectedSuite()
    {
        if (SelectedSuite is null)
        {
            return;
        }

        var document = SelectedSuite.ToDocument();
        var result = _suiteService.Save(document);
        AddDiagnostics(result.Diagnostics);
        if (result.Value is null)
        {
            StatusMessage = "Suite save failed. Review diagnostics.";
            NotifyChanged();
            return;
        }

        SourceEditorText = result.Value;
        StatusMessage = $"Saved {SelectedSuite.Name}.";
        LoadProjectCore(Snapshot?.Catalog.ProjectRoot ?? ProjectPathInput, SelectedProfile, null, document.FilePath);
    }

    public void DeleteSelectedSuite()
    {
        if (SelectedSuite is null)
        {
            return;
        }

        _suiteService.Delete(SelectedSuite.FilePath);
        StatusMessage = $"Deleted suite {SelectedSuite.Name}.";
        LoadProjectCore(Snapshot?.Catalog.ProjectRoot ?? ProjectPathInput, SelectedProfile, SelectedFlow?.FilePath, null);
    }

    public void ToggleSuiteFlow(string flowId, bool selected)
    {
        if (SelectedSuite is null)
        {
            return;
        }

        if (selected)
        {
            SelectedSuite.FlowIds.Add(flowId);
        }
        else
        {
            SelectedSuite.FlowIds.Remove(flowId);
        }

        SelectedAssetSummary = BuildSuiteSummary(SelectedSuite);
        NotifyChanged();
    }

    public Task AddFixtureRowAsync()
    {
        SelectedFlow?.Fixtures.Add(new FlowDocumentViewModel.EditableFixtureRow());
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task RemoveFixtureRowAsync(int index)
    {
        if (SelectedFlow is not null && index >= 0 && index < SelectedFlow.Fixtures.Count)
        {
            SelectedFlow.Fixtures.RemoveAt(index);
            RefreshSelectedFlowAnalysis();
            NotifyChanged();
        }

        return Task.CompletedTask;
    }

    public Task AddActionRowAsync()
    {
        SelectedFlow?.Actions.Add(new FlowDocumentViewModel.EditableExecutableRow());
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task RemoveActionRowAsync(int index)
    {
        if (SelectedFlow is not null && index >= 0 && index < SelectedFlow.Actions.Count)
        {
            SelectedFlow.Actions.RemoveAt(index);
            RefreshSelectedFlowAnalysis();
            NotifyChanged();
        }

        return Task.CompletedTask;
    }

    public Task AddExpectationRowAsync()
    {
        SelectedFlow?.Expectations.Add(new FlowDocumentViewModel.EditableExecutableRow());
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task RemoveExpectationRowAsync(int index)
    {
        if (SelectedFlow is not null && index >= 0 && index < SelectedFlow.Expectations.Count)
        {
            SelectedFlow.Expectations.RemoveAt(index);
            RefreshSelectedFlowAnalysis();
            NotifyChanged();
        }

        return Task.CompletedTask;
    }

    public Task RunSelectedAsync()
        => SelectedFlow is null
            ? Task.CompletedTask
            : ExecuteRunAsync(new RunOptions
            {
                FlowPath = SelectedFlow.FilePath,
                Profile = SelectedProfile,
                RetryCountOverride = ParseRetryOverride(),
                ScreenshotPolicyOverride = ScreenshotPolicy
            }, reloadAtEnd: true);

    public Task RunAllAsync()
        => Snapshot is null
            ? Task.CompletedTask
            : ExecuteRunAsync(new RunOptions
            {
                Profile = SelectedProfile,
                RetryCountOverride = ParseRetryOverride(),
                ScreenshotPolicyOverride = ScreenshotPolicy
            }, reloadAtEnd: true);

    public Task RerunFailedAsync()
    {
        if (SelectedRun is null)
        {
            return Task.CompletedTask;
        }

        var failedFlows = SelectedRun.Result.Flows
            .Where(flow => flow.Outcome is RunOutcome.Failed or RunOutcome.Errored)
            .Select(flow => flow.SourceFile)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();
        if (failedFlows.Count == 0)
        {
            StatusMessage = "The selected run has no failed flows to rerun.";
            NotifyChanged();
            return Task.CompletedTask;
        }

        return ExecuteRunAsync(new RunOptions
        {
            FlowPaths = failedFlows,
            Profile = SelectedProfile,
            RetryCountOverride = ParseRetryOverride(),
            ScreenshotPolicyOverride = ScreenshotPolicy,
            Trigger = "rerun-failed"
        }, reloadAtEnd: true);
    }

    public Task RerunFromStepAsync()
        => SelectedRunFlow is null || string.IsNullOrWhiteSpace(SelectedRunFlow.SourceFile)
            ? Task.CompletedTask
            : ExecuteRunAsync(new RunOptions
            {
                FlowPath = SelectedRunFlow.SourceFile,
                Profile = SelectedProfile,
                StartFromStep = SelectedRunStep?.Name,
                RetryCountOverride = ParseRetryOverride(),
                ScreenshotPolicyOverride = ScreenshotPolicy,
                Trigger = "rerun-from-step"
            }, reloadAtEnd: true);

    public async Task RunSelectedSuiteAsync()
    {
        if (Snapshot is null || SelectedSuite is null)
        {
            return;
        }

        var suiteDocument = SelectedSuite.ToDocument();
        var profile = string.IsNullOrWhiteSpace(suiteDocument.Profile) ? SelectedProfile : suiteDocument.Profile!;
        var reportFormats = ParseCommaSeparated(SelectedSuite.ReportFormatsText);
        var diagnostics = new List<Diagnostic>();
        var selectedFlows = _suiteService.ResolveFlows(Snapshot.Catalog, suiteDocument, diagnostics);
        AddDiagnostics(diagnostics);
        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            StatusMessage = "Suite selection did not resolve to runnable flows.";
            NotifyChanged();
            return;
        }

        if (suiteDocument.FlowIds.Count == 0)
        {
            await ExecuteRunAsync(new RunOptions
            {
                Tag = string.IsNullOrWhiteSpace(suiteDocument.Tag) ? null : suiteDocument.Tag,
                Profile = profile,
                ReportFormats = reportFormats,
                RetryCountOverride = ParseRetryOverride(),
                ScreenshotPolicyOverride = ScreenshotPolicy
            }, reloadAtEnd: true);
            return;
        }

        LiveEvents.Clear();
        LiveRunHeadline = $"Running suite {suiteDocument.Name}...";
        NotifyChanged();

        IsBusy = true;
        NotifyChanged();
        try
        {
            foreach (var flow in selectedFlows)
            {
                await ExecuteRunAsync(new RunOptions
                {
                    FlowPath = flow.SourceFile,
                    Profile = profile,
                    ReportFormats = reportFormats,
                    RetryCountOverride = ParseRetryOverride(),
                    ScreenshotPolicyOverride = ScreenshotPolicy
                }, reloadAtEnd: false, clearLiveEvents: false);
            }

            LoadProjectCore(Snapshot.Catalog.ProjectRoot, profile, null, suiteDocument.FilePath);
            StatusMessage = $"Suite {suiteDocument.Name} completed.";
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
        }
    }

    public string DescribeRun(StoredRunResult run)
        => $"{run.Result.Metadata.Profile} • {run.Result.Flows.Count(flow => flow.Outcome == RunOutcome.Passed)}/{run.Result.Flows.Count} passed • {run.Result.Flows.Count(flow => flow.PassedWithRetry)} retried • {run.Result.Metadata.StartedAt.LocalDateTime:g}";

    public void ApplyQuickAction(string actionId)
    {
        if (SelectedFlow is null)
        {
            return;
        }

        var updated = _authoringService.ApplyQuickAction(Snapshot?.Catalog, SelectedFlow.ToDocument(), actionId);
        updated.SourceText = _flowDocumentService.Serialize(updated);
        SelectedFlow = FlowDocumentViewModel.FromDocument(updated);
        SourceEditorText = updated.SourceText;
        SelectedAssetSummary = $"Applied quick action to {SelectedFlow.Name}.";
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
    }

    public void RefreshFlowAnalysis()
    {
        RefreshSelectedFlowAnalysis();
        NotifyChanged();
    }

    private async Task ExecuteRunAsync(RunOptions options, bool reloadAtEnd, bool clearLiveEvents = true)
    {
        if (Snapshot is null)
        {
            return;
        }

        if (clearLiveEvents)
        {
            LiveEvents.Clear();
        }

        IsBusy = true;
        LiveRunHeadline = $"Dispatching run to {(SelectedRunnerNode?.DisplayName ?? "local embedded node")}...";
        NotifyChanged();

        try
        {
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
                NotifyChanged();
            });

            var result = await _runtimeOrchestrator.ExecuteAsync(Snapshot.Catalog.ProjectRoot, options, progress);
            AddDiagnostics(result.Diagnostics);
            LiveRunHeadline = result.Passed
                ? $"{result.Metadata.RunId} passed"
                : $"{result.Metadata.RunId} finished with failures";
            StatusMessage = $"Completed run {result.Metadata.RunId} on {(SelectedRunnerNode?.DisplayName ?? "local embedded node")}.";

            if (reloadAtEnd)
            {
                LoadProjectCore(Snapshot.Catalog.ProjectRoot, options.Profile ?? SelectedProfile, options.FlowPath, SelectedSuite?.FilePath);
            }
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
        }
    }

    private void LoadProjectCore(string startDirectory, string? profile, string? selectedFlowPath, string? selectedSuitePath)
    {
        var result = _projectService.Load(startDirectory, profile);
        Diagnostics.Clear();
        AddDiagnostics(result.Diagnostics);
        if (result.Value is null)
        {
            Snapshot = null;
            CurrentMetrics = null;
            Suites.Clear();
            Runs.Clear();
            SelectedRunArtifacts.Clear();
            SelectedArtifact = null;
            SelectedRun = null;
            SelectedRunFlow = null;
            SelectedRunStep = null;
            SelectedFlow = null;
            SelectedSuite = null;
            FlowAnalysis = new();
            SelectedRunComparison = new StudioRunComparison { Summary = "No run selected." };
            PreviewText = string.Empty;
            PreviewImageDataUrl = string.Empty;
            StatusMessage = result.Diagnostics.Count == 0 ? "Could not open project." : result.Diagnostics[0].Message;
            NotifyChanged();
            return;
        }

        Snapshot = result.Value;
        ProjectPathInput = Snapshot.Catalog.ProjectRoot;
        RememberWorkspace(Snapshot.Catalog.ProjectRoot);
        SelectedProfile = Snapshot.Catalog.EffectiveConfig.ActiveProfile;
        RetryCountOverrideText = Snapshot.Catalog.EffectiveConfig.Config.Defaults.Retries.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ScreenshotPolicy = Snapshot.Catalog.EffectiveConfig.Profile.Evidence?.ScreenshotPolicy ?? "on-failure";
        StatusMessage = $"Loaded {Snapshot.Catalog.EffectiveConfig.Config.Project.Name}.";

        ReloadSuites(selectedSuitePath);

        Runs.Clear();
        Runs.AddRange(Snapshot.Runs);
        CurrentMetrics = _runMetricsService.Aggregate(Runs, DefaultMetricsOptions);
        SelectedRun = Runs.FirstOrDefault();
        SelectedRunFlow = SelectedRun?.Result.Flows.FirstOrDefault();
        SelectedRunStep = SelectedRunFlow?.Steps.FirstOrDefault();
        PopulateArtifacts();
        UpdateRunComparison();

        SelectionHeadline = Snapshot.Catalog.EffectiveConfig.Config.Project.Name;
        SelectedAssetSummary = $"Project root: {Snapshot.Catalog.ProjectRoot}{Environment.NewLine}Active profile: {Snapshot.Catalog.EffectiveConfig.ActiveProfile}";
        SourceEditorText = string.Empty;
        PreviewText = string.Empty;
        PreviewImageDataUrl = string.Empty;

        if (!string.IsNullOrWhiteSpace(selectedFlowPath))
        {
            SelectFlow(selectedFlowPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedSuitePath))
        {
            var suite = Suites.FirstOrDefault(item => string.Equals(item.FilePath, selectedSuitePath, StringComparison.OrdinalIgnoreCase));
            if (suite is not null)
            {
                SelectSuite(suite);
                return;
            }
        }

        var firstFlow = Snapshot.Catalog.NormalizedFlows.FirstOrDefault()?.SourceFile;
        if (!string.IsNullOrWhiteSpace(firstFlow))
        {
            SelectFlow(firstFlow);
            return;
        }

        NotifyChanged();
    }

    private void ReloadSuites(string? selectedSuitePath)
    {
        Suites.Clear();
        if (Snapshot is null)
        {
            return;
        }

        var suites = _suiteService.LoadAll(Snapshot.Catalog.ProjectRoot);
        AddDiagnostics(suites.Diagnostics);
        if (suites.Value is not null)
        {
            Suites.AddRange(suites.Value);
        }

        if (!string.IsNullOrWhiteSpace(selectedSuitePath))
        {
            var selected = Suites.FirstOrDefault(item => string.Equals(item.FilePath, selectedSuitePath, StringComparison.OrdinalIgnoreCase));
            SelectedSuite = selected is null ? null : StudioSuiteEditorModel.FromDocument(selected);
        }
    }

    private void PopulateArtifacts()
    {
        SelectedRunSteps.Clear();
        SelectedRunArtifacts.Clear();
        SelectedArtifact = null;
        PreviewText = string.Empty;
        PreviewImageDataUrl = string.Empty;

        if (SelectedRun is null)
        {
            return;
        }

        if (SelectedRunFlow is not null)
        {
            SelectedRunSteps.AddRange(SelectedRunFlow.Steps);
            SelectedRunStep ??= SelectedRunFlow.Steps.FirstOrDefault();
            var stepArtifacts = SelectedRunStep is null
                ? SelectedRunFlow.Steps.SelectMany(step => step.Artifacts)
                : SelectedRunStep.Artifacts;
            foreach (var artifact in stepArtifacts)
            {
                SelectedRunArtifacts.Add(new StudioArtifactItem(
                    $"{artifact.Category}: {Path.GetFileName(artifact.RelativePath)}",
                    $"{artifact.Description ?? artifact.RelativePath}{FormatArtifactSize(artifact.SizeBytes)}",
                    Path.Combine(SelectedRun.Result.Metadata.ArtifactRoot, artifact.RelativePath)));
            }
        }

        foreach (var report in SelectedRun.Result.Reports)
        {
            SelectedRunArtifacts.Add(new StudioArtifactItem(
                $"report: {report.Key}",
                report.Value,
                report.Value));
        }

        SelectedArtifact = SelectedRunArtifacts.FirstOrDefault();
        UpdatePreview();
    }

    public void SelectRunStep(StepRunResult? step)
    {
        SelectedRunStep = step;
        PopulateArtifacts();
        NotifyChanged();
    }

    private void UpdatePreview()
    {
        PreviewText = string.Empty;
        PreviewImageDataUrl = string.Empty;

        if (SelectedArtifact is null || !File.Exists(SelectedArtifact.Path))
        {
            return;
        }

        var extension = Path.GetExtension(SelectedArtifact.Path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
        {
            var mimeType = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
            PreviewImageDataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(SelectedArtifact.Path))}";
            return;
        }

        if (extension is ".json" or ".txt" or ".log" or ".md" or ".yaml" or ".yml" or ".html" or ".xml")
        {
            PreviewText = File.ReadAllText(SelectedArtifact.Path);
            return;
        }

        PreviewText = $"Preview is not available for {extension} files.{Environment.NewLine}{SelectedArtifact.Path}";
    }

    private void ClearDesignSelection()
    {
        SelectedFlow = null;
        SelectedSuite = null;
        FlowAnalysis = new();
    }

    private void AddDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        Diagnostics.AddRange(diagnostics);
    }

    private string ReadSource(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? File.ReadAllText(path)
            : string.Empty;

    private void NotifyChanged()
        => Changed?.Invoke();

    private string? ResolveProjectStartDirectory()
    {
        if (!string.IsNullOrWhiteSpace(ProjectPathInput))
        {
            return ProjectPathInput;
        }

        if (RecentWorkspaces.Count > 0)
        {
            return RecentWorkspaces[0];
        }

        if (!string.IsNullOrWhiteSpace(SuggestedWorkspacePath))
        {
            return SuggestedWorkspacePath;
        }

        return null;
    }

    private string? DetermineWorkspaceBrowserStartPath()
    {
        var candidate = ResolveProjectStartDirectory();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var parent = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : null;
    }

    private void RememberWorkspace(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        var normalized = Path.GetFullPath(projectRoot);
        RecentWorkspaces.RemoveAll(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase));
        RecentWorkspaces.Insert(0, normalized);
        if (RecentWorkspaces.Count > 8)
        {
            RecentWorkspaces.RemoveRange(8, RecentWorkspaces.Count - 8);
        }
    }

    private static IReadOnlyList<StudioDemoWorkspace> BuildDemoWorkspaces()
    {
        var demos = new List<StudioDemoWorkspace>();

        AddDemo(demos,
            id: "httpbin-smoke",
            relativeConfigPath: Path.Combine("specs", "httpbin-smoke", ".cress", "config.yaml"),
            title: "HTTP smoke demo",
            description: "Start with the built-in service smoke tests to validate the CLI, reports, and end-to-end project wiring.",
            tags: ["service", "smoke", "onboarding"]);

        AddDemo(demos,
            id: "web-smoke",
            relativeConfigPath: Path.Combine("specs", "web-smoke", ".cress", "config.yaml"),
            title: "Browser search-style demo",
            description: "Explore a browser workflow with login, search, and navigation steps authored in the same Studio surface used for Playwright-backed tests.",
            tags: ["web", "playwright", "search"]);

        AddDemo(demos,
            id: "calc-smoke",
            relativeConfigPath: Path.Combine("specs", "calc-smoke", ".cress", "config.yaml"),
            title: "Calculator desktop demo",
            description: "Load the Windows desktop sample to inspect Calculator-style attach/invoke flows and desktop evidence patterns.",
            tags: ["desktop", "calculator", "flaui"],
            preferredProfile: "local");

        return demos;
    }

    private static void AddDemo(
        ICollection<StudioDemoWorkspace> demos,
        string id,
        string relativeConfigPath,
        string title,
        string description,
        IReadOnlyList<string> tags,
        string? preferredProfile = null)
    {
        var configPath = FindRepositoryAsset(relativeConfigPath);
        var projectPath = configPath is null
            ? null
            : new FileInfo(configPath).Directory?.Parent?.FullName;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        demos.Add(new StudioDemoWorkspace(id, title, description, projectPath, tags, preferredProfile));
    }

    private static IReadOnlyList<StudioRunnerNode> BuildRunnerNodes()
    {
        var capabilities = new List<string> { "http", "playwright", "plugins" };
        if (OperatingSystem.IsWindows())
        {
            capabilities.Add("flaui");
        }

        return
        [
            new StudioRunnerNode(
                StudioRunnerNode.LocalNodeId,
                "Local embedded node",
                $"{Environment.MachineName} • in-process",
                "Runs flows on the same machine as Studio Web. This is the development node that future remote runners will mirror.",
                capabilities)
        ];
    }

    private static string BuildChildCountLabel(string directory)
    {
        try
        {
            var count = Directory.EnumerateDirectories(directory).Take(100).Count();
            return count == 0 ? "empty" : $"{count}+ folders";
        }
        catch
        {
            return "restricted";
        }
    }

    private static string? ResolveSuggestedWorkspacePath(ProjectLocator projectLocator)
    {
        var currentRoot = projectLocator.FindProjectRoot(Environment.CurrentDirectory);
        if (!string.IsNullOrWhiteSpace(currentRoot))
        {
            return currentRoot;
        }

        var knownConfig = FindRepositoryAsset(Path.Combine("specs", "httpbin-smoke", ".cress", "config.yaml"))
            ?? FindRepositoryAsset(Path.Combine("specs", "web-smoke", ".cress", "config.yaml"))
            ?? FindRepositoryAsset(Path.Combine("specs", "calc-smoke", ".cress", "config.yaml"));
        return knownConfig is null
            ? null
            : new FileInfo(knownConfig).Directory?.Parent?.FullName;
    }

    private static string? FindRepositoryAsset(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private int? ParseRetryOverride()
        => int.TryParse(RetryCountOverrideText, out var value) && value >= 0
            ? value
            : null;

    private void RefreshSelectedFlowAnalysis()
    {
        FlowAnalysis = SelectedFlow is null
            ? new FlowEditorAnalysis()
            : _authoringService.Analyze(Snapshot?.Catalog, SelectedFlow.ToDocument());
    }

    private void UpdateRunComparison()
    {
        if (SelectedRun is null)
        {
            SelectedRunComparison = new StudioRunComparison { Summary = "No run selected." };
            return;
        }

        var previous = Runs.SkipWhile(run => !ReferenceEquals(run, SelectedRun)).Skip(1).FirstOrDefault();
        SelectedRunComparison = _runInsightsService.Compare(SelectedRun, previous);
    }

    private static string FormatArtifactSize(long? sizeBytes)
        => sizeBytes is null
            ? string.Empty
            : $" • {sizeBytes.Value / 1024d:0.#} KB";

    private static IReadOnlyList<string> ParseCommaSeparated(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    private static string BuildSuiteSummary(StudioSuiteEditorModel suite)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Suite: {suite.Name}");
        builder.AppendLine($"Id: {suite.Id}");
        builder.AppendLine($"Profile: {suite.Profile ?? "inherit"}");
        builder.AppendLine($"Tag filter: {suite.Tag ?? "none"}");
        builder.AppendLine($"Flows: {(suite.FlowIds.Count == 0 ? "all matching flows" : string.Join(", ", suite.FlowIds.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)))}");
        builder.AppendLine($"Reports: {suite.ReportFormatsText}");
        if (!string.IsNullOrWhiteSpace(suite.Description))
        {
            builder.AppendLine();
            builder.AppendLine(suite.Description);
        }

        return builder.ToString().TrimEnd();
    }
}

public sealed record StudioArtifactItem(string DisplayName, string Detail, string Path);

public sealed record StudioDirectoryEntry(string Name, string Path, bool IsCressWorkspace, string? ItemCountLabel);

public sealed record StudioDemoWorkspace(
    string Id,
    string Title,
    string Description,
    string ProjectPath,
    IReadOnlyList<string> Tags,
    string? PreferredProfile = null);

public sealed record StudioRunnerNode(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Capabilities)
{
    public const string LocalNodeId = "local-embedded";
}

public sealed class StudioSuiteEditorModel
{
    public string FilePath { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Profile { get; set; }
    public string? Tag { get; set; }
    public HashSet<string> FlowIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string ReportFormatsText { get; set; } = "html, json, markdown";

    public static StudioSuiteEditorModel FromDocument(StudioSuiteDocument document)
    {
        var model = new StudioSuiteEditorModel
        {
            FilePath = document.FilePath ?? string.Empty,
            Id = document.Id,
            Name = document.Name,
            Description = document.Description,
            Profile = document.Profile,
            Tag = document.Tag,
            ReportFormatsText = string.Join(", ", document.ReportFormats)
        };

        foreach (var flowId in document.FlowIds)
        {
            model.FlowIds.Add(flowId);
        }

        return model;
    }

    public StudioSuiteDocument ToDocument()
        => new()
        {
            FilePath = FilePath,
            Id = Id,
            Name = Name,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            Profile = string.IsNullOrWhiteSpace(Profile) ? null : Profile,
            Tag = string.IsNullOrWhiteSpace(Tag) ? null : Tag,
            FlowIds = FlowIds.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            ReportFormats = string.IsNullOrWhiteSpace(ReportFormatsText)
                ? []
                : ReportFormatsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        };
}
