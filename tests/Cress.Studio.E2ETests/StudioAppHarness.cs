using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Cress.Core.Models;
using JerrettDavis.Flawright;
using JerrettDavis.Flawright.Locator;

namespace Cress.Studio.E2ETests;

internal sealed class StudioAppHarness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Process _process;
    private readonly IFlawright _flawright;
    private readonly IFlawrightPage _window;
    private int _screenshotSequence;

    private StudioAppHarness(
        string scenarioRoot,
        string workspaceRoot,
        string projectRoot,
        string screenshotsRoot,
        string studioPath,
        string testAppPath,
        string flowFilePath,
        Process process,
        IFlawright flawright,
        IFlawrightPage window)
    {
        ScenarioRoot = scenarioRoot;
        WorkspaceRoot = workspaceRoot;
        ProjectRoot = projectRoot;
        ScreenshotsRoot = screenshotsRoot;
        StudioPath = studioPath;
        TestAppPath = testAppPath;
        FlowFilePath = flowFilePath;
        _process = process;
        _flawright = flawright;
        _window = window;
    }

    public string ScenarioRoot { get; }
    public string WorkspaceRoot { get; }
    public string ProjectRoot { get; }
    public string ScreenshotsRoot { get; }
    public string StudioPath { get; }
    public string TestAppPath { get; }
    public string FlowFilePath { get; }
    public string RunArtifactsRoot => Path.Combine(ProjectRoot, "artifacts", "runs");
    public string ReportsRoot => Path.Combine(ProjectRoot, "reports");

    public static Task<StudioAppHarness> LaunchAsync(string scenarioName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Studio E2E tests require Windows.");
        }

        var repositoryRoot = GetRepositoryRoot();
        var scenarioRoot = Path.Combine(repositoryRoot, "artifacts", "studio-e2e", SanitizeName(scenarioName));
        RecreateDirectory(scenarioRoot);

        var workspaceRoot = Path.Combine(scenarioRoot, "workspace");
        var projectRoot = Path.Combine(workspaceRoot, "project");
        var screenshotsRoot = Path.Combine(scenarioRoot, "screenshots");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(screenshotsRoot);

        var fixtureRoot = Path.Combine(repositoryRoot, "tests", "Cress.Studio.E2ETests", "Fixtures", "StudioSampleProject");
        CopyDirectory(fixtureRoot, projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "models"));

        var studioPath = FindBuiltExecutable(repositoryRoot, Path.Combine("src", "Cress.Studio"), "Cress.Studio.exe");
        var testAppPath = FindBuiltExecutable(repositoryRoot, Path.Combine("tests", "Cress.Flawright.TestApp"), "Cress.Flawright.TestApp.exe");
        var flowFilePath = Path.Combine(projectRoot, "flows", "studio-desktop.flow.yaml");

        var profilePath = Path.Combine(projectRoot, ".cress", "profiles", "local.yaml");
        var profileText = File.ReadAllText(profilePath).Replace("__FLAUI_TEST_APP__", testAppPath, StringComparison.Ordinal);
        File.WriteAllText(profilePath, profileText);

        File.WriteAllText(
            Path.Combine(scenarioRoot, "scenario.json"),
            JsonSerializer.Serialize(new
            {
                studioPath,
                testAppPath,
                projectRoot,
                flowFilePath
            }, new JsonSerializerOptions { WriteIndented = true }));

        var startInfo = new ProcessStartInfo
        {
            FileName = studioPath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(studioPath)!
        };
        startInfo.Environment["CRESS_STUDIO_AUTO_SELECT_FIRST_FLOW"] = "1";
        startInfo.ArgumentList.Add(projectRoot);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Cress Studio.");
        process.WaitForInputIdle(15000);

        var flawright = Flawright.AttachAsync(
            new AttachOptions { ProcessId = process.Id },
            new FlawrightOptions { DefaultTimeout = TimeSpan.FromSeconds(20) }).GetAwaiter().GetResult();
        var window = flawright.Browser.WaitForPageAsync("Cress Studio", TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        window.BringToFrontAsync().GetAwaiter().GetResult();

        var harness = new StudioAppHarness(
            scenarioRoot,
            workspaceRoot,
            projectRoot,
            screenshotsRoot,
            studioPath,
            testAppPath,
            flowFilePath,
            process,
            flawright,
            window);

        harness.WaitForCondition(
            () => harness.GetElementText("StatusMessageText").Contains("Loaded Studio Sample Project.", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(20),
            "Studio did not finish loading the sample project.");
        harness.CaptureWindow("launch");
        harness.WriteWindowDump();

        return Task.FromResult(harness);
    }

    public void CaptureWindow(string name)
    {
        var path = Path.Combine(ScreenshotsRoot, $"{++_screenshotSequence:D3}-{SanitizeName(name)}.png");
        var bytes = _window.ScreenshotAsync(ct: CancellationToken.None).GetAwaiter().GetResult();
        File.WriteAllBytes(path, bytes);
    }

    public string GetElementText(string automationId)
        => ReadLocatorText($"#{automationId}");

    public string GetTextBoxText(string automationId)
        => ReadLocatorText($"#{automationId}");

    public void SetTextBoxText(string automationId, string value)
        => _window.FillAsync($"#{automationId}", value).GetAwaiter().GetResult();

    public void ClickButton(string automationId)
        => _window.ClickAsync($"#{automationId}").GetAwaiter().GetResult();

    public void SelectTab(string automationId)
    {
        var (tabControlSelector, headerText, contentSelector, index) = automationId switch
        {
            "OverviewTab" => ("#EditorTabControl", "Overview", "#SelectedAssetSummaryTextBox", 0),
            "DesignerTab" => ("#EditorTabControl", "Designer", "#FlowIdTextBox", 1),
            "SourceTab" => ("#EditorTabControl", "Source", "#SourceEditorTextBox", 2),
            "LiveRunTab" => ("#ResultsTabControl", "Live run", "#LiveEventsList", 0),
            "RunsTab" => ("#ResultsTabControl", "Runs", "#RunsList", 1),
            "DiagnosticsTab" => ("#ResultsTabControl", "Diagnostics", "#DiagnosticsList", 2),
            _ => (string.Empty, automationId.EndsWith("Tab", StringComparison.Ordinal) ? automationId[..^3] : automationId, $"#{automationId}", -1)
        };

        if (!string.IsNullOrWhiteSpace(tabControlSelector))
        {
            try
            {
                _window.Locator($"{tabControlSelector} >> role:TabItem >> name:{headerText}").First.ClickAsync().GetAwaiter().GetResult();
            }
            catch (FlawrightTimeoutException)
            {
                _window.BringToFrontAsync().GetAwaiter().GetResult();
                _window.FocusAsync(tabControlSelector).GetAwaiter().GetResult();
                SendKeys.SendWait("{HOME}");
                Thread.Sleep(150);
                for (var step = 0; step < index; step++)
                {
                    SendKeys.SendWait("{RIGHT}");
                    Thread.Sleep(150);
                }
            }
        }
        else
        {
            _window.ClickAsync($"#{automationId}").GetAwaiter().GetResult();
        }

        try
        {
            WaitForCondition(
                () => _window.Locator(contentSelector).IsVisibleAsync().GetAwaiter().GetResult(),
                TimeSpan.FromSeconds(4),
                $"Tab '{automationId}' did not expose '{contentSelector}'.");
        }
        catch (TimeoutException) when (!string.IsNullOrWhiteSpace(tabControlSelector) && index >= 0)
        {
            _window.BringToFrontAsync().GetAwaiter().GetResult();
            _window.FocusAsync(tabControlSelector).GetAwaiter().GetResult();
            SendKeys.SendWait("{HOME}");
            Thread.Sleep(150);
            for (var step = 0; step < index; step++)
            {
                SendKeys.SendWait("{RIGHT}");
                Thread.Sleep(150);
            }

            WaitForCondition(
                () => _window.Locator(contentSelector).IsVisibleAsync().GetAwaiter().GetResult(),
                TimeSpan.FromSeconds(8),
                $"Tab '{automationId}' did not expose '{contentSelector}'.");
        }
    }

    public void SelectExplorerItem(string groupPrefix, string itemContains)
    {
        try
        {
            _window.Locator("#ProjectExplorerTree").Locator($"text:{itemContains}").First.ClickAsync().GetAwaiter().GetResult();
            return;
        }
        catch
        {
        }

        _window.BringToFrontAsync().GetAwaiter().GetResult();
        _window.FocusAsync("#ProjectExplorerTree").GetAwaiter().GetResult();
        SendKeys.SendWait("{HOME}");
        Thread.Sleep(250);
        SendKeys.SendWait("{RIGHT}");
        Thread.Sleep(250);
        SendKeys.SendWait("{DOWN}");

        WaitForCondition(
            () => GetTextBoxText("SelectedAssetSummaryTextBox").Contains(itemContains, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(10),
            $"Keyboard navigation did not select explorer item '{itemContains}'.");
    }

    public string SelectFirstListItem(string automationId)
    {
        var locator = _window.Locator($"#{automationId}").Locator("role:Text").First;
        var text = locator.InnerTextAsync().GetAwaiter().GetResult();
        locator.ClickAsync().GetAwaiter().GetResult();
        return text;
    }

    public string SelectListItemContaining(string automationId, string text)
    {
        var locator = _window.Locator($"#{automationId}").Locator($"text:{text}").First;
        var match = locator.InnerTextAsync().GetAwaiter().GetResult();
        locator.ClickAsync().GetAwaiter().GetResult();
        return match;
    }

    public IReadOnlyList<string> GetListItemNames(string automationId)
        => _window.Locator($"#{automationId}").Locator("role:Text").AllInnerTextsAsync().GetAwaiter().GetResult()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool IsElementVisible(string automationId)
        => _window.Locator($"#{automationId}").IsVisibleAsync().GetAwaiter().GetResult();

    public void WaitForPreviewText(string expectedFragment)
    {
        WaitForCondition(
            () => GetTextBoxText("PreviewTextBox").Contains(expectedFragment, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(10),
            $"Preview text did not contain '{expectedFragment}'.");
    }

    public (string RunDirectory, RunResult Result) WaitForLatestRunResult()
    {
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until)
        {
            if (Directory.Exists(RunArtifactsRoot))
            {
                foreach (var runDirectory in Directory.EnumerateDirectories(RunArtifactsRoot).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var resultPath = Path.Combine(runDirectory, "result.json");
                    if (!File.Exists(resultPath))
                    {
                        continue;
                    }

                    try
                    {
                        var result = JsonSerializer.Deserialize<RunResult>(File.ReadAllText(resultPath), JsonOptions);
                        if (result is not null && result.Flows.Count > 0)
                        {
                            return (runDirectory, result);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException("Studio did not write a completed run result within 60 seconds.");
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    CaptureWindow("final");
                }
                catch
                {
                }

                _process.CloseMainWindow();
                if (!_process.WaitForExit(5000))
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
        }
        finally
        {
            _flawright.DisposeAsync().GetAwaiter().GetResult();
            _process.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public void WaitForCondition(Func<bool> predicate, TimeSpan timeout, string failureMessage)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (predicate())
            {
                return;
            }

            Thread.Sleep(150);
        }

        throw new TimeoutException(failureMessage);
    }

    private void WriteWindowDump()
    {
        var lines = _window.Locator("role:Text").AllInnerTextsAsync().GetAwaiter().GetResult()
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        File.WriteAllLines(Path.Combine(ScenarioRoot, "window-tree.txt"), lines);
    }

    private string ReadLocatorText(string selector)
    {
        var locator = _window.Locator(selector);
        var text = locator.TextContentAsync().GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        text = locator.InnerTextAsync().GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return locator.InputValueAsync().GetAwaiter().GetResult() ?? string.Empty;
    }

    private static string FindBuiltExecutable(string repositoryRoot, string projectRelativePath, string executableName)
    {
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(repositoryRoot, projectRelativePath, "bin", configuration, "net10.0-windows", executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate '{executableName}'. Make sure the project has been built.");
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cress.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root from the test assembly.");
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value
            .Select(character => invalid.Contains(character) ? '-' : character)
            .Select(character => char.IsWhiteSpace(character) ? '-' : char.ToLowerInvariant(character))
            .ToArray());
    }

    private static T TryGet<T>(Func<T> valueFactory, T fallback)
    {
        try
        {
            return valueFactory();
        }
        catch
        {
            return fallback;
        }
    }
}
