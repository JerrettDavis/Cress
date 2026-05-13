using Cress.Studio.Launcher;

namespace Cress.Studio.Windows;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = StudioLaunchOptions.Parse(args);
            ApplicationConfiguration.Initialize();
            return await StudioLaunchUi.RunAsync(options).ConfigureAwait(true);
        }
        catch (StudioUsageException)
        {
            MessageBox.Show(
                StudioLaunchOptions.GetUsage("Cress.Studio.Windows.exe"),
                "Cress Studio usage",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Unable to start Cress Studio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}
