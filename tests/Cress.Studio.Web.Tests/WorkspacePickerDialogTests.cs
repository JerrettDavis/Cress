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

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cress-studio-web-picker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateProject(string root, string name)
    {
        var projectRoot = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(projectRoot, ".cress", "profiles"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "flows"));
        File.WriteAllText(Path.Combine(projectRoot, ".cress", "config.yaml"), """
        version: 1
        project:
          name: Picker sample
          defaultProfile: local
        paths:
          capabilities: capabilities
          flows: flows
          models: models
          fixtures: fixtures
          steps: steps
          artifacts: .cress/artifacts
          reports: reports
        """);
        File.WriteAllText(Path.Combine(projectRoot, ".cress", "profiles", "local.yaml"), """
        baseUrl: https://example.test
        """);
        return projectRoot;
    }

    private static string ToTestIdSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "empty";
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
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

    [Fact]
    public void WorkspacePicker_shows_empty_state_for_folder_without_children()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();

            Assert.Contains("No folders are available here. Move up a level or choose another root.", cut.Markup);
            Assert.NotNull(cut.Find("[data-testid='workspace-picker-empty']"));
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
    public void WorkspacePicker_shows_empty_state_when_filter_removes_all_matches()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        Directory.CreateDirectory(Path.Combine(tempRoot, "alpha"));

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();
            cut.Find("[data-testid='workspace-picker-filter']").Input("zzz");

            Assert.Contains("No folders match the current filter. Clear the search to see the full folder list.", cut.Markup);
            Assert.Contains("Filter: zzz", cut.Markup);
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
    public void WorkspacePicker_filter_matches_workspace_keyword_and_open_navigates_into_entry()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        var projectRoot = CreateProject(tempRoot, "picker-project");
        Directory.CreateDirectory(Path.Combine(projectRoot, "child-folder"));
        var openTestId = "workspace-picker-open-" + ToTestIdSegment(projectRoot);

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();
            cut.Find("[data-testid='workspace-picker-filter']").Input("workspace");

            Assert.Contains("picker-project", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("1 workspaces", cut.Markup, StringComparison.Ordinal);

            cut.Find($"[data-testid='{openTestId}']").Click();

            Assert.Contains(projectRoot, cut.Find("[data-testid='workspace-picker-location']").GetAttribute("value"));
            cut.Find("[aria-label='Clear workspace filter']").Click();
            Assert.Contains("child-folder", cut.Markup, StringComparison.Ordinal);
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
    public void WorkspacePicker_select_closes_dialog_and_updates_project_path_without_loading()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        var projectRoot = CreateProject(tempRoot, "picker-project");
        var testId = "workspace-picker-select-" + ToTestIdSegment(projectRoot);

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();
            cut.Find($"[data-testid='{testId}']").Click();

            Assert.False(state.IsWorkspacePickerOpen);
            Assert.Equal(projectRoot, state.ProjectPathInput);
            Assert.False(state.HasLoadedProject);
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
    public void WorkspacePicker_load_closes_dialog_and_loads_project_immediately()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        var projectRoot = CreateProject(tempRoot, "picker-project");
        var testId = "workspace-picker-load-" + ToTestIdSegment(projectRoot);

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();
            cut.Find($"[data-testid='{testId}']").Click();

            Assert.False(state.IsWorkspacePickerOpen);
            Assert.True(state.HasLoadedProject);
            Assert.Equal(projectRoot, state.ProjectPathInput);
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
    public void WorkspacePicker_backdrop_and_close_button_close_dialog_and_clear_filter()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        Directory.CreateDirectory(Path.Combine(tempRoot, "alpha"));

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();
            cut.Find("[data-testid='workspace-picker-filter']").Input("alpha");
            cut.Find("[data-testid='workspace-picker-backdrop']").Click();

            Assert.False(state.IsWorkspacePickerOpen);

            state.OpenWorkspacePicker();
            cut.Render();
            Assert.DoesNotContain("Filter: alpha", cut.Markup, StringComparison.Ordinal);

            cut.Find("[data-testid='workspace-picker-close']").Click();
            Assert.False(state.IsWorkspacePickerOpen);
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
    public void WorkspacePicker_renders_error_message_when_browser_error_is_present()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        Directory.CreateDirectory(Path.Combine(tempRoot, "alpha"));

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();
            SetPrivate(state, nameof(StudioWorkspaceState.WorkspaceBrowserError), "Access denied");

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();

            Assert.Contains("Access denied", cut.Markup, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("[data-testid='workspace-picker-error']"));
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
    public void WorkspacePicker_use_current_folder_selects_without_loading()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        var projectRoot = CreateProject(tempRoot, "picker-project");

        try
        {
            state.SetProjectPath(projectRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();
            cut.Find("[data-testid='workspace-picker-use-current']").Click();

            Assert.False(state.IsWorkspacePickerOpen);
            Assert.Equal(projectRoot, state.ProjectPathInput);
            Assert.False(state.HasLoadedProject);
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
    public void WorkspacePicker_up_navigates_to_parent_folder()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        var nestedRoot = Path.Combine(tempRoot, "nested");
        Directory.CreateDirectory(nestedRoot);
        CreateProject(nestedRoot, "picker-project");
        Directory.CreateDirectory(Path.Combine(tempRoot, "sibling-folder"));

        try
        {
            state.SetProjectPath(nestedRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();
            cut.Find("[data-testid='workspace-picker-up']").Click();

            Assert.Contains(tempRoot, cut.Find("[data-testid='workspace-picker-location']").GetAttribute("value"));
            Assert.Contains("nested", cut.Markup);
            Assert.Contains("sibling-folder", cut.Markup);
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
    public void WorkspacePicker_close_clears_filter_before_reopen()
    {
        var state = CreateState();
        var tempRoot = CreateTemporaryRoot();
        Directory.CreateDirectory(Path.Combine(tempRoot, "alpha"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "beta"));

        try
        {
            state.SetProjectPath(tempRoot);
            state.OpenWorkspacePicker();

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.WorkspacePickerDialog>();
            cut.Find("[data-testid='workspace-picker-filter']").Input("alpha");
            Assert.Contains("Filter: alpha", cut.Markup);

            cut.Find("[data-testid='workspace-picker-cancel']").Click();
            Assert.False(state.IsWorkspacePickerOpen);

            state.OpenWorkspacePicker();
            cut.Render();

            Assert.DoesNotContain("Filter: alpha", cut.Markup);
            Assert.Contains("alpha", cut.Markup);
            Assert.Contains("beta", cut.Markup);
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
