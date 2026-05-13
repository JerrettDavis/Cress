namespace Cress.Studio.Launcher;

public sealed record StudioLaunchOptions(
    StudioLaunchMode Mode,
    string? WebRootPath,
    int? Port,
    bool LaunchBrowserClient)
{
    public static StudioLaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var mode = StudioLaunchMode.Desktop;
        string? webRootPath = null;
        int? port = null;
        var launchBrowserClient = true;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--browser":
                    mode = StudioLaunchMode.Browser;
                    break;
                case "--desktop":
                    mode = StudioLaunchMode.Desktop;
                    break;
                case "--mode" when index + 1 < args.Count:
                    mode = ParseMode(args[++index]);
                    break;
                case "--web-root" when index + 1 < args.Count:
                    webRootPath = args[++index];
                    break;
                case "--port" when index + 1 < args.Count:
                    if (!int.TryParse(args[++index], out var parsedPort) || parsedPort <= 0)
                    {
                        throw new ArgumentException("The --port value must be a positive integer.", nameof(args));
                    }

                    port = parsedPort;
                    break;
                case "--no-open-browser":
                    launchBrowserClient = false;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    throw new StudioUsageException();
                default:
                    throw new ArgumentException($"Unknown Studio launcher option '{argument}'.", nameof(args));
            }
        }

        return new StudioLaunchOptions(mode, webRootPath, port, launchBrowserClient);
    }

    public static string GetUsage(string commandName)
        => $"""
           Usage:
             {commandName} [--desktop|--browser] [--port <port>] [--web-root <path>] [--no-open-browser]

           Options:
             --desktop          Launch Studio in the embedded desktop shell.
             --browser          Launch Studio in the default browser with the local host window.
             --mode <mode>      Explicitly choose 'desktop' or 'browser'.
             --port <port>      Bind Studio to a specific loopback port.
             --web-root <path>  Override the packaged Studio web payload location.
             --no-open-browser  Keep the browser host window local without auto-opening a browser tab.
             --help             Show this help text.
           """;

    private static StudioLaunchMode ParseMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "desktop" => StudioLaunchMode.Desktop,
            "browser" => StudioLaunchMode.Browser,
            _ => throw new ArgumentException($"Unknown Studio launch mode '{value}'.", nameof(value))
        };
}

public sealed class StudioUsageException : Exception
{
    public StudioUsageException()
        : base("Studio launcher usage requested.")
    {
    }
}
