using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Cress.Core.Models;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FlaUiApplication = FlaUI.Core.Application;

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
    private readonly FlaUiApplication _application;
    private readonly UIA3Automation _automation;
    private readonly Window _window;
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
        FlaUiApplication application,
        UIA3Automation automation,
        Window window)
    {
        ScenarioRoot = scenarioRoot;
        WorkspaceRoot = workspaceRoot;
        ProjectRoot = projectRoot;
        ScreenshotsRoot = screenshotsRoot;
        StudioPath = studioPath;
        TestAppPath = testAppPath;
        FlowFilePath = flowFilePath;
        _process = process;
        _application = application;
        _automation = automation;
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
        var testAppPath = FindBuiltExecutable(repositoryRoot, Path.Combine("tests", "Cress.FlaUi.TestApp"), "Cress.FlaUi.TestApp.exe");
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

        var application = FlaUiApplication.Attach(process.Id);
        var automation = new UIA3Automation();
        var window = WaitForWindow(application, automation, "Cress Studio", TimeSpan.FromSeconds(20));
        window.SetForeground();

        var harness = new StudioAppHarness(
            scenarioRoot,
            workspaceRoot,
            projectRoot,
            screenshotsRoot,
            studioPath,
            testAppPath,
            flowFilePath,
            process,
            application,
            automation,
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
        using var image = Capture.Element(_window);
        image.ToFile(path);
    }

    public string GetElementText(string automationId)
        => FindElementById(automationId).Name;

    public string GetTextBoxText(string automationId)
    {
        var textBox = FindElementById(automationId).AsTextBox();
        return textBox.Text;
    }

    public void SetTextBoxText(string automationId, string value)
    {
        var textBox = FindElementById(automationId).AsTextBox();
        if (textBox.Patterns.Value.IsSupported)
        {
            textBox.Patterns.Value.Pattern.SetValue(value);
        }
        else
        {
            textBox.Focus();
            textBox.Enter(value);
        }
    }

    public void ClickButton(string automationId)
    {
        var button = FindElementById(automationId).AsButton();
        if (button.Patterns.Invoke.IsSupported)
        {
            button.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        button.Click();
    }

    public void SelectTab(string automationId)
    {
        var tab = FindElementById(automationId).AsTabItem();
        if (tab.Patterns.SelectionItem.IsSupported)
        {
            tab.Patterns.SelectionItem.Pattern.Select();
        }
        else
        {
            tab.Focus();
        }

        WaitForCondition(
            () => !tab.Properties.IsOffscreen.ValueOrDefault,
            TimeSpan.FromSeconds(5),
            $"Tab '{automationId}' did not become visible.");
    }

    public void SelectExplorerItem(string groupPrefix, string itemContains)
    {
        var tree = FindElementById("ProjectExplorerTree");
        try
        {
            var groupLabel = WaitForDescendant(
                tree,
                element => element.ControlType == ControlType.Text && element.Name.StartsWith(groupPrefix, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5),
                $"Could not find explorer group '{groupPrefix}'.");
            var group = GetAncestor(groupLabel, ControlType.TreeItem);
            Expand(group);

            var itemLabel = WaitForDescendant(
                tree,
                element => element.ControlType == ControlType.Text && element.Name.Contains(itemContains, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5),
                $"Could not find explorer item containing '{itemContains}'.");
            var item = GetAncestor(itemLabel, ControlType.TreeItem);
            SelectItem(item);
            return;
        }
        catch
        {
        }

        _window.SetForeground();
        tree.Focus();
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
        var list = FindElementById(automationId);
        var label = WaitForDescendant(
            list,
            element => element.ControlType == ControlType.Text && !string.IsNullOrWhiteSpace(element.Name),
            TimeSpan.FromSeconds(10),
            $"List '{automationId}' did not contain any items.");
        var item = GetAncestor(label, ControlType.ListItem);
        SelectItem(item);
        return label.Name;
    }

    public string SelectListItemContaining(string automationId, string text)
    {
        var list = FindElementById(automationId);
        var label = WaitForDescendant(
            list,
            element => element.ControlType == ControlType.Text && element.Name.Contains(text, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(10),
            $"List '{automationId}' did not contain an item matching '{text}'.");
        var item = GetAncestor(label, ControlType.ListItem);
        SelectItem(item);
        return label.Name;
    }

    public IReadOnlyList<string> GetListItemNames(string automationId)
    {
        var list = FindElementById(automationId);
        return list.FindAllDescendants()
            .Where(element => element.ControlType == ControlType.Text)
            .Select(element => element.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsElementVisible(string automationId)
    {
        var element = FindElementById(automationId);
        var bounds = element.BoundingRectangle;
        return !element.Properties.IsOffscreen.ValueOrDefault && bounds.Width > 0 && bounds.Height > 0;
    }

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
            _automation.Dispose();
            _application.Dispose();
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
        var lines = _window.FindAllDescendants()
            .Select(element => $"{TryGet(() => element.ControlType.ToString(), "?")} | {TryGet(() => element.Name, string.Empty)} | {TryGet(() => element.Properties.AutomationId.ValueOrDefault, string.Empty)}")
            .ToList();
        File.WriteAllLines(Path.Combine(ScenarioRoot, "window-tree.txt"), lines);
    }

    private AutomationElement FindElementById(string automationId)
    {
        var conditions = _automation.ConditionFactory;
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            var match = _window.FindFirstDescendant(conditions.ByAutomationId(automationId));
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(150);
        }

        throw new TimeoutException($"Could not find element with automation id '{automationId}'.");
    }

    private static Window WaitForWindow(FlaUiApplication application, UIA3Automation automation, string title, TimeSpan timeout)
    {
        var conditions = automation.ConditionFactory;
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var window = application.GetAllTopLevelWindows(automation)
                .FirstOrDefault(candidate => string.Equals(candidate.Title, title, StringComparison.OrdinalIgnoreCase));
            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(150);
        }

        throw new TimeoutException($"Could not find Studio window '{title}'.");
    }

    private static void Expand(AutomationElement element)
    {
        if (element.Patterns.ExpandCollapse.IsSupported)
        {
            var state = element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value;
            if (state is ExpandCollapseState.Collapsed or ExpandCollapseState.PartiallyExpanded)
            {
                element.Patterns.ExpandCollapse.Pattern.Expand();
            }
        }
    }

    private static void SelectItem(AutomationElement element)
    {
        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        element.Focus();
    }

    private static AutomationElement GetAncestor(AutomationElement element, ControlType controlType)
    {
        var current = element.Parent;
        while (current is not null)
        {
            if (current.ControlType == controlType)
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not find ancestor of type '{controlType}'.");
    }

    private static AutomationElement WaitForDescendant(AutomationElement root, Func<AutomationElement, bool> predicate, TimeSpan timeout, string failureMessage)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var match = root.FindAllDescendants().FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(150);
        }

        throw new TimeoutException(failureMessage);
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
