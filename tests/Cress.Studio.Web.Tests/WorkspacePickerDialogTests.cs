using Bunit;
using Cress.Studio;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class WorkspacePickerDialogTests : TestContext
{
    private StudioWorkspaceState CreateState()
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    [Fact]
    public void WorkspacePicker_renders_current_location_and_workspace_badge()
    {
        var state = CreateState();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cress-studio-web-{Guid.NewGuid():N}");
        var workspaceFolder = Path.Combine(tempRoot, "sample-workspace");
        var cressFolder = Path.Combine(workspaceFolder, ".cress");

        Directory.CreateDirectory(cressFolder);
        File.WriteAllText(Path.Combine(cressFolder, "config.yaml"), "name: sample-workspace");

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();

            Assert.Contains("Browse for a workspace", cut.Markup);
            Assert.Contains(tempRoot, cut.Markup);
            Assert.Contains("sample-workspace", cut.Markup);
            Assert.Contains("Cress workspace", cut.Markup);
            Assert.Contains("Load this folder", cut.Markup);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void WorkspacePicker_filters_entries_and_can_clear_filter()
    {
        var state = CreateState();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cress-studio-web-{Guid.NewGuid():N}");
        var workspaceFolder = Path.Combine(tempRoot, "sample-workspace");
        var docsFolder = Path.Combine(tempRoot, "notes");
        var cressFolder = Path.Combine(workspaceFolder, ".cress");

        Directory.CreateDirectory(cressFolder);
        Directory.CreateDirectory(docsFolder);
        File.WriteAllText(Path.Combine(cressFolder, "config.yaml"), "name: sample-workspace");

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();

            cut.Find("[data-testid='workspace-picker-filter']").Input("sample");

            Assert.Contains("sample-workspace", cut.Markup);
            Assert.DoesNotContain("notes", cut.Markup);
            Assert.Contains("1 shown", cut.Markup);
            Assert.Contains("1 workspaces", cut.Markup);

            cut.Find("[aria-label='Clear workspace filter']").Click();

            Assert.Contains("sample-workspace", cut.Markup);
            Assert.Contains("notes", cut.Markup);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
