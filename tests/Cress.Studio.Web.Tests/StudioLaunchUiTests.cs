using System.Diagnostics;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Windows.Forms;
using Cress.Studio.Launcher;
using Microsoft.Web.WebView2.Core;

namespace Cress.Studio.Web.Tests;

public sealed class StudioLaunchUiTests
{
    [Fact]
    public async Task RunAsync_uses_browser_form_for_browser_mode()
    {
        Form? capturedForm = null;
        var session = new FakeStudioServerSession(new Uri("http://127.0.0.1:5130/"));

        var result = await StudioLaunchUi.RunAsync(
            new StudioLaunchOptions(StudioLaunchMode.Browser, "ignored", null, false),
            (_, _) => Task.FromResult<IStudioServerSession>(session),
            form => capturedForm = form);

        Assert.Equal(0, result);
        Assert.IsType<StudioLaunchUi.StudioBrowserForm>(capturedForm);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task RunAsync_uses_desktop_form_for_desktop_mode()
    {
        Form? capturedForm = null;
        var session = new FakeStudioServerSession(new Uri("http://127.0.0.1:5131/"));

        var result = await StudioLaunchUi.RunAsync(
            new StudioLaunchOptions(StudioLaunchMode.Desktop, "ignored", null, true),
            (_, _) => Task.FromResult<IStudioServerSession>(session),
            form => capturedForm = form);

        Assert.Equal(0, result);
        Assert.IsType<StudioLaunchUi.StudioShellForm>(capturedForm);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void BrowserForm_auto_open_launches_once_when_shown()
    {
        var launches = RunInSta(() =>
        {
            List<Uri> launchedUris = [];
            using var form = new StudioLaunchUi.StudioBrowserForm(
                new Uri("http://127.0.0.1:5123/"),
                launchBrowserClient: true,
                launchedUris.Add);

            RaiseShown(form);
            Click(FindButton(form, "Open Studio"));

            return launchedUris;
        });

        Assert.Collection(
            launches,
            uri => Assert.Equal(new Uri("http://127.0.0.1:5123/"), uri));
    }

    [Fact]
    public void BrowserForm_open_button_launches_when_auto_open_disabled()
    {
        var launches = RunInSta(() =>
        {
            List<Uri> launchedUris = [];
            using var form = new StudioLaunchUi.StudioBrowserForm(
                new Uri("http://127.0.0.1:5124/"),
                launchBrowserClient: false,
                launchedUris.Add);

            RaiseShown(form);
            Click(FindButton(form, "Open Studio"));

            return launchedUris;
        });

        Assert.Collection(
            launches,
            uri => Assert.Equal(new Uri("http://127.0.0.1:5124/"), uri));
    }

    [Fact]
    public void BrowserForm_initializes_expected_window_state_with_default_launcher()
    {
        RunInSta(() =>
        {
            using var form = new StudioLaunchUi.StudioBrowserForm(
                new Uri("http://127.0.0.1:5132/"),
                launchBrowserClient: false);

            Assert.Equal("Cress Studio Browser Host", form.Text);
            Assert.Equal(FormStartPosition.CenterScreen, form.StartPosition);
            Assert.Equal(new Size(560, 260), form.MinimumSize);

            var layout = Assert.IsType<TableLayoutPanel>(Assert.Single(form.Controls));
            var urlBox = Assert.IsType<TextBox>(layout.Controls.OfType<Control>().Single(control => control is TextBox));
            Assert.True(urlBox.ReadOnly);
            Assert.Equal("http://127.0.0.1:5132/", urlBox.Text);
            return true;
        });
    }

    [Fact]
    public void ShellForm_open_in_browser_button_uses_injected_launcher()
    {
        var launches = RunInSta(() =>
        {
            List<Uri> launchedUris = [];
            using var form = new StudioLaunchUi.StudioShellForm(
                new Uri("http://127.0.0.1:5125/"),
                launchedUris.Add);

            var toolStrip = Assert.IsType<ToolStrip>(form.Controls.OfType<ToolStrip>().Single(control => control.GetType() == typeof(ToolStrip)));
            var openBrowserButton = Assert.IsType<ToolStripButton>(toolStrip.Items[0]);
            Click(openBrowserButton);

            return launchedUris;
        });

        Assert.Collection(
            launches,
            uri => Assert.Equal(new Uri("http://127.0.0.1:5125/"), uri));
    }

    [Fact]
    public async Task ShellForm_initialize_async_navigates_embedded_webview_when_runtime_is_available()
    {
        var observed = await RunInSta(async () =>
        {
            var ensured = false;
            var configured = false;
            Uri? navigatedUri = null;
            using var form = new StudioLaunchUi.StudioShellForm(
                new Uri("http://127.0.0.1:5133/"),
                launchUri: _ => throw new InvalidOperationException("External browser launch should not be used."),
                ensureWebViewReady: () =>
                {
                    ensured = true;
                    return Task.CompletedTask;
                },
                configureWebView: () => configured = true,
                navigateInShell: uri => navigatedUri = uri);

            await form.InitializeAsync();

            return (ensured, configured, navigatedUri, Text: GetStatusLabel(form).Text);
        });

        Assert.True(observed.ensured);
        Assert.True(observed.configured);
        Assert.Equal(new Uri("http://127.0.0.1:5133/"), observed.navigatedUri);
        Assert.Equal("http://127.0.0.1:5133/", observed.Text);
    }

    [Fact]
    public async Task ShellForm_initialize_async_falls_back_to_browser_when_webview_runtime_is_missing()
    {
        var observed = await RunInSta(async () =>
        {
            List<Uri> launchedUris = [];
            using var form = new StudioLaunchUi.StudioShellForm(
                new Uri("http://127.0.0.1:5134/"),
                launchUri: launchedUris.Add,
                ensureWebViewReady: () => Task.FromException(CreateRuntimeNotFoundException()));

            await form.InitializeAsync();

            return (launches: launchedUris, Text: GetStatusLabel(form).Text);
        });

        Assert.Collection(
            observed.launches,
            uri => Assert.Equal(new Uri("http://127.0.0.1:5134/"), uri));
        Assert.Equal("WebView2 runtime not found. Opening Studio in your browser instead.", observed.Text);
    }

    [Fact]
    public void ShellForm_initializes_expected_window_state()
    {
        RunInSta(() =>
        {
            using var form = new StudioLaunchUi.StudioShellForm(new Uri("http://127.0.0.1:5126/"));

            Assert.Equal("Cress Studio", form.Text);
            Assert.Equal(FormStartPosition.CenterScreen, form.StartPosition);
            Assert.Equal(new Size(1024, 720), form.MinimumSize);
            Assert.Equal("Connecting to http://127.0.0.1:5126/", GetStatusLabel(form).Text);
            return true;
        });
    }

    [Fact]
    public void LaunchExternalUri_uses_shell_execute_start_info()
    {
        ProcessStartInfo? captured = null;

        StudioLaunchUi.LaunchExternalUri(
            new Uri("http://127.0.0.1:5135/"),
            startInfo =>
            {
                captured = startInfo;
                return null;
            });

        Assert.NotNull(captured);
        var startInfo = captured;
        Assert.Equal("http://127.0.0.1:5135/", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void ResolveTitleFont_prefers_message_box_font_and_falls_back_to_default()
    {
        using var preferredBase = new Font("Arial", 10);
        using var fallbackBase = new Font("Courier New", 11);
        using var preferred = StudioLaunchUi.ResolveTitleFont(preferredBase, fallbackBase);
        using var fallback = StudioLaunchUi.ResolveTitleFont(null, fallbackBase);

        Assert.Equal(preferredBase.FontFamily.Name, preferred.FontFamily.Name);
        Assert.Equal(FontStyle.Bold, preferred.Style);
        Assert.Equal(fallbackBase.FontFamily.Name, fallback.FontFamily.Name);
        Assert.Equal(FontStyle.Bold, fallback.Style);
    }

    private static Button FindButton(Form form, string text)
    {
        var layout = Assert.IsType<TableLayoutPanel>(Assert.Single(form.Controls));
        var buttonFlow = Assert.IsType<FlowLayoutPanel>(layout.Controls.OfType<Control>().Single(control => control is FlowLayoutPanel));
        return Assert.IsType<Button>(buttonFlow.Controls.OfType<Control>().Single(control => control.Text == text));
    }

    private static ToolStripStatusLabel GetStatusLabel(StudioLaunchUi.StudioShellForm form)
    {
        var statusStrip = Assert.IsType<StatusStrip>(form.Controls.OfType<Control>().Single(control => control is StatusStrip));
        return Assert.IsType<ToolStripStatusLabel>(Assert.Single(statusStrip.Items));
    }

    private static void Click(Button button)
    {
        var method = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find Button.OnClick.");
        method.Invoke(button, [EventArgs.Empty]);
    }

    private static void Click(ToolStripButton button)
    {
        var method = typeof(ToolStripItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find ToolStripItem.OnClick.");
        method.Invoke(button, [EventArgs.Empty]);
    }

    private static void RaiseShown(Form form)
    {
        var method = typeof(Form).GetMethod("OnShown", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find Form.OnShown.");
        method.Invoke(form, [EventArgs.Empty]);
    }

    private static T RunInSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? failure = null;
        using var completed = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completed.Wait();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    private static WebView2RuntimeNotFoundException CreateRuntimeNotFoundException()
    {
        var constructor = typeof(WebView2RuntimeNotFoundException)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .First();
        var arguments = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(string)
                ? "runtime-missing"
                : parameter.HasDefaultValue
                    ? parameter.DefaultValue
                    : parameter.ParameterType.IsValueType
                        ? Activator.CreateInstance(parameter.ParameterType)
                        : null)
            .ToArray();

        return (WebView2RuntimeNotFoundException)constructor.Invoke(arguments);
    }

    private sealed class FakeStudioServerSession(Uri baseAddress) : IStudioServerSession
    {
        public bool Disposed { get; private set; }

        public Uri BaseAddress { get; } = baseAddress;

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
