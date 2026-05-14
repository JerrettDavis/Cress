using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Cress.Studio.Launcher;

public static class StudioLaunchUi
{
    public static async Task<int> RunAsync(StudioLaunchOptions options, CancellationToken cancellationToken = default)
        => await RunAsync(
            options,
            static async (launchOptions, token) => await StudioServerSession.StartAsync(launchOptions, token).ConfigureAwait(true),
            Application.Run,
            cancellationToken).ConfigureAwait(true);

    internal static async Task<int> RunAsync(
        StudioLaunchOptions options,
        Func<StudioLaunchOptions, CancellationToken, Task<IStudioServerSession>> startSession,
        Action<Form> runApplication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(startSession);
        ArgumentNullException.ThrowIfNull(runApplication);

        using var session = await startSession(options, cancellationToken).ConfigureAwait(true);
        using Form form = CreateForm(options, session.BaseAddress);
        runApplication(form);
        return 0;
    }

    internal static Form CreateForm(StudioLaunchOptions options, Uri baseAddress)
        => options.Mode == StudioLaunchMode.Desktop
            ? new StudioShellForm(baseAddress)
            : new StudioBrowserForm(baseAddress, options.LaunchBrowserClient);

    internal static Font ResolveTitleFont(Font? messageBoxFont = null, Font? defaultFont = null)
        => new(messageBoxFont ?? defaultFont ?? Control.DefaultFont, FontStyle.Bold);

    internal static void LaunchExternalUri(Uri address, Func<ProcessStartInfo, Process?>? processStarter = null)
    {
        ArgumentNullException.ThrowIfNull(address);

        var startInfo = new ProcessStartInfo
        {
            FileName = address.ToString(),
            UseShellExecute = true
        };

        (processStarter ?? Process.Start)(startInfo);
    }

    internal sealed class StudioBrowserForm : Form
    {
        private readonly Uri _baseAddress;
        private readonly bool _launchBrowserClient;
        private readonly Action<Uri> _launchUri;
        private bool _opened;

        public StudioBrowserForm(Uri baseAddress, bool launchBrowserClient, Action<Uri>? launchUri = null)
        {
            _baseAddress = baseAddress;
            _launchBrowserClient = launchBrowserClient;
            _launchUri = launchUri ?? (uri => LaunchExternalUri(uri));

            Text = "Cress Studio Browser Host";
            MinimumSize = new Size(560, 260);
            StartPosition = FormStartPosition.CenterScreen;

            var titleLabel = new Label
            {
                AutoSize = true,
                Font = ResolveTitleFont(SystemFonts.MessageBoxFont, Control.DefaultFont),
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
            _launchUri(_baseAddress);
        }
    }

    internal sealed class StudioShellForm : Form
    {
        private readonly Uri _baseAddress;
        private readonly Action<Uri> _launchUri;
        private readonly Func<Task> _ensureWebViewReady;
        private readonly Action _configureWebView;
        private readonly Action<Uri> _navigateInShell;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly WebView2 _webView;

        public StudioShellForm(
            Uri baseAddress,
            Action<Uri>? launchUri = null,
            Func<Task>? ensureWebViewReady = null,
            Action? configureWebView = null,
            Action<Uri>? navigateInShell = null)
        {
            _baseAddress = baseAddress;
            _launchUri = launchUri ?? (uri => LaunchExternalUri(uri));

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
            _ensureWebViewReady = ensureWebViewReady ?? new Func<Task>(() => _webView.EnsureCoreWebView2Async());
            _configureWebView = configureWebView ?? (() => _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true);
            _navigateInShell = navigateInShell ?? (uri => _webView.Source = uri);

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

        internal async Task InitializeAsync()
        {
            try
            {
                await _ensureWebViewReady().ConfigureAwait(true);
                _configureWebView();
                _navigateInShell(_baseAddress);
                _statusLabel.Text = _baseAddress.ToString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _statusLabel.Text = "WebView2 runtime not found. Opening Studio in your browser instead.";
                OpenInBrowser();
            }
        }

        private async void HandleShown(object? sender, EventArgs e)
        {
            await InitializeAsync().ConfigureAwait(true);
        }

        private void OpenInBrowser()
        {
            _launchUri(_baseAddress);
        }
    }
}
