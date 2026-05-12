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
    private readonly Label _targetsBadge = new();
    private readonly Label _sessionsBadge = new();
    private readonly Label _selectionTitleLabel = new();
    private readonly Label _selectionMetaLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _activityLabel = new();
    private readonly Label _previewHintLabel = new();
    private readonly PictureBox _previewBox = new();
    private readonly Button _startButton = new();
    private readonly Button _pauseButton = new();
    private readonly Button _resumeButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _overlayButton = new();
    private readonly Button _openStudioButton = new();
    private readonly Button _openBridgeButton = new();

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
        BackColor = CompanionUiTheme.WindowBackground;
        ForeColor = CompanionUiTheme.TextPrimary;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(24),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hero = BuildCardPanel();
        hero.Dock = DockStyle.Fill;
        hero.Padding = new Padding(24, 22, 24, 22);

        var title = new Label
        {
            Text = "Desktop companion",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0),
            ForeColor = CompanionUiTheme.TextPrimary
        };
        hero.Controls.Add(title);

        _statusLabel.Text = "The companion is ready to attach to desktop apps.";
        _statusLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Regular);
        _statusLabel.AutoSize = false;
        _statusLabel.Size = new Size(620, 48);
        _statusLabel.Location = new Point(0, 42);
        _statusLabel.ForeColor = CompanionUiTheme.TextSecondary;
        hero.Controls.Add(_statusLabel);

        _endpointLabel.Text = $"Bridge  {_baseAddress}api/companion";
        _endpointLabel.AutoSize = true;
        _endpointLabel.Location = new Point(0, 96);
        _endpointLabel.ForeColor = CompanionUiTheme.TextSecondary;
        hero.Controls.Add(_endpointLabel);

        ConfigurePill(_targetsBadge, new Point(690, 22), new Size(126, 28), CompanionVisualTone.Accent);
        _targetsBadge.Text = "0 windows";
        hero.Controls.Add(_targetsBadge);

        ConfigurePill(_sessionsBadge, new Point(824, 22), new Size(116, 28), CompanionVisualTone.Success);
        _sessionsBadge.Text = "0 sessions";
        hero.Controls.Add(_sessionsBadge);

        _openStudioButton.Text = "Open Studio";
        _openStudioButton.Size = new Size(126, 38);
        _openStudioButton.Location = new Point(690, 88);
        _openStudioButton.Click += (_, _) => OpenStudio();
        CompanionUiTheme.StyleButton(_openStudioButton, CompanionButtonStyle.Primary);
        hero.Controls.Add(_openStudioButton);

        _openBridgeButton.Text = "Open bridge";
        _openBridgeButton.Size = new Size(126, 38);
        _openBridgeButton.Location = new Point(824, 88);
        _openBridgeButton.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = _baseAddress.ToString(),
            UseShellExecute = true
        });
        CompanionUiTheme.StyleButton(_openBridgeButton, CompanionButtonStyle.Secondary);
        hero.Controls.Add(_openBridgeButton);

        root.Controls.Add(hero, 0, 0);
        root.SetColumnSpan(hero, 3);

        ConfigureListBox(_targetsList, "Attachable windows", DrawTargetItem);
        ConfigureListBox(_sessionsList, "Live sessions", DrawSessionItem);

        var targetsPanel = BuildListPanel(
            "Attachable windows",
            "Start only what you need. Each row stays concise so the important desktop windows are easy to spot.",
            _targetsList);
        var sessionsPanel = BuildListPanel(
            "Live sessions",
            "Pause, resume, or stop without hunting through verbose diagnostics.",
            _sessionsList);
        var detailPanel = BuildDetailPanel();

        root.Controls.Add(targetsPanel, 0, 1);
        root.Controls.Add(sessionsPanel, 1, 1);
        root.Controls.Add(detailPanel, 2, 1);

        Controls.Add(root);

        _targetsList.SelectedIndexChanged += (_, _) =>
        {
            if (_targetsList.Focused && _targetsList.SelectedItem is not null)
            {
                _sessionsList.ClearSelected();
            }

            RefreshDetails();
        };

        _sessionsList.SelectedIndexChanged += (_, _) =>
        {
            if (_sessionsList.Focused && _sessionsList.SelectedItem is not null)
            {
                _targetsList.ClearSelected();
            }

            RefreshDetails();
        };

        RefreshActionStates();
    }

    public int? SelectedSessionProcessId
        => _sessionsList.SelectedItem is CompanionSessionSnapshot session ? session.ProcessId : null;

    public void UpdateData(CompanionServiceSnapshot snapshot, IReadOnlyList<CompanionTargetInfo> targets)
    {
        var selectedSessionId = SelectedSessionProcessId;
        var selectedTargetId = _targetsList.SelectedItem is CompanionTargetInfo target ? target.ProcessId : (int?)null;
        var attachableTargets = targets.Where(targetInfo => targetInfo.IsAttachable).ToList();

        _statusLabel.Text = CompanionPresentation.BuildManagerStatus(snapshot, attachableTargets.Count);
        _targetsBadge.Text = $"{attachableTargets.Count} window(s)";
        _sessionsBadge.Text = $"{snapshot.Sessions.Count} session(s)";

        _targetsList.DataSource = attachableTargets;
        _sessionsList.DataSource = snapshot.Sessions.ToList();

        if (selectedTargetId.HasValue)
        {
            SelectByProcessId(_targetsList, selectedTargetId.Value);
        }
        else if (_targetsList.Items.Count > 0)
        {
            _targetsList.SelectedIndex = 0;
        }

        if (selectedSessionId.HasValue)
        {
            SelectByProcessId(_sessionsList, selectedSessionId.Value);
        }
        else if (_sessionsList.Items.Count > 0)
        {
            _sessionsList.SelectedIndex = 0;
        }

        RefreshDetails();
    }

    private Panel BuildDetailPanel()
    {
        var panel = BuildCardPanel();
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(24, 22, 24, 22);

        var heading = new Label
        {
            Text = "Focus view",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0),
            ForeColor = CompanionUiTheme.TextPrimary
        };
        panel.Controls.Add(heading);

        _selectionTitleLabel.Text = "Select a window or live session";
        _selectionTitleLabel.Font = new Font("Segoe UI", 17, FontStyle.Bold);
        _selectionTitleLabel.AutoSize = false;
        _selectionTitleLabel.Size = new Size(420, 34);
        _selectionTitleLabel.Location = new Point(0, 38);
        _selectionTitleLabel.ForeColor = CompanionUiTheme.TextPrimary;
        panel.Controls.Add(_selectionTitleLabel);

        _selectionMetaLabel.Text = "The manager keeps only the current context in focus.";
        _selectionMetaLabel.AutoSize = false;
        _selectionMetaLabel.Size = new Size(420, 24);
        _selectionMetaLabel.Location = new Point(0, 76);
        _selectionMetaLabel.ForeColor = CompanionUiTheme.TextSecondary;
        panel.Controls.Add(_selectionMetaLabel);

        _summaryLabel.Text = "Select an attachable window to start recording, or select a live session to manage it.";
        _summaryLabel.AutoSize = false;
        _summaryLabel.Size = new Size(420, 56);
        _summaryLabel.Location = new Point(0, 112);
        _summaryLabel.ForeColor = CompanionUiTheme.TextPrimary;
        panel.Controls.Add(_summaryLabel);

        _activityLabel.Text = "Session context, bridge access, and overlay guidance stay in this card so the surrounding layout remains quiet.";
        _activityLabel.AutoSize = false;
        _activityLabel.Size = new Size(420, 44);
        _activityLabel.Location = new Point(0, 176);
        _activityLabel.ForeColor = CompanionUiTheme.TextSecondary;
        panel.Controls.Add(_activityLabel);

        _previewBox.BorderStyle = BorderStyle.None;
        _previewBox.BackColor = CompanionUiTheme.SurfaceRaised;
        _previewBox.Location = new Point(0, 242);
        _previewBox.Size = new Size(420, 228);
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _previewBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_previewBox);

        _previewHintLabel.Text = "Preview becomes available as soon as the first frame is captured.";
        _previewHintLabel.AutoSize = false;
        _previewHintLabel.Size = new Size(420, 38);
        _previewHintLabel.Location = new Point(0, 482);
        _previewHintLabel.ForeColor = CompanionUiTheme.TextSecondary;
        panel.Controls.Add(_previewHintLabel);

        _startButton.Text = "Start session";
        _startButton.Location = new Point(0, 532);
        _startButton.Size = new Size(138, 38);
        CompanionUiTheme.StyleButton(_startButton, CompanionButtonStyle.Primary);
        _startButton.Click += async (_, _) =>
        {
            if (_targetsList.SelectedItem is CompanionTargetInfo selectedTarget)
            {
                await _coordinator.StartRecordingAsync(selectedTarget.ProcessId);
            }
        };
        panel.Controls.Add(_startButton);

        _pauseButton.Text = "Pause";
        _pauseButton.Location = new Point(148, 532);
        _pauseButton.Size = new Size(86, 38);
        CompanionUiTheme.StyleButton(_pauseButton, CompanionButtonStyle.Secondary);
        _pauseButton.Click += async (_, _) =>
        {
            if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
            {
                await _coordinator.PauseRecordingAsync(session.ProcessId);
            }
        };
        panel.Controls.Add(_pauseButton);

        _resumeButton.Text = "Resume";
        _resumeButton.Location = new Point(244, 532);
        _resumeButton.Size = new Size(90, 38);
        CompanionUiTheme.StyleButton(_resumeButton, CompanionButtonStyle.Secondary);
        _resumeButton.Click += async (_, _) =>
        {
            if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
            {
                await _coordinator.ResumeRecordingAsync(session.ProcessId);
            }
        };
        panel.Controls.Add(_resumeButton);

        _stopButton.Text = "Stop";
        _stopButton.Location = new Point(344, 532);
        _stopButton.Size = new Size(76, 38);
        CompanionUiTheme.StyleButton(_stopButton, CompanionButtonStyle.Danger);
        _stopButton.Click += async (_, _) =>
        {
            if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
            {
                await _coordinator.StopRecordingAsync(session.ProcessId);
            }
        };
        panel.Controls.Add(_stopButton);

        _overlayButton.Text = "Hide overlay";
        _overlayButton.Location = new Point(0, 580);
        _overlayButton.Size = new Size(138, 38);
        CompanionUiTheme.StyleButton(_overlayButton, CompanionButtonStyle.Ghost);
        _overlayButton.Click += async (_, _) =>
        {
            if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
            {
                await _coordinator.SetOverlayEnabledAsync(session.ProcessId, !session.OverlayEnabled);
            }
        };
        panel.Controls.Add(_overlayButton);

        return panel;
    }

    private Panel BuildListPanel(string title, string subtitle, ListBox listBox)
    {
        var panel = BuildCardPanel();
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(18, 18, 18, 18);

        var heading = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0),
            ForeColor = CompanionUiTheme.TextPrimary
        };
        panel.Controls.Add(heading);

        var hint = new Label
        {
            Text = subtitle,
            AutoSize = false,
            Size = new Size(280, 46),
            Location = new Point(0, 30),
            ForeColor = CompanionUiTheme.TextSecondary
        };
        panel.Controls.Add(hint);

        listBox.Location = new Point(0, 90);
        listBox.Size = new Size(300, 510);
        listBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(listBox);
        return panel;
    }

    private static void ConfigureListBox(ListBox listBox, string accessibleName, DrawItemEventHandler drawHandler)
    {
        CompanionUiTheme.StyleListBox(listBox, accessibleName);
        listBox.DrawItem += drawHandler;
    }

    private void RefreshDetails()
    {
        RefreshActionStates();

        if (_sessionsList.SelectedItem is CompanionSessionSnapshot session)
        {
            var presentation = CompanionPresentation.DescribeSessionSelection(session, _baseAddress);
            _selectionTitleLabel.Text = presentation.Title;
            _selectionMetaLabel.Text = presentation.Meta;
            _summaryLabel.Text = presentation.Summary;
            _activityLabel.Text = presentation.Activity;
            _previewHintLabel.Text = presentation.PreviewHint;
            CompanionUiTheme.ApplyTone(_sessionsBadge, presentation.Tone);
            _previewBox.Image = DecodePreview(session.PreviewImageDataUrl);
            return;
        }

        if (_targetsList.SelectedItem is CompanionTargetInfo target)
        {
            var presentation = CompanionPresentation.DescribeTargetSelection(target);
            _selectionTitleLabel.Text = presentation.Title;
            _selectionMetaLabel.Text = presentation.Meta;
            _summaryLabel.Text = presentation.Summary;
            _activityLabel.Text = presentation.Activity;
            _previewHintLabel.Text = presentation.PreviewHint;
            _previewBox.Image = null;
            return;
        }

        _selectionTitleLabel.Text = "Select a window or live session";
        _selectionMetaLabel.Text = "The companion keeps a single focal context at a time.";
        _summaryLabel.Text = "Choose an attachable window to start recording, or switch to a live session to control it.";
        _activityLabel.Text = "Status, preview, and actions stay in this panel so the rest of the manager remains calm and scannable.";
        _previewHintLabel.Text = "Preview becomes available automatically after the session starts.";
        _previewBox.Image = null;
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

    private static Panel BuildCardPanel()
    {
        var panel = new Panel();
        CompanionUiTheme.StyleCard(panel);
        return panel;
    }

    private static void ConfigurePill(Label label, Point location, Size size, CompanionVisualTone tone)
    {
        label.AutoSize = false;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        label.Location = location;
        label.Size = size;
        CompanionUiTheme.ApplyTone(label, tone);
    }

    private void DrawTargetItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _targetsList.Items.Count || _targetsList.Items[e.Index] is not CompanionTargetInfo target)
        {
            return;
        }

        CompanionUiTheme.DrawListItem(e, CompanionPresentation.DescribeTarget(target));
    }

    private void DrawSessionItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _sessionsList.Items.Count || _sessionsList.Items[e.Index] is not CompanionSessionSnapshot session)
        {
            return;
        }

        CompanionUiTheme.DrawListItem(e, CompanionPresentation.DescribeSession(session));
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
        catch (ArgumentException)
        {
            return null;
        }
        catch (FormatException)
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
