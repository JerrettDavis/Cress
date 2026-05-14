using System.ComponentModel;
using System.Diagnostics;

namespace Cress.Companion;

internal sealed record ProcessCatalogSnapshot(
    int ProcessId,
    string ProcessName,
    IntPtr MainWindowHandle,
    string MainWindowTitle,
    Func<string?> GetMainModuleFileName);

public sealed class ProcessCompanionTargetCatalog : ICompanionTargetCatalog
{
    private readonly Func<IEnumerable<ProcessCatalogSnapshot>> _enumerateProcesses;

    public ProcessCompanionTargetCatalog()
        : this(GetProcessSnapshots)
    {
    }

    internal ProcessCompanionTargetCatalog(Func<IEnumerable<ProcessCatalogSnapshot>> enumerateProcesses)
    {
        _enumerateProcesses = enumerateProcesses;
    }

    public Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync()
    {
        var targets = new List<CompanionTargetInfo>();

        foreach (var process in _enumerateProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    continue;
                }

                string? mainModuleFileName = null;
                var isAttachable = true;

                try
                {
                    mainModuleFileName = process.GetMainModuleFileName();
                }
                catch (Win32Exception)
                {
                    isAttachable = false;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                targets.Add(new CompanionTargetInfo
                {
                    ProcessId = process.ProcessId,
                    ProcessName = process.ProcessName,
                    WindowTitle = process.MainWindowTitle,
                    MainModuleFileName = mainModuleFileName,
                    IsAttachable = isAttachable
                });
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        return Task.FromResult<IReadOnlyList<CompanionTargetInfo>>(
            targets.OrderBy(target => target.ProcessName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IEnumerable<ProcessCatalogSnapshot> GetProcessSnapshots()
    {
        foreach (var process in Process.GetProcesses())
        {
            yield return new ProcessCatalogSnapshot(
                process.Id,
                process.ProcessName,
                process.MainWindowHandle,
                process.MainWindowTitle,
                () => process.MainModule?.FileName);
        }
    }
}
