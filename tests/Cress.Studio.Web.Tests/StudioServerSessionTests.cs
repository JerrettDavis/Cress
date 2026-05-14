using System.Diagnostics;
using System.Net.Http;

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
    public void GetUsage_includes_browser_specific_options()
    {
        var usage = StudioLaunchOptions.GetUsage("cress-studio");

        Assert.Contains("--no-open-browser", usage, StringComparison.Ordinal);
        Assert.Contains("--web-root <path>", usage, StringComparison.Ordinal);
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
}
