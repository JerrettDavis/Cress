using Cress.Core.Models;

namespace Cress.Studio.E2ETests;

[Collection("Studio E2E")]
public sealed class StudioEndToEndTests
{
    [Fact]
    public async Task Studio_launches_loads_workspace_and_persists_flow_edits()
    {
        await using var studio = await StudioAppHarness.LaunchAsync(nameof(Studio_launches_loads_workspace_and_persists_flow_edits));

        Assert.Equal("Loaded Studio Sample Project.", studio.GetElementText("StatusMessageText"));
        studio.WaitForCondition(
            () => studio.GetTextBoxText("SelectedAssetSummaryTextBox").Contains("Desktop studio flow", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(10),
            "Studio did not auto-select the sample flow.");
        studio.SelectTab("DesignerTab");
        Assert.Equal("studio-desktop-flow", studio.GetTextBoxText("FlowIdTextBox"));
        studio.CaptureWindow("flow-selected");

        studio.SelectTab("SourceTab");
        var source = studio.GetTextBoxText("SourceEditorTextBox");
        var updatedSource = source.Replace("name: Desktop studio flow", "name: Desktop studio flow edited", StringComparison.Ordinal);
        studio.SetTextBoxText("SourceEditorTextBox", updatedSource);
        studio.CaptureWindow("source-edited");

        studio.ClickButton("ApplySourceButton");
        studio.WaitForCondition(
            () => studio.GetElementText("StatusMessageText").Contains("Source applied to designer.", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(10),
            "Studio did not report that source changes were applied.");
        studio.SelectTab("DesignerTab");
        studio.WaitForCondition(
            () => string.Equals(studio.GetTextBoxText("FlowNameTextBox"), "Desktop studio flow edited", StringComparison.Ordinal),
            TimeSpan.FromSeconds(20),
            "Designer did not reflect the edited flow name.");
        studio.CaptureWindow("designer-updated");

        studio.ClickButton("SaveFlowButton");
        studio.WaitForCondition(
            () => File.ReadAllText(studio.FlowFilePath).Contains("name: Desktop studio flow edited", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            "Flow source file was not saved to disk.");

        Assert.Contains("Desktop studio flow edited", File.ReadAllText(studio.FlowFilePath), StringComparison.Ordinal);
        Assert.Equal("Desktop studio flow edited", studio.GetTextBoxText("FlowNameTextBox"));
    }

    [Fact]
    public async Task Studio_runs_flow_and_surfaces_evidence_and_reports()
    {
        await using var studio = await StudioAppHarness.LaunchAsync(nameof(Studio_runs_flow_and_surfaces_evidence_and_reports));

        studio.ClickButton("RunSelectedButton");

        var (runDirectory, result) = studio.WaitForLatestRunResult();
        Assert.True(result.Passed, "Studio-triggered run did not pass.");
        Assert.Single(result.Flows);
        Assert.Contains("flaui", result.Flows[0].Drivers, StringComparer.OrdinalIgnoreCase);

        var screenshotDirectory = Path.Combine(runDirectory, "screenshots");
        studio.WaitForCondition(
            () => Directory.Exists(screenshotDirectory) && Directory.EnumerateFiles(screenshotDirectory, "*.png").Count() >= 2,
            TimeSpan.FromSeconds(10),
            "Run did not capture the expected screenshot artifacts.");
        studio.CaptureWindow("run-completed");

        var reportRoot = Path.Combine(studio.ReportsRoot, result.Metadata.RunId);
        Assert.True(File.Exists(Path.Combine(reportRoot, "report.html")));
        Assert.True(File.Exists(Path.Combine(reportRoot, "report.json")));
        Assert.True(File.Exists(Path.Combine(reportRoot, "junit.xml")));
        Assert.True(File.Exists(Path.Combine(reportRoot, "summary.md")));

        studio.SelectTab("RunsTab");
        studio.SelectFirstListItem("RunsList");
        studio.SelectFirstListItem("RunFlowsList");
        var artifactNames = studio.GetListItemNames("ArtifactsList");
        Assert.Contains(artifactNames, name => name.Contains("screenshots:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(artifactNames, name => name.Contains("report: json", StringComparison.OrdinalIgnoreCase));

        studio.SelectListItemContaining("ArtifactsList", "screenshots:");
        studio.WaitForCondition(
            () => studio.IsElementVisible("PreviewImage"),
            TimeSpan.FromSeconds(10),
            "Preview image did not appear for screenshot evidence.");
        studio.CaptureWindow("screenshot-preview");

        studio.SelectListItemContaining("ArtifactsList", "report: json");
        studio.WaitForPreviewText("\"runId\"");
        studio.CaptureWindow("report-preview");
    }
}
