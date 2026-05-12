using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cress.Companion;

public sealed class ProcessWindowInspector : ICompanionWindowInspector
{
    public CompanionWindowState Inspect(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();

            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero)
            {
                return new CompanionWindowState
                {
                    WindowTitle = process.MainWindowTitle,
                    IsVisible = false
                };
            }

            var isVisible = IsWindowVisible(handle);
            if (!GetWindowRect(handle, out var rect))
            {
                return new CompanionWindowState
                {
                    WindowTitle = process.MainWindowTitle,
                    IsVisible = isVisible
                };
            }

            return new CompanionWindowState
            {
                WindowTitle = process.MainWindowTitle,
                IsVisible = isVisible,
                Bounds = new CompanionWindowBounds(
                    rect.Left,
                    rect.Top,
                    Math.Max(0, rect.Right - rect.Left),
                    Math.Max(0, rect.Bottom - rect.Top))
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
