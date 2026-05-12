using System.Diagnostics;

namespace Cress.Companion.Windows;

internal sealed class CompanionManagerForm : Form
{
    private readonly DesktopCompanionCoordinator _coordinator;
    private readonly Uri _baseAddress;
    private readonly string _studioUrl;
    private readonly ListBox _targetsList = new();
    private readonly ListBox _sessionsList = new();
    private readonly Label _statusLabel = new();
    private readonly Label _endpointLabel = new();
    private readonly Label _detailLabel = new();
    private readonly PictureBox _previewBox = new();
    private readonly Button _startButton = new();
    private readonly Button _pauseButton = new();
    private readonly Button _resumeButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _overlayButton = new();

    public CompanionManagerForm(DesktopCompanionCoordinator coordinator, Uri baseAddress, string studioUrl)
    {
        _coordinator = coordinator;
        _baseAddress = baseAddress;
        _studioUrl = studioUrl;

        Text = "Cress Desktop Companion";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(16)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hero = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 122
        };

        var title = new Label
        {
            Text = "Desktop companion",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0)
        };
        hero.Controls.Add(title);

        _statusLabel.Text = "Ready to attach to desktop apps.";
        _statusLabel.Font = new Font("Segoe UI", 10, FontStyle.Regular);
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(4, 42);
        hero.Controls.Add(_statusLabel);

        _endpointLabel.Text = $"Bridge: {_baseAddress}api/companion";
        _endpointLabel.AutoSize = true;
        _endpointLabel.Location = new Point(4, 70);
        hero.Controls.Add(_endpointLabel);

        var openStudioButton = new Button
        {
            Text = "Open Studio Web",
            Width = 150,
            Height = 34,
            Location = new Point(0, 92)
        };
        openStudioButton.Click += (_, _) => OpenStudio();
        hero.Controls.Add(openStudioButton);

        root.Controls.Add(hero, 0, 0);
        root.SetColumnSpan(hero, 3);

        ConfigureListBox(_targetsList, "Available windows");
        ConfigureListBox(_sessionsList, "Active companion sessions");

        var targetsPanel = BuildPanel("Available windows", _targetsList);
        var sessionsPanel = BuildPanel("Active sessions", _sessionsList);
        var detailPanel = BuildDetailPanel();

        root.Controls.Add(targetsPanel, 0, 1);
        root.Controls.Add(sessionsPanel, 1, 1);
        root.Controls.Add(detailPanel, 2, 1);

        Controls.Add(root);

        _targetsList.Format += (_, e) =>
        {
            if (e.ListItem is CompanionTargetInfo target)
            {
                e.Value = $"{target.ProcessName}  •  {target.WindowTitle}  •  PID {target.ProcessId}";
            }
        };

        _sessionsList.Format += (_, e) =>
        {
            if (e.ListItem is CompanionSessionSnapshot session)
            {
                e.Value = $"{session.ProcessName}  •  {session.Status}  •  {session.CapturedEventCount} events  •  {session.LastStepSummary}";
            }
        };

        _sessionsList.SelectedIndexChanged += (_, _) => RefreshDetails();
        _targetsList.SelectedIndexChanged += (_, _) => RefreshActionStates();
        RefreshActionStates();
    }

    public int? SelectedSessionProcessId
        => _sessionsList.SelectedItem is CompanionSessionSnapshot session ? session.ProcessId : null;

    public void UpdateData(CompanionServiceSnapshot snapshot, IReadOnlyList<CompanionTargetInfo> targets)
    {
        var selectedSessionId = SelectedSessionProcessId;
        var selectedTargetId = _targetsList.SelectedItem is CompanionTargetInfo target ? target.ProcessId : (int?)null;

        _statusLabel.Text = snapshot.Sessions.Count == 0
            ? "Ready to anchor overlays and stream recordings into Studio Web."
            : $"{snapshot.Sessions.Count} session(s) tracked. Recording flows stay available from the tray, manager, and Studio bridge.";

        _targetsList.DataSource = targets.Where(targetInfo => targetInfo.IsAttachable).ToList();
        _sessionsList.DataSource = snapshot.Sessions.ToList();

        if (selectedTargetId.HasValue)
        {
            SelectByProcessId(_targetsList, selectedTargetId.Value);
        }

        if (selectedSessionId.HasValue)
        {
            SelectByProcessId(_sessionsList, selectedSessionId.Value);
        }

        RefreshDetails();
    }

    private Panel BuildDetailPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        var heading = new Label
        {
            Text = "Monitor and control",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0)
        };
        panel.Controls.Add(heading);

        _detailLabel.Text = "Select a running window to start a companion session, or select an active session to pause, resume, stop, or pin its overlay.";
        _detailLabel.AutoSize = false;
        _detailLabel.Width = 460;
        _detailLabel.Height = 120;
        _detailLabel.Location = new Point(0, 32);
        panel.Controls.Add(_detailLabel);

        _previewBox.BorderStyle = BorderStyle.FixedSingle;
        _previewBox.Location = new Point(0, 150);
        _previewBox.Size = new Size(460, 260);
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        panel.Controls.Add(_previewBox);

        _startButton.Text = "Start session";
        _startButton.Location = new Point(0, 430);
        _startButton.Size = new Size(130, 34);
        _startButton.Click += async (_, _) =>
        {
            if (_targetsList.SelectedItem is CompanionTargetInfo selectedTarget)
            {
                await _coordinator.StartRecordingAsync(selectedTarget.ProcessId);
            }
        };
        panel.Controls.Add(_startButton);

        _pauseButton.Text = "Pause";
        _pauseButton.Location = new Point(140, 430);
        _pauseButton.Size = new Size(90, 34);
        _pauseButton.Click += async (_, _) =>
        {
            if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
            {
                await _coordinator.PauseRecordingAsync(session.ProcessId);
            }
        };
        panel.Controls.Add(_pauseButton);

        _resumeButton.Text = "Resume";
        _resumeButton.Location = new Point(240, 430);
        _resumeButton.Size = new Size(90, 34);
        _resumeButton.Click += async (_, _) =>
        {
            if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
            {
                await _coordinator.ResumeRecordingAsync(session.ProcessId);
            }
        };
        panel.Controls.Add(_resumeButton);

        _stopButton.Text = "Stop";
        _stopButton.Location = new Point(340, 430);
        _stopButton.Size = new Size(90, 34);
        _stopButton.Click += async (_, _) =>
        {
            if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
            {
                await _coordinator.StopRecordingAsync(session.ProcessId);
            }
        };
        panel.Controls.Add(_stopButton);

        _overlayButton.Text = "Toggle overlay";
        _overlayButton.Location = new Point(0, 474);
        _overlayButton.Size = new Size(130, 34);
        _overlayButton.Click += async (_, _) =>
        {
            if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
            {
                await _coordinator.SetOverlayEnabledAsync(session.ProcessId, !session.OverlayEnabled);
            }
        };
        panel.Controls.Add(_overlayButton);

        var openStudioButton = new Button
        {
            Text = "Open Studio",
            Location = new Point(140, 474),
            Size = new Size(110, 34)
        };
        openStudioButton.Click += (_, _) => OpenStudio();
        panel.Controls.Add(openStudioButton);

        var openBridgeButton = new Button
        {
            Text = "Open bridge",
            Location = new Point(260, 474),
            Size = new Size(110, 34)
        };
        openBridgeButton.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = _baseAddress.ToString(),
            UseShellExecute = true
        });
        panel.Controls.Add(openBridgeButton);

        return panel;
    }

    private static Panel BuildPanel(string title, ListBox listBox)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var heading = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0)
        };
        panel.Controls.Add(heading);

        listBox.Location = new Point(0, 34);
        listBox.Size = new Size(300, 560);
        listBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(listBox);
        return panel;
    }

    private static void ConfigureListBox(ListBox listBox, string accessibleName)
    {
        listBox.AccessibleName = accessibleName;
        listBox.HorizontalScrollbar = true;
        listBox.BorderStyle = BorderStyle.FixedSingle;
        listBox.Font = new Font("Segoe UI", 10, FontStyle.Regular);
    }

    private void RefreshDetails()
    {
        RefreshActionStates();

        if (_sessionsList.SelectedItem is not CompanionSessionSnapshot session)
        {
            _detailLabel.Text = "Select an active session to inspect the latest step text, preview, and overlay state.";
            _previewBox.Image = null;
            return;
        }

        _detailLabel.Text =
            $"Window: {session.WindowTitle}{Environment.NewLine}" +
            $"Status: {session.Status}{Environment.NewLine}" +
            $"Events: {session.CapturedEventCount} ({session.SuppressedEventCount} suppressed while paused){Environment.NewLine}" +
            $"Latest event: {session.LastEventSummary}{Environment.NewLine}" +
            $"Latest step: {session.LastStepSummary}{Environment.NewLine}" +
            $"Overlay: {(session.OverlayEnabled ? "anchored near the titlebar" : "disabled")}{Environment.NewLine}" +
            $"Bridge path: {_baseAddress}api/companion";

        _previewBox.Image = DecodePreview(session.PreviewImageDataUrl);
    }

    private void RefreshActionStates()
    {
        var session = _sessionsList.SelectedItem as CompanionSessionSnapshot;
        _startButton.Enabled = _targetsList.SelectedItem is CompanionTargetInfo;
        _pauseButton.Enabled = session?.Status == CompanionSessionStatus.Recording;
        _resumeButton.Enabled = session?.Status == CompanionSessionStatus.Paused;
        _stopButton.Enabled = session is not null && session.Status is CompanionSessionStatus.Recording or CompanionSessionStatus.Paused;
        _overlayButton.Enabled = session is not null;
        _overlayButton.Text = session?.OverlayEnabled == true ? "Hide overlay" : "Show overlay";
    }

    private static void SelectByProcessId(ListBox listBox, int processId)
    {
        for (var index = 0; index < listBox.Items.Count; index++)
        {
            if (listBox.Items[index] is CompanionTargetInfo target && target.ProcessId == processId)
            {
                listBox.SelectedIndex = index;
                return;
            }

            if (listBox.Items[index] is CompanionSessionSnapshot session && session.ProcessId == processId)
            {
                listBox.SelectedIndex = index;
                return;
            }
        }
    }

    private static Image? DecodePreview(string? previewImageDataUrl)
    {
        if (string.IsNullOrWhiteSpace(previewImageDataUrl))
        {
            return null;
        }

        const string prefix = "data:image/png;base64,";
        if (!previewImageDataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var data = Convert.FromBase64String(previewImageDataUrl[prefix.Length..]);
            using var stream = new MemoryStream(data);
            return Image.FromStream(stream);
        }
        catch
        {
            return null;
        }
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
