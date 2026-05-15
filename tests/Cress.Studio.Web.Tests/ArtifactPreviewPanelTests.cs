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

    [Fact]
    public void ArtifactPreviewPanel_shows_empty_state_when_filters_remove_all_artifacts()
    {
        var state = CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);

        state.SelectedRunArtifacts.Add(new StudioArtifactItem("report: html", @"C:\artifacts\report.html", @"C:\artifacts\report.html"));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();
        cut.Find("[data-testid='artifact-filter']").Input("missing");

        Assert.NotNull(cut.Find("[data-testid='artifact-filter-empty']"));
        Assert.Contains("0 shown", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactPreviewPanel_report_text_and_other_filters_show_matching_artifacts()
    {
        var state = CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);

        state.SelectedRunArtifacts.Add(new StudioArtifactItem("report.json", @"C:\artifacts\report.json", @"C:\artifacts\report.json"));
        state.SelectedRunArtifacts.Add(new StudioArtifactItem("run.log", @"C:\artifacts\run.log", @"C:\artifacts\run.log"));
        state.SelectedRunArtifacts.Add(new StudioArtifactItem("trace.bin", @"C:\artifacts\trace.bin", @"C:\artifacts\trace.bin"));

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        cut.Find("[data-testid='artifact-type-report']").Click();
        Assert.Contains("report.json", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("run.log", cut.Markup);
        Assert.Contains("Type: Reports", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid='artifact-type-text']").Click();
        Assert.Contains("run.log", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("trace.bin", cut.Markup);
        Assert.Contains("Type: Text", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid='artifact-type-other']").Click();
        Assert.Contains("trace.bin", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("report.json", cut.Markup);
        Assert.Contains("Type: Other", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactPreviewPanel_open_artifact_button_copies_selected_path()
    {
        var state = CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        var artifact = new StudioArtifactItem("report.json", @"C:\artifacts\report.json", @"C:\artifacts\report.json");
        state.SelectedRunArtifacts.Add(artifact);
        state.SelectArtifact(artifact);

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        Assert.Contains("selected", cut.Find(".stack-list .explorer-button").GetAttribute("class"));
        Assert.Null(cut.Find("[data-testid='artifact-open-button']").GetAttribute("disabled"));

        await cut.InvokeAsync(() => cut.Find("[data-testid='artifact-open-button']").Click());

        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "navigator.clipboard.writeText"
            && string.Equals(invocation.Arguments[0]?.ToString(), artifact.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void ArtifactPreviewPanel_shows_image_preview_when_available()
    {
        var state = CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        SetPrivate(state, nameof(StudioWorkspaceState.PreviewImageDataUrl), "data:image/png;base64,AQID");

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        var image = cut.Find("img.preview-image");
        Assert.Equal("data:image/png;base64,AQID", image.GetAttribute("src"));
    }

    [Fact]
    public void ArtifactPreviewPanel_shows_text_preview_when_available()
    {
        var state = CreateState();
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true);
        SetPrivate(state, nameof(StudioWorkspaceState.PreviewText), "preview text");

        var cut = RenderComponent<Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel>();

        Assert.Contains("preview text", cut.Find("pre.preview-text").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactPreviewPanel_private_helpers_cover_pdf_markdown_svg_and_default_types()
    {
        var getArtifactTypeLabel = typeof(Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel)
            .GetMethod("GetArtifactTypeLabel", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GetArtifactTypeLabel was not found.");
        var getArtifactKind = typeof(Cress.Studio.Web.Components.Studio.ArtifactPreviewPanel)
            .GetMethod("GetArtifactKind", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GetArtifactKind was not found.");

        Assert.Equal("pdf", Assert.IsType<string>(getArtifactTypeLabel.Invoke(null, ["report.pdf"])));
        Assert.Equal("doc", Assert.IsType<string>(getArtifactTypeLabel.Invoke(null, ["notes.md"])));
        Assert.Equal("img", Assert.IsType<string>(getArtifactTypeLabel.Invoke(null, ["diagram.svg"])));
        Assert.Equal("file", Assert.IsType<string>(getArtifactTypeLabel.Invoke(null, ["archive.bin"])));

        Assert.Equal("report", Assert.IsType<string>(getArtifactKind.Invoke(null, [new StudioArtifactItem("report", "pdf", @"C:\artifacts\report.pdf")])));
        Assert.Equal("text", Assert.IsType<string>(getArtifactKind.Invoke(null, [new StudioArtifactItem("notes", "log", @"C:\artifacts\run.log")])));
        Assert.Equal("image", Assert.IsType<string>(getArtifactKind.Invoke(null, [new StudioArtifactItem("diagram", "svg", @"C:\artifacts\diagram.svg")])));
        Assert.Equal("other", Assert.IsType<string>(getArtifactKind.Invoke(null, [new StudioArtifactItem("archive", "bin", @"C:\artifacts\archive.bin")])));
    }
}
