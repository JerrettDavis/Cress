using System.Diagnostics;

namespace Cress.Companion.Windows;

internal sealed class CompanionApplicationContext : ApplicationContext
{
    private readonly DesktopCompanionCoordinator _coordinator;
    private readonly Uri _baseAddress;
    private readonly string _studioUrl;
    private readonly NotifyIcon _notifyIcon;
    private readonly CompanionManagerForm _managerForm;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly Dictionary<int, CompanionOverlayForm> _overlays = new();
    private IReadOnlyList<CompanionTargetInfo> _targets = [];
    private int _refreshTicks;

    public CompanionApplicationContext(DesktopCompanionCoordinator coordinator, Uri baseAddress, string studioUrl)
    {
        _coordinator = coordinator;
        _baseAddress = baseAddress;
        _studioUrl = studioUrl;

        _managerForm = new CompanionManagerForm(_coordinator, _baseAddress, _studioUrl);
        _managerForm.FormClosing += HandleManagerFormClosing;
        _managerForm.Show();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Cress Desktop Companion",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowManager();

        _uiTimer = new System.Windows.Forms.Timer
        {
            Interval = 600
        };
        _uiTimer.Tick += (_, _) => RefreshUi();
        _uiTimer.Start();

        RefreshUi();
    }

    protected override void ExitThreadCore()
    {
        _uiTimer.Stop();
        _uiTimer.Dispose();

        foreach (var overlay in _overlays.Values)
        {
            overlay.Close();
            overlay.Dispose();
        }

        _overlays.Clear();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _managerForm.Dispose();
        base.ExitThreadCore();
    }

    private void RefreshUi()
    {
        _refreshTicks++;
        if (_refreshTicks % 5 == 1)
        {
            _targets = _coordinator.ListTargetsAsync().GetAwaiter().GetResult();
        }

        var includePreview = _managerForm.SelectedSessionProcessId.HasValue;
        var snapshot = _coordinator.GetSnapshot(includePreview);
        _managerForm.UpdateData(snapshot, _targets);
        ReconcileOverlays(snapshot);

        var activeCount = snapshot.Sessions.Count(session => session.Status is CompanionSessionStatus.Recording or CompanionSessionStatus.Paused);
        _notifyIcon.Text = activeCount > 0
            ? $"Cress Desktop Companion ({activeCount} active)"
            : "Cress Desktop Companion";
    }

    private void ReconcileOverlays(CompanionServiceSnapshot snapshot)
    {
        var desired = snapshot.Sessions
            .Where(session => session.OverlayEnabled && session.Status is CompanionSessionStatus.Recording or CompanionSessionStatus.Paused && session.WindowBounds is not null && session.IsWindowVisible)
            .ToDictionary(session => session.ProcessId);

        foreach (var session in desired.Values)
        {
            if (!_overlays.TryGetValue(session.ProcessId, out var overlay))
            {
                overlay = new CompanionOverlayForm(
                    session.ProcessId,
                    _coordinator,
                    ShowManager);
                _overlays[session.ProcessId] = overlay;
                overlay.Show();
            }

            overlay.UpdateData(session);
        }

        foreach (var processId in _overlays.Keys.Except(desired.Keys).ToList())
        {
            var overlay = _overlays[processId];
            overlay.Close();
            overlay.Dispose();
            _overlays.Remove(processId);
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open manager", null, (_, _) => ShowManager());
        menu.Items.Add("Open Studio", null, (_, _) => OpenStudio());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private void HandleManagerFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _managerForm.Hide();
            _notifyIcon.ShowBalloonTip(
                1500,
                "Cress Desktop Companion",
                "The manager is still running in the tray.",
                ToolTipIcon.Info);
        }
    }

    private void ShowManager()
    {
        if (!_managerForm.Visible)
        {
            _managerForm.Show();
        }

        if (_managerForm.WindowState == FormWindowState.Minimized)
        {
            _managerForm.WindowState = FormWindowState.Normal;
        }

        _managerForm.BringToFront();
        _managerForm.Activate();
    }

    private void OpenStudio()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _studioUrl,
            UseShellExecute = true
        });
    }
}
