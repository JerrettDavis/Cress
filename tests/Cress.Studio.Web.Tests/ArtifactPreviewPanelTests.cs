using System.Reflection;
using Bunit;
using Cress.Studio;
using Cress.Studio.Services;
using Cress.Studio.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio.Web.Tests;

public sealed class ArtifactPreviewPanelTests : TestContext
{
    private StudioWorkspaceState CreateState()
    {
        Services.AddCressStudioBackend();
        Services.AddSingleton<StudioWorkspaceState>();
        return Services.GetRequiredService<StudioWorkspaceState>();
    }

    private static void SetPrivate<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().Name}");
        property.SetValue(target, value);
    }

    [Fact]
    public void ArtifactPreviewPanel_renders_empty_state_with_no_artifacts()
    {
        CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        Assert.Contains("Artifacts and reports", cut.Markup);
        Assert.Contains("Preview", cut.Markup);
        Assert.Contains("Select a report or evidence file", cut.Markup);
        Assert.Empty(cut.FindAll(".stack-list .explorer-button"));
    }

    [Fact]
    public void ArtifactPreviewPanel_renders_artifact_list_with_two_items()
    {
        var state = CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);

        state.SelectedRunArtifacts.Add(new StudioArtifactItem("screenshot: step-1.png", "On failure • 24.3 KB", @"C:\artifacts\step-1.png"));
        state.SelectedRunArtifacts.Add(new StudioArtifactItem("report: html", @"C:\artifacts\report.html", @"C:\artifacts\report.html"));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        var buttons = cut.FindAll(".stack-list .explorer-button");
        Assert.Equal(2, buttons.Count);
        Assert.Contains("screenshot: step-1.png", cut.Markup);
        Assert.Contains("report: html", cut.Markup);
    }

    [Fact]
    public void ArtifactPreviewPanel_type_filter_shows_only_matching_artifacts()
    {
        var state = CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);

        state.SelectedRunArtifacts.Add(new StudioArtifactItem("screenshot: step-1.png", "On failure • 24.3 KB", @"C:\artifacts\step-1.png"));
        state.SelectedRunArtifacts.Add(new StudioArtifactItem("report.json", @"C:\artifacts\report.json", @"C:\artifacts\report.json"));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        cut.Find("[data-testid='artifact-type-image']").Click();

        Assert.Contains("screenshot: step-1.png", cut.Markup);
        Assert.DoesNotContain("report.json", cut.Markup);
        Assert.Contains("1 shown", cut.Markup);
        Assert.Contains("1 images", cut.Markup);
    }

    [Fact]
    public void ArtifactPreviewPanel_search_clear_button_restores_artifact_list()
    {
        var state = CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);

        state.SelectedRunArtifacts.Add(new StudioArtifactItem("screenshot: step-1.png", "On failure • 24.3 KB", @"C:\artifacts\step-1.png"));
        state.SelectedRunArtifacts.Add(new StudioArtifactItem("report: html", @"C:\artifacts\report.html", @"C:\artifacts\report.html"));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        cut.Find("[data-testid='artifact-filter']").Input("report");

        Assert.DoesNotContain("screenshot: step-1.png", cut.Markup);
        Assert.Contains("report: html", cut.Markup);

        cut.Find("[aria-label='Clear artifact filter']").Click();

        Assert.Contains("screenshot: step-1.png", cut.Markup);
        Assert.Contains("report: html", cut.Markup);
    }

    [Fact]
    public void ArtifactPreviewPanel_open_artifact_button_is_disabled_when_no_selection()
    {
        CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        var openButton = cut.Find("[data-testid='artifact-open-button']");
        Assert.NotNull(openButton.GetAttribute("disabled"));
    }
}
