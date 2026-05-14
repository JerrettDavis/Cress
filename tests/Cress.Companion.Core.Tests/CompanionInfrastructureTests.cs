using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Cress.Companion;
using Cress.Recorder;

namespace Cress.Companion.Core.Tests;

public sealed class CompanionInfrastructureTests
{
    [Fact]
    public void SystemCompanionClock_returns_recent_utc_timestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var clock = new SystemCompanionClock();

        var value = clock.UtcNow;

        Assert.InRange(value, before, DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void ScreenPreviewProvider_returns_null_for_non_positive_bounds()
    {
        var provider = new ScreenPreviewProvider();

        Assert.Null(provider.CapturePreview(new CompanionWindowBounds(0, 0, 0, 100)));
        Assert.Null(provider.CapturePreview(new CompanionWindowBounds(0, 0, 100, 0)));
    }

    [Fact]
    public void ScreenPreviewProvider_clamps_dimensions_and_returns_data_url()
    {
        (int Left, int Top, int Width, int Height) capture = default;
        var provider = new ScreenPreviewProvider((left, top, width, height) =>
        {
            capture = (left, top, width, height);
            return [1, 2, 3];
        });

        var preview = provider.CapturePreview(new CompanionWindowBounds(10, 20, 1200, 800));

        Assert.Equal((10, 20, 640, 360), capture);
        Assert.Equal("data:image/png;base64,AQID", preview);
    }

    [Fact]
    public void ScreenPreviewProvider_returns_null_when_internal_maximums_reduce_capture_to_zero()
    {
        var provider = new ScreenPreviewProvider((_, _, _, _) => throw new UnreachableException(), maxWidth: 0, maxHeight: 360);

        var preview = provider.CapturePreview(new CompanionWindowBounds(10, 20, 50, 50));

        Assert.Null(preview);
    }

    [Fact]
    public void ScreenPreviewProvider_capture_png_returns_png_bytes()
    {
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        var bytes = ScreenPreviewProvider.CapturePng(screen.Left, screen.Top, 1, 1);

        Assert.NotEmpty(bytes);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public void ProcessWindowInspector_returns_invisible_state_for_missing_process()
    {
        var inspector = new ProcessWindowInspector();

        var state = inspector.Inspect(int.MaxValue);

        Assert.False(state.IsVisible);
        Assert.Null(state.Bounds);
    }

    [Fact]
    public void ProcessWindowInspector_try_get_bounds_returns_null_for_invalid_handle()
    {
        Assert.Null(ProcessWindowInspector.TryGetBounds(IntPtr.Zero));
    }

    [Fact]
    public void ProcessWindowInspector_try_get_bounds_reads_desktop_window_rect()
    {
        var bounds = Assert.IsType<CompanionWindowBounds>(ProcessWindowInspector.TryGetBounds(GetDesktopWindow()));

        Assert.True(bounds.Width >= 0);
        Assert.True(bounds.Height >= 0);
    }

    [Fact]
    public void ProcessWindowInspector_handles_process_without_main_window()
    {
        var inspector = new ProcessWindowInspector();

        var state = inspector.Inspect(Process.GetCurrentProcess().Id);

        Assert.False(state.IsVisible);
        Assert.Null(state.Bounds);
    }

    [Fact]
    public void ProcessWindowInspector_returns_bounds_when_available()
    {
        var inspector = new ProcessWindowInspector(
            _ => new ProcessWindowSnapshot(new IntPtr(123), "Sample App"),
            _ => true,
            _ => new CompanionWindowBounds(10, 20, 300, 400));

        var state = inspector.Inspect(123);

        Assert.Equal("Sample App", state.WindowTitle);
        Assert.True(state.IsVisible);
        Assert.Equal(new CompanionWindowBounds(10, 20, 300, 400), state.Bounds);
    }

    [Fact]
    public void ProcessWindowInspector_returns_title_when_rect_is_unavailable()
    {
        var inspector = new ProcessWindowInspector(
            _ => new ProcessWindowSnapshot(new IntPtr(123), "Sample App"),
            _ => true,
            _ => null);

        var state = inspector.Inspect(123);

        Assert.Equal("Sample App", state.WindowTitle);
        Assert.True(state.IsVisible);
        Assert.Null(state.Bounds);
    }

    [Fact]
    public void ProcessWindowInspector_handles_snapshot_provider_failures()
    {
        var inspector = new ProcessWindowInspector(
            _ => throw new InvalidOperationException("boom"),
            _ => true,
            _ => new CompanionWindowBounds(0, 0, 1, 1));

        var state = inspector.Inspect(123);

        Assert.False(state.IsVisible);
        Assert.Null(state.Bounds);
    }

    [Fact]
    public void RecordingSessionBackendFactory_creates_backend_that_can_stop_and_dispose_without_start()
    {
        var factory = new RecordingSessionBackendFactory();

        using var backend = factory.Create(int.MaxValue);
        var events = backend.Stop();

        Assert.Empty(events);
    }

    [Fact]
    public void RecordingSessionBackend_forwards_captured_events()
    {
        var factory = new RecordingSessionBackendFactory();
        using var backend = factory.Create(int.MaxValue);
        RecordedEvent? forwarded = null;
        backend.EventCaptured += captured => forwarded = captured;
        var handler = backend.GetType().GetMethod("HandleEventCaptured", BindingFlags.Instance | BindingFlags.NonPublic);

        var recordedEvent = new RecordedEvent
        {
            Kind = EventKind.Invoke,
            Element = new ElementInfo { AutomationId = "submit-button" }
        };

        handler!.Invoke(backend, [recordedEvent]);

        Assert.Same(recordedEvent, forwarded);
    }

    [Fact]
    public void RecordingSessionBackend_start_throws_immediately_when_session_is_disposed()
    {
        var session = RecordingSession.FromProcessId(Environment.ProcessId);
        session.Dispose();
        using var backend = new RecordingSessionBackend(session);

        Assert.Throws<ObjectDisposedException>(() => backend.Start());
    }

    [Fact]
    public async Task ProcessCompanionTargetCatalog_lists_targets_without_throwing()
    {
        var catalog = new ProcessCompanionTargetCatalog();

        var targets = await catalog.ListTargetsAsync();

        Assert.NotNull(targets);
        Assert.True(targets.SequenceEqual(targets.OrderBy(target => target.ProcessName, StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ProcessCompanionTargetCatalog_sorts_targets_and_skips_non_windowed_entries()
    {
        var catalog = new ProcessCompanionTargetCatalog(() =>
        [
            new ProcessCatalogSnapshot(2, "zeta", new IntPtr(1), "Zeta Window", () => @"C:\zeta.exe"),
            new ProcessCatalogSnapshot(1, "alpha", IntPtr.Zero, "Hidden", () => @"C:\alpha.exe"),
            new ProcessCatalogSnapshot(3, "beta", new IntPtr(2), "Beta Window", () => @"C:\beta.exe"),
            new ProcessCatalogSnapshot(4, "gamma", new IntPtr(3), "", () => @"C:\gamma.exe")
        ]);

        var targets = await catalog.ListTargetsAsync();

        Assert.Collection(
            targets,
            target =>
            {
                Assert.Equal(3, target.ProcessId);
                Assert.Equal("beta", target.ProcessName);
                Assert.Equal(@"C:\beta.exe", target.MainModuleFileName);
                Assert.True(target.IsAttachable);
            },
            target =>
            {
                Assert.Equal(2, target.ProcessId);
                Assert.Equal("zeta", target.ProcessName);
                Assert.Equal(@"C:\zeta.exe", target.MainModuleFileName);
                Assert.True(target.IsAttachable);
            });
    }

    [Fact]
    public async Task ProcessCompanionTargetCatalog_marks_targets_unattachable_when_module_access_is_denied()
    {
        var catalog = new ProcessCompanionTargetCatalog(() =>
        [
            new ProcessCatalogSnapshot(
                5,
                "locked",
                new IntPtr(1),
                "Locked Window",
                () => throw new Win32Exception("access denied"))
        ]);

        var targets = await catalog.ListTargetsAsync();

        var target = Assert.Single(targets);
        Assert.Equal("locked", target.ProcessName);
        Assert.Null(target.MainModuleFileName);
        Assert.False(target.IsAttachable);
    }

    [Fact]
    public async Task ProcessCompanionTargetCatalog_skips_targets_when_module_lookup_reports_exit()
    {
        var catalog = new ProcessCompanionTargetCatalog(() =>
        [
            new ProcessCatalogSnapshot(
                5,
                "exited",
                new IntPtr(1),
                "Exited Window",
                () => throw new InvalidOperationException("exited"))
        ]);

        var targets = await catalog.ListTargetsAsync();

        Assert.Empty(targets);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
}
