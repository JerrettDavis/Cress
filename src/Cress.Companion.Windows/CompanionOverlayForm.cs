namespace Cress.Companion.Windows;

internal sealed class CompanionOverlayForm : Form
{
    private readonly int _processId;
    private readonly DesktopCompanionCoordinator _coordinator;
    private readonly Action _showManager;
    private readonly Label _titleLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _detailLabel = new();
    private readonly Button _toggleButton = new();
    private readonly Button _pauseResumeButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _managerButton = new();
    private bool _expanded = true;

    public CompanionOverlayForm(int processId, DesktopCompanionCoordinator coordinator, Action showManager)
    {
        _processId = processId;
        _coordinator = coordinator;
        _showManager = showManager;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = CompanionUiTheme.Surface;
        ForeColor = CompanionUiTheme.TextPrimary;
        Width = 332;
        Height = 138;
        Padding = new Padding(14);
        Opacity = 0.98;
        Resize += (_, _) => CompanionUiTheme.ApplyRoundedRegion(this, 20);

        _titleLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        _titleLabel.AutoSize = false;
        _titleLabel.Location = new Point(16, 14);
        _titleLabel.Size = new Size(150, 22);
        _titleLabel.ForeColor = CompanionUiTheme.TextPrimary;
        Controls.Add(_titleLabel);

        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(170, 12);
        _statusLabel.Size = new Size(82, 24);
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        CompanionUiTheme.ApplyTone(_statusLabel, CompanionVisualTone.Success);
        Controls.Add(_statusLabel);

        _toggleButton.Text = "Compact";
        _toggleButton.Location = new Point(258, 10);
        _toggleButton.Size = new Size(60, 28);
        CompanionUiTheme.StyleButton(_toggleButton, CompanionButtonStyle.Ghost, compact: true);
        _toggleButton.Click += (_, _) => ToggleExpanded();
        Controls.Add(_toggleButton);

        _detailLabel.Location = new Point(16, 48);
        _detailLabel.Size = new Size(300, 42);
        _detailLabel.ForeColor = CompanionUiTheme.TextSecondary;
        Controls.Add(_detailLabel);

        _pauseResumeButton.Location = new Point(16, 98);
        _pauseResumeButton.Size = new Size(88, 30);
        CompanionUiTheme.StyleButton(_pauseResumeButton, CompanionButtonStyle.Primary, compact: true);
        _pauseResumeButton.Click += async (_, _) =>
        {
            var session = _coordinator.GetSnapshot().Sessions.FirstOrDefault(item => item.ProcessId == _processId);
            if (session is null)
            {
                return;
            }

            if (session.Status == CompanionSessionStatus.Paused)
            {
                await _coordinator.ResumeRecordingAsync(_processId);
            }
            else
            {
                await _coordinator.PauseRecordingAsync(_processId);
            }
        };
        Controls.Add(_pauseResumeButton);

        _stopButton.Text = "Stop";
        _stopButton.Location = new Point(112, 98);
        _stopButton.Size = new Size(64, 30);
        CompanionUiTheme.StyleButton(_stopButton, CompanionButtonStyle.Danger, compact: true);
        _stopButton.Click += async (_, _) => await _coordinator.StopRecordingAsync(_processId);
        Controls.Add(_stopButton);

        _managerButton.Text = "Open";
        _managerButton.Location = new Point(184, 98);
        _managerButton.Size = new Size(64, 30);
        CompanionUiTheme.StyleButton(_managerButton, CompanionButtonStyle.Secondary, compact: true);
        _managerButton.Click += (_, _) => _showManager();
        Controls.Add(_managerButton);

        CompanionUiTheme.ApplyRoundedRegion(this, 20);
    }

    public void UpdateData(CompanionSessionSnapshot session)
    {
        _titleLabel.Text = CompanionPresentation.BuildOverlayTitle(session);
        _statusLabel.Text = session.Status.ToString();
        CompanionUiTheme.ApplyTone(
            _statusLabel,
            session.Status == CompanionSessionStatus.Paused ? CompanionVisualTone.Warning : CompanionVisualTone.Success);
        _detailLabel.Text = CompanionPresentation.BuildOverlaySummary(session);
        _pauseResumeButton.Text = session.Status == CompanionSessionStatus.Paused ? "Resume" : "Pause";

        if (session.WindowBounds is not null)
        {
            Width = _expanded ? 332 : 214;
            Height = _expanded ? 138 : 52;
            Left = session.WindowBounds.Left + Math.Max(0, session.WindowBounds.Width - Width - 148);
            Top = session.WindowBounds.Top + 8;
        }

        if (!Visible)
        {
            Show();
        }
    }

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        _toggleButton.Text = _expanded ? "Compact" : "Expand";
        _detailLabel.Visible = _expanded;
        _pauseResumeButton.Visible = _expanded;
        _stopButton.Visible = _expanded;
        _managerButton.Visible = _expanded;
    }
}
