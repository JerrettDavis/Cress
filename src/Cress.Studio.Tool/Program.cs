using System.Diagnostics;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("cress-studio currently supports Windows only.");
    return 1;
}

try
{
    var forwardedArgs = args;
    if (args.Any(argument => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)
        || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)
        || string.Equals(argument, "/?", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Out.WriteLine(
            """
            Usage:
              cress-studio [--desktop|--browser] [--port <port>] [--web-root <path>] [--no-open-browser]

            This tool launches the packaged Windows Studio host from the tool bundle.
            Any supplied arguments are forwarded to Cress.Studio.Windows.exe.
            """);
        return 0;
    }

    var studioExecutable = Path.Combine(AppContext.BaseDirectory, "studio", "Cress.Studio.Windows.exe");
    if (!File.Exists(studioExecutable))
    {
        Console.Error.WriteLine($"Unable to locate the bundled Studio host at '{studioExecutable}'.");
        return 1;
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = studioExecutable,
        WorkingDirectory = Path.GetDirectoryName(studioExecutable)!,
        UseShellExecute = false,
        Arguments = string.Join(" ", forwardedArgs.Select(QuoteArgument))
    };

    Process.Start(startInfo);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static string QuoteArgument(string argument)
    => argument.Contains(' ', StringComparison.Ordinal)
        ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
        : argument;
