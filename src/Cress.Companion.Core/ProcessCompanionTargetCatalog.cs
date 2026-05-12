using System.ComponentModel;
using System.Diagnostics;

namespace Cress.Companion;

public sealed class ProcessCompanionTargetCatalog : ICompanionTargetCatalog
{
    public Task<IReadOnlyList<CompanionTargetInfo>> ListTargetsAsync()
    {
        var targets = new List<CompanionTargetInfo>();

        foreach (var process in Process.GetProcesses())
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
                    mainModuleFileName = process.MainModule?.FileName;
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
                    ProcessId = process.Id,
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
}
