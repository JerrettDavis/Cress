using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

using Cress.Studio.Launcher;

namespace Cress.Studio.Web.Tests;

public sealed class StudioServerSessionTests
{
    [Fact]
    public async Task StartAsync_launches_packaged_web_payload_and_serves_requests()
    {
        using var host = TemporaryPublishedStudioHost.Create(
            """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/", () => Results.Text("studio-ok"));
            app.Run();
            """);

        using var session = await StudioServerSession.StartAsync(
            new StudioLaunchOptions(StudioLaunchMode.Browser, host.PublishDirectory, null, false));
        using var client = new HttpClient();

        var response = await client.GetStringAsync(session.BaseAddress);

        Assert.Equal("studio-ok", response);
    }

    [Fact]
    public async Task StartAsync_honors_requested_port()
    {
        using var host = TemporaryPublishedStudioHost.Create(
            """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/", () => Results.Text("studio-port"));
            app.Run();
            """);

        using var session = await StudioServerSession.StartAsync(
            new StudioLaunchOptions(StudioLaunchMode.Browser, host.PublishDirectory, 5129, false));
        using var client = new HttpClient();

        Assert.Equal(new Uri("http://127.0.0.1:5129/"), session.BaseAddress);
        Assert.Equal("studio-port", await client.GetStringAsync(session.BaseAddress));
    }

    [Fact]
    public async Task StartAsync_throws_when_packaged_payload_exits_before_startup()
    {
        using var host = TemporaryPublishedStudioHost.Create(
            """
            Console.WriteLine("launch-failed");
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StudioServerSession.StartAsync(
            new StudioLaunchOptions(StudioLaunchMode.Browser, host.PublishDirectory, 5126, false)));

        Assert.Contains("launch-failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_honors_cancellation_when_server_never_becomes_reachable()
    {
        using var host = TemporaryPublishedStudioHost.Create(
            """
            await Task.Delay(Timeout.InfiniteTimeSpan);
            """);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StudioServerSession.StartAsync(
            new StudioLaunchOptions(StudioLaunchMode.Browser, host.PublishDirectory, null, false),
            cancellationSource.Token));
    }

    [Fact]
    public async Task Dispose_is_idempotent_after_successful_start()
    {
        using var host = TemporaryPublishedStudioHost.Create(
            """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/", () => Results.Text("studio-dispose"));
            app.Run();
            """);

        var session = await StudioServerSession.StartAsync(
            new StudioLaunchOptions(StudioLaunchMode.Browser, host.PublishDirectory, null, false));

        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void GetCapturedOutput_returns_default_message_when_no_output_was_recorded()
    {
        var session = CreateUninitializedSession();

        var output = (string)InvokeNonPublic(session, "GetCapturedOutput")!;

        Assert.Equal("No Studio server output was captured.", output);
    }

    [Fact]
    public void Dispose_swallows_invalid_operation_for_unstarted_process()
    {
        var session = CreateUninitializedSession();

        session.Dispose();
    }

    [Fact]
    public void GetUsage_includes_browser_specific_options()
    {
        var usage = StudioLaunchOptions.GetUsage("cress-studio");

        Assert.Contains("--no-open-browser", usage, StringComparison.Ordinal);
        Assert.Contains("--web-root <path>", usage, StringComparison.Ordinal);
        Assert.Contains("cress-studio", usage, StringComparison.Ordinal);
    }

    private sealed class TemporaryPublishedStudioHost : IDisposable
    {
        private readonly string _rootPath;

        private TemporaryPublishedStudioHost(string rootPath, string publishDirectory)
        {
            _rootPath = rootPath;
            PublishDirectory = publishDirectory;
        }

        public string PublishDirectory { get; }

        public static TemporaryPublishedStudioHost Create(string programSource)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "cress-studio-server-tests", Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(rootPath, "src");
            var publishDirectory = Path.Combine(rootPath, "publish");
            Directory.CreateDirectory(projectDirectory);

            File.WriteAllText(Path.Combine(projectDirectory, "Cress.Studio.Web.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>Cress.Studio.Web</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), programSource);

            RunDotNet(projectDirectory, "publish", "Cress.Studio.Web.csproj", "-c", "Release", "-o", publishDirectory);
            return new TemporaryPublishedStudioHost(rootPath, publishDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }

        private static void RunDotNet(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"dotnet {string.Join(' ', arguments)} failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }

    private static StudioServerSession CreateUninitializedSession()
    {
        var session = (StudioServerSession)RuntimeHelpers.GetUninitializedObject(typeof(StudioServerSession));
        SetField(session, "_process", new Process());
        SetField(session, "_output", new StringBuilder());
        SetField(session, "<BaseAddress>k__BackingField", new Uri("http://127.0.0.1:5999/"));
        SetField(session, "_disposed", false);
        return session;
    }

    private static object? InvokeNonPublic(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find method '{methodName}'.");
        return method.Invoke(instance, arguments);
    }

    private static void SetField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find field '{fieldName}'.");
        field.SetValue(instance, value);
    }
}
