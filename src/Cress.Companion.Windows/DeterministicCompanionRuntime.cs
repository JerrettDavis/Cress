using Cress.Companion;
using Cress.Recorder;

namespace Cress.Companion.Windows;

internal sealed class DeterministicCompanionTargetCatalog : ICompanionTargetCatalog
{
    public const int ProcessId = 42420;
    public const string ProcessName = "Companion Demo";
    public const string WindowTitle = "Companion demo target";

    private static readonly CompanionTargetInfo DemoTarget = new()
    {
        ProcessId = ProcessId,
        ProcessName = ProcessName,
        WindowTitle = WindowTitle,
        MainModuleFileName = "Cress.Companion.Windows.exe",
        IsAttachable = true
    };

    public Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync()
        => Task.FromResult<IReadOnlyList<CompanionTargetInfo>>([DemoTarget]);
}

internal sealed class DeterministicCompanionWindowInspector : ICompanionWindowInspector
{
    public CompanionWindowState Inspect(int processId)
        => processId == DeterministicCompanionTargetCatalog.ProcessId
            ? new CompanionWindowState
            {
                WindowTitle = DeterministicCompanionTargetCatalog.WindowTitle,
                IsVisible = true,
                Bounds = new CompanionWindowBounds(120, 120, 1280, 720)
            }
            : new CompanionWindowState();
}

internal sealed class DeterministicCompanionPreviewProvider : ICompanionPreviewProvider
{
    public string? CapturePreview(CompanionWindowBounds bounds)
        => null;
}

internal sealed class DeterministicCompanionSessionBackendFactory : ICompanionSessionBackendFactory
{
    public ICompanionSessionBackend Create(int processId)
        => new DeterministicCompanionSessionBackend();
}

internal sealed class DeterministicCompanionSessionBackend : ICompanionSessionBackend
{
#pragma warning disable CS0067
    public event Action<RecordedEvent>? EventCaptured;
#pragma warning restore CS0067

    public void Start()
    {
    }

    public IReadOnlyList<RecordedEvent> Stop()
        => [];

    public void Dispose()
    {
    }
}
