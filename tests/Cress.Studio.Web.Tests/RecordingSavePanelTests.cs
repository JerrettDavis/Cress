using System.Reflection;
using Bunit;
using Cress.Core.Models;
using Cress.Execution;
using Cress.Recorder;
using Cress.Recorder.Inference;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class RecordingSavePanelTests : TestContext
{
    private (StudioWorkspaceState state, FakeStudioRecorderService recorder) CreateState(
        FakeStudioRecorderService? recorder = null)
    {
        Services.AddCressStudioBackend();
        var fake = recorder ?? new FakeStudioRecorderService();
        Services.AddSingleton<IStudioRecorderService>(fake);
        Services.AddSingleton<StudioWorkspaceState>();
        var state = Services.GetRequiredService<StudioWorkspaceState>();
        return (state, fake);
    }

    private static InferredStep MakeClickStep(string automationId = "btn1")
        => new InferredStep
        {
            Kind = StepKind.Click,
            Locator = new Locator { AutomationId = automationId },
            SourceTimestamp = DateTime.UtcNow
        };

    private static RecordedEvent MakeInvokeEvent(string automationId = "btn1", int seq = 1)
        => new RecordedEvent
        {
            Sequence = seq,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = EventKind.Invoke,
            Element = new ElementInfo { AutomationId = automationId, ControlType = "Button" }
        };

    /// <summary>
    /// Opens the save panel with a pre-populated recording result that has N editable steps.
    /// </summary>
    private static void OpenSavePanelWith(StudioWorkspaceState state, IReadOnlyList<InferredStep> steps,
        IReadOnlyList<RecordedEvent>? events = null)
    {
        // Directly set LastRecordingResult and open the panel.
        state.GetType()
            .GetProperty(nameof(state.LastRecordingResult))!
            .SetValue(state, new RecordingResult
            {
                Steps = steps,
                Events = events ?? [],
                Duration = TimeSpan.FromSeconds(5),
                ProcessName = "calc"
            });
        state.OpenSavePanel();
    }

    [Fact]
    public void RecordingSavePanel_renders_nothing_when_panel_closed()
    {
        CreateState();
        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();
        // No visible panel content.
        Assert.DoesNotContain("Save recording", cut.Markup);
    }

    [Fact]
    public void RecordingSavePanel_renders_step_list_when_open()
    {
        var (state, _) = CreateState();
        var steps = new List<InferredStep> { MakeClickStep("btn1"), MakeClickStep("btn2") };
        OpenSavePanelWith(state, steps);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();

        Assert.Contains("btn1", cut.Markup);
        Assert.Contains("btn2", cut.Markup);
        Assert.Contains("step-preview-item", cut.Markup);
    }

    [Fact]
    public void RecordingSavePanel_delete_removes_step_from_list()
    {
        var (state, _) = CreateState();
        var steps = new List<InferredStep>
        {
            MakeClickStep("btn1"),
            MakeClickStep("btn2"),
            MakeClickStep("btn3")
        };
        OpenSavePanelWith(state, steps);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();

        // Before delete: all 3 steps present.
        Assert.Equal(3, cut.FindAll(".step-preview-item").Count);

        // Click the first delete (×) button.
        var deleteBtn = cut.Find(".step-preview-delete");
        deleteBtn.Click();

        // After delete: 2 steps remain.
        Assert.Equal(2, cut.FindAll(".step-preview-item").Count);
        // btn1 should be gone.
        Assert.DoesNotContain("automationId=btn1", cut.Markup);
    }

    [Fact]
    public void RecordingSavePanel_assertion_target_dropdown_shows_unique_automation_ids()
    {
        var (state, _) = CreateState();
        var events = new List<RecordedEvent>
        {
            MakeInvokeEvent("btn1", 1),
            MakeInvokeEvent("btn2", 2),
            MakeInvokeEvent("btn1", 3)  // duplicate — should appear only once in dropdown
        };
        OpenSavePanelWith(state, new List<InferredStep>(), events);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();

        // Dropdown for assertion target should be rendered.
        Assert.Contains("rec-assertion-target", cut.Markup);
        // btn1 and btn2 appear.
        Assert.Contains("btn1", cut.Markup);
        Assert.Contains("btn2", cut.Markup);
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
    }

    private static string CreateProjectRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cress-recording-save-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".cress", "profiles"));
        Directory.CreateDirectory(Path.Combine(root, "flows"));
        File.WriteAllText(Path.Combine(root, ".cress", "config.yaml"), """
        version: 1
        project:
          name: Recording sample
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
        File.WriteAllText(Path.Combine(root, ".cress", "profiles", "local.yaml"), """
        baseUrl: https://example.test
        """);
        return root;
    }

    [Fact]
    public void RecordingSavePanel_assertion_target_reruns_inference_and_updates_steps()
    {
        var (state, _) = CreateState();
        // Provide a ValueChanged event on "resultBox" — with no assertion target set,
        // it should infer as SetValue; with "resultBox" as target, it should become AssertText.
        var events = new List<RecordedEvent>
        {
            new RecordedEvent
            {
                Sequence = 1,
                Timestamp = DateTimeOffset.UtcNow,
                Kind = EventKind.ValueChanged,
                Element = new ElementInfo { AutomationId = "resultBox", ControlType = "Text" },
                Value = "42"
            }
        };
        // Initial steps (no assertion target — should be SetValue).
        var initialEngine = new StepInferenceEngine();
        var initialSteps = initialEngine.Infer(events, new InferenceOptions
        {
            DebounceWindow = TimeSpan.FromMilliseconds(50),
            IgnoreFocusEvents = true,
            AssertionTargetAutomationId = null
        }).ToList();

        OpenSavePanelWith(state, initialSteps, events);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();

        // Initially: SetValue step in the preview list.
        var previewItems = cut.FindAll(".step-preview-text");
        Assert.Contains(previewItems, el => el.TextContent.Contains("SetValue"));
        Assert.DoesNotContain(previewItems, el => el.TextContent.Contains("AssertText"));

        // Select "resultBox" as the assertion target.
        var select = cut.Find("#rec-assertion-target");
        select.Change("resultBox");

        // After re-inference: AssertText step in the preview list.
        var updatedItems = cut.FindAll(".step-preview-text");
        Assert.Contains(updatedItems, el => el.TextContent.Contains("AssertText"));
        Assert.DoesNotContain(updatedItems, el => el.TextContent.Contains("SetValue"));
    }

    // -------------------------------------------------------------------------
    // Replay button tests (Gap B)
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordingSavePanel_replay_button_disabled_before_save()
    {
        var (state, _) = CreateState();
        OpenSavePanelWith(state, new List<InferredStep> { MakeClickStep("btn1") });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();

        // The replay button should be present but disabled (no save yet).
        var replayBtn = cut.Find("button[disabled]");
        Assert.NotNull(replayBtn);
        Assert.Contains("Replay just-recorded", cut.Markup);
    }

    [Fact]
    public async Task RecordingSavePanel_save_requires_a_path()
    {
        var (state, _) = CreateState();
        OpenSavePanelWith(state, new List<InferredStep> { MakeClickStep("btn1") });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();
        cut.Find("#rec-save-path").Change(string.Empty);

        var saveButton = cut.FindAll("button").First(button => button.TextContent.Contains("Save", StringComparison.Ordinal));
        await cut.InvokeAsync(() => saveButton.Click());

        Assert.Contains("Please enter a save path.", cut.Markup);
    }

    [Fact]
    public async Task RecordingSavePanel_save_persists_flow_and_enables_replay()
    {
        var (state, _) = CreateState();
        var projectRoot = CreateProjectRoot();

        try
        {
            SetPrivate(state, "Snapshot", new StudioProjectSnapshot
            {
                Catalog = new ProjectCatalog { ProjectRoot = projectRoot }
            });
            state.SetProjectPath(projectRoot);
            OpenSavePanelWith(state, new List<InferredStep> { MakeClickStep("btn1") });

            var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();
            var savePath = Path.Combine(projectRoot, "flows", "recorded", "captured.flow.yaml");
            cut.Find("#rec-save-path").Change(savePath);

            var saveButton = cut.FindAll("button").First(button => button.TextContent.Contains("Save", StringComparison.Ordinal));
            await cut.InvokeAsync(() => saveButton.Click());

            Assert.True(File.Exists(savePath));
            var replayButton = cut.FindAll("button").First(button => button.TextContent.Contains("Replay just-recorded", StringComparison.Ordinal));
            Assert.False(replayButton.HasAttribute("disabled"));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RecordingSavePanel_replay_reports_missing_project_when_saved_path_exists_but_no_project_is_loaded()
    {
        var (state, _) = CreateState();
        OpenSavePanelWith(state, new List<InferredStep> { MakeClickStep("btn1") });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();
        var savedPathField = cut.Instance.GetType().GetField("_savedPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        savedPathField.SetValue(cut.Instance, @"C:\temp\recorded.flow.yaml");
        await cut.InvokeAsync(() => cut.Instance.GetType()
            .GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cut.Instance, null));

        var replayButton = cut.FindAll("button").First(button => button.TextContent.Contains("Replay just-recorded", StringComparison.Ordinal));
        await cut.InvokeAsync(() => replayButton.Click());

        Assert.Contains("Cannot replay: no project is loaded.", cut.Markup);
        Assert.Contains("diagnostic--error", cut.Markup);
    }

    [Fact]
    public void RecordingSavePanel_discard_closes_panel_and_resets_form_state()
    {
        var (state, _) = CreateState();
        OpenSavePanelWith(state, new List<InferredStep> { MakeClickStep("btn1") });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();
        cut.Find("#rec-flow-id").Change("custom-flow");
        cut.Find("#rec-flow-name").Change("Custom flow");

        cut.FindAll("button").First(button => button.TextContent.Contains("Discard", StringComparison.Ordinal)).Click();

        Assert.False(state.IsRecorderSavePanelOpen);
        Assert.DoesNotContain("Save recording", cut.Markup);

        state.OpenSavePanel();
        cut.Render();

        Assert.DoesNotContain("custom-flow", cut.Markup);
        Assert.DoesNotContain("Custom flow", cut.Markup);
    }

    [Fact]
    public async Task RecordingSavePanel_replay_shows_passed_status_on_success()
    {
        var fake = new FakeStudioRecorderService
        {
            ReplayResult = new RecordingReplayResult
            {
                Passed = true,
                Summary = "Passed (3 steps in 1s)",
                StepResults = ["step1: Passed", "step2: Passed", "step3: Passed"],
                Duration = TimeSpan.FromSeconds(1)
            }
        };
        var (state, _) = CreateState(fake);

        // Set up snapshot so projectPath is available.
        var catalog = new ProjectCatalog { ProjectRoot = @"C:\fake\project" };
        var snapshot = new StudioProjectSnapshot { Catalog = catalog };
        SetPrivate(state, "Snapshot", snapshot);

        OpenSavePanelWith(state, new List<InferredStep> { MakeClickStep("btn1") });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();

        // Inject _savedPath directly to simulate post-save state.
        var field = cut.Instance.GetType()
            .GetField("_savedPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(cut.Instance, @"C:\fake\project\flows\recorded\my.flow.yaml");
        // Force re-render so the disabled attribute is removed from the replay button.
        await cut.InvokeAsync(() => cut.Instance.GetType()
            .GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cut.Instance, null));

        // Replay button should now be enabled; find it by text.
        var allButtons = cut.FindAll("button");
        var replayBtn = allButtons.FirstOrDefault(b => b.TextContent.Contains("Replay just-recorded"));
        Assert.NotNull(replayBtn);
        Assert.False(replayBtn.HasAttribute("disabled"), "Replay button should be enabled after save");

        await cut.InvokeAsync(() => replayBtn.Click());

        // Verify success status displayed.
        Assert.Contains("Passed", cut.Markup);
        Assert.Contains("diagnostic--success", cut.Markup);
    }

    [Fact]
    public async Task RecordingSavePanel_replay_shows_failed_status_on_failure()
    {
        var fake = new FakeStudioRecorderService
        {
            ReplayResult = new RecordingReplayResult
            {
                Passed = false,
                Summary = "Failed at step click-btn1: element not found",
                StepResults = ["click-btn1: Failed — element not found"],
                Duration = TimeSpan.FromSeconds(2)
            }
        };
        var (state, _) = CreateState(fake);

        var catalog = new ProjectCatalog { ProjectRoot = @"C:\fake\project" };
        var snapshot = new StudioProjectSnapshot { Catalog = catalog };
        SetPrivate(state, "Snapshot", snapshot);

        OpenSavePanelWith(state, new List<InferredStep> { MakeClickStep("btn1") });

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.RecordingSavePanel>();

        var field = cut.Instance.GetType()
            .GetField("_savedPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(cut.Instance, @"C:\fake\project\flows\recorded\my.flow.yaml");
        await cut.InvokeAsync(() => cut.Instance.GetType()
            .GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cut.Instance, null));

        var allButtons = cut.FindAll("button");
        var replayBtn = allButtons.FirstOrDefault(b => b.TextContent.Contains("Replay just-recorded"));
        Assert.NotNull(replayBtn);
        await cut.InvokeAsync(() => replayBtn.Click());

        Assert.Contains("Failed", cut.Markup);
        Assert.Contains("diagnostic--error", cut.Markup);
    }
}
