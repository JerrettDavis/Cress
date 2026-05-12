namespace Cress.Companion.Windows;

internal sealed class CompanionOverlayForm : Form
{
    private readonly int _processId;
    private readonly DesktopCompanionCoordinator _coordinator;
    private readonly Action _showManager;
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
        BackColor = Color.FromArgb(28, 31, 43);
        ForeColor = Color.White;
        Width = 280;
        Height = 112;
        Padding = new Padding(12);

        _toggleButton.Text = "Companion";
        _toggleButton.FlatStyle = FlatStyle.Flat;
        _toggleButton.ForeColor = Color.White;
        _toggleButton.BackColor = Color.FromArgb(56, 74, 216);
        _toggleButton.FlatAppearance.BorderSize = 0;
        _toggleButton.Location = new Point(12, 12);
        _toggleButton.Size = new Size(104, 30);
        _toggleButton.Click += (_, _) => ToggleExpanded();
        Controls.Add(_toggleButton);

        _statusLabel.Location = new Point(126, 14);
        _statusLabel.Size = new Size(138, 24);
        _statusLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        Controls.Add(_statusLabel);

        _detailLabel.Location = new Point(12, 48);
        _detailLabel.Size = new Size(252, 28);
        Controls.Add(_detailLabel);

        _pauseResumeButton.Location = new Point(12, 78);
        _pauseResumeButton.Size = new Size(76, 24);
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
        _stopButton.Location = new Point(94, 78);
        _stopButton.Size = new Size(60, 24);
        _stopButton.Click += async (_, _) => await _coordinator.StopRecordingAsync(_processId);
        Controls.Add(_stopButton);

        _managerButton.Text = "Manager";
        _managerButton.Location = new Point(160, 78);
        _managerButton.Size = new Size(78, 24);
        _managerButton.Click += (_, _) => _showManager();
        Controls.Add(_managerButton);
    }

    public void UpdateData(CompanionSessionSnapshot session)
    {
        _statusLabel.Text = $"{session.ProcessName} • {session.Status}";
        _detailLabel.Text = _expanded
            ? $"{session.LastEventSummary} | {session.LastStepSummary}"
            : $"{session.CapturedEventCount} events";
        _pauseResumeButton.Text = session.Status == CompanionSessionStatus.Paused ? "Resume" : "Pause";

        if (session.WindowBounds is not null)
        {
            Width = _expanded ? 280 : 156;
            Height = _expanded ? 112 : 48;
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
        _detailLabel.Visible = _expanded;
        _pauseResumeButton.Visible = _expanded;
        _stopButton.Visible = _expanded;
        _managerButton.Visible = _expanded;
    }
}
