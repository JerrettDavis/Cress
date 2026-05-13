using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace Cress.Studio.Launcher;

public sealed class StudioServerSession : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(350);
    private static readonly HttpClient StartupClient = CreateStartupClient();

    private readonly Process _process;
    private readonly StringBuilder _output = new();
    private bool _disposed;

    private StudioServerSession(Process process, Uri baseAddress)
    {
        _process = process;
        BaseAddress = baseAddress;

        _process.OutputDataReceived += (_, eventArgs) => AppendOutput(eventArgs.Data);
        _process.ErrorDataReceived += (_, eventArgs) => AppendOutput(eventArgs.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public Uri BaseAddress { get; }

    public static async Task<StudioServerSession> StartAsync(StudioLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var webRoot = StudioBundleLocator.ResolveWebRoot(options.WebRootPath);
        var executablePath = Path.Combine(webRoot, StudioBundleLocator.StudioExecutableName);
        var port = options.Port ?? LoopbackPortAllocator.GetAvailablePort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = webRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.Environment["ASPNETCORE_URLS"] = baseAddress.ToString().TrimEnd('/');
        startInfo.Environment["CRESS_DISABLE_HTTPS_REDIRECTION"] = "1";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start Studio web payload at '{executablePath}'.");

        var session = new StudioServerSession(process, baseAddress);

        try
        {
            await session.WaitForStartupAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static HttpClient CreateStartupClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CressStudioLauncher", "1.0"));
        return client;
    }

    private void AppendOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }
    }

    private async Task WaitForStartupAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < StartupTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Studio exited before it became reachable.{Environment.NewLine}{GetCapturedOutput()}");
            }

            try
            {
                using var response = await StartupClient.GetAsync(BaseAddress, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode >= 200)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(StartupPollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Studio did not become reachable at {BaseAddress} within {StartupTimeout.TotalSeconds:F0} seconds.{Environment.NewLine}{GetCapturedOutput()}");
    }

    private string GetCapturedOutput()
    {
        lock (_output)
        {
            if (_output.Length == 0)
            {
                return "No Studio server output was captured.";
            }

            return "Captured Studio output:" + Environment.NewLine + _output.ToString().TrimEnd();
        }
    }
}
