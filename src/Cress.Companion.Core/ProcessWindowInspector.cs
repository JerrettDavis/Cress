using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cress.Companion;

internal readonly record struct ProcessWindowSnapshot(IntPtr Handle, string WindowTitle);
internal delegate ProcessWindowSnapshot ProcessWindowSnapshotProvider(int processId);
internal delegate bool WindowVisibilityProvider(IntPtr handle);
internal delegate CompanionWindowBounds? WindowBoundsProvider(IntPtr handle);

public sealed class ProcessWindowInspector : ICompanionWindowInspector
{
    private readonly ProcessWindowSnapshotProvider _snapshotProvider;
    private readonly WindowVisibilityProvider _isWindowVisible;
    private readonly WindowBoundsProvider _getBounds;

    public ProcessWindowInspector()
        : this(GetSnapshot, IsWindowVisible, TryGetBounds)
    {
    }

    internal ProcessWindowInspector(
        ProcessWindowSnapshotProvider snapshotProvider,
        WindowVisibilityProvider isWindowVisible,
        WindowBoundsProvider getBounds)
    {
        _snapshotProvider = snapshotProvider;
        _isWindowVisible = isWindowVisible;
        _getBounds = getBounds;
    }

    public CompanionWindowState Inspect(int processId)
    {
        try
        {
            var snapshot = _snapshotProvider(processId);
            var handle = snapshot.Handle;
            if (handle == IntPtr.Zero)
            {
                return new CompanionWindowState
                {
                    WindowTitle = snapshot.WindowTitle,
                    IsVisible = false
                };
            }

            var isVisible = _isWindowVisible(handle);
            var bounds = _getBounds(handle);
            if (bounds is null)
            {
                return new CompanionWindowState
                {
                    WindowTitle = snapshot.WindowTitle,
                    IsVisible = isVisible
                };
            }

            return new CompanionWindowState
            {
                WindowTitle = snapshot.WindowTitle,
                IsVisible = isVisible,
                Bounds = bounds
            };
        }
        catch (Exception)
        {
            return new CompanionWindowState
            {
                IsVisible = false
            };
        }
    }

    private static ProcessWindowSnapshot GetSnapshot(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Refresh();
        return new ProcessWindowSnapshot(process.MainWindowHandle, process.MainWindowTitle);
    }

    internal static CompanionWindowBounds? TryGetBounds(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect))
        {
            return null;
        }

        return new CompanionWindowBounds(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
