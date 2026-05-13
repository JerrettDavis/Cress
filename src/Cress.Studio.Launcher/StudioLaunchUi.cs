using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Cress.Studio.Launcher;

public static class StudioLaunchUi
{
    public static async Task<int> RunAsync(StudioLaunchOptions options, CancellationToken cancellationToken = default)
    {
        using var session = await StudioServerSession.StartAsync(options, cancellationToken).ConfigureAwait(true);
        using Form form = options.Mode == StudioLaunchMode.Desktop
            ? new StudioShellForm(session.BaseAddress)
            : new StudioBrowserForm(session.BaseAddress, options.LaunchBrowserClient);
        Application.Run(form);
        return 0;
    }

    private sealed class StudioBrowserForm : Form
    {
        private readonly Uri _baseAddress;
        private readonly bool _launchBrowserClient;
        private bool _opened;

        public StudioBrowserForm(Uri baseAddress, bool launchBrowserClient)
        {
            _baseAddress = baseAddress;
            _launchBrowserClient = launchBrowserClient;

            Text = "Cress Studio Browser Host";
            MinimumSize = new Size(560, 260);
            StartPosition = FormStartPosition.CenterScreen;

            var titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
                Text = "Cress Studio is running"
            };

            var subtitleLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                Text = "This window keeps the local Studio service alive while you work in your default browser."
            };

            var urlLabel = new TextBox
            {
                ReadOnly = true,
                Text = _baseAddress.ToString(),
                Dock = DockStyle.Top
            };

            var openButton = new Button
            {
                AutoSize = true,
                Text = "Open Studio"
            };
            openButton.Click += (_, _) => OpenInBrowser();

            var copyButton = new Button
            {
                AutoSize = true,
                Text = "Copy URL"
            };
            copyButton.Click += (_, _) => Clipboard.SetText(_baseAddress.ToString());

            var exitButton = new Button
            {
                AutoSize = true,
                Text = "Exit"
            };
            exitButton.Click += (_, _) => Close();

            var buttonFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight
            };
            buttonFlow.Controls.Add(openButton);
            buttonFlow.Controls.Add(copyButton);
            buttonFlow.Controls.Add(exitButton);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 4
            };
            content.RowStyles.Add(new RowStyle());
            content.RowStyles.Add(new RowStyle());
            content.RowStyles.Add(new RowStyle());
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(titleLabel, 0, 0);
            content.Controls.Add(subtitleLabel, 0, 1);
            content.Controls.Add(urlLabel, 0, 2);
            content.Controls.Add(buttonFlow, 0, 3);

            Controls.Add(content);
            if (_launchBrowserClient)
            {
                Shown += (_, _) => OpenInBrowser();
            }
        }

        private void OpenInBrowser()
        {
            if (_opened)
            {
                return;
            }

            _opened = true;
            Process.Start(new ProcessStartInfo
            {
                FileName = _baseAddress.ToString(),
                UseShellExecute = true
            });
        }
    }

    private sealed class StudioShellForm : Form
    {
        private readonly Uri _baseAddress;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly WebView2 _webView;

        public StudioShellForm(Uri baseAddress)
        {
            _baseAddress = baseAddress;

            Text = "Cress Studio";
            MinimumSize = new Size(1024, 720);
            StartPosition = FormStartPosition.CenterScreen;

            var toolStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Stretch = true
            };

            var openBrowserButton = new ToolStripButton("Open in browser");
            openBrowserButton.Click += (_, _) => OpenInBrowser();

            var copyUrlButton = new ToolStripButton("Copy URL");
            copyUrlButton.Click += (_, _) => Clipboard.SetText(_baseAddress.ToString());

            toolStrip.Items.Add(openBrowserButton);
            toolStrip.Items.Add(copyUrlButton);

            _webView = new WebView2
            {
                Dock = DockStyle.Fill
            };

            var statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel($"Connecting to {_baseAddress}");
            statusStrip.Items.Add(_statusLabel);

            Controls.Add(_webView);
            Controls.Add(toolStrip);
            Controls.Add(statusStrip);

            toolStrip.Dock = DockStyle.Top;
            statusStrip.Dock = DockStyle.Bottom;

            Shown += HandleShown;
        }

        private async void HandleShown(object? sender, EventArgs e)
        {
            try
            {
                await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _webView.Source = _baseAddress;
                _statusLabel.Text = _baseAddress.ToString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _statusLabel.Text = "WebView2 runtime not found. Opening Studio in your browser instead.";
                OpenInBrowser();
            }
        }

        private void OpenInBrowser()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _baseAddress.ToString(),
                UseShellExecute = true
            });
        }
    }
}
