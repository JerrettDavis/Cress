using Cress.Gherkin.Phrases;

namespace Cress.Gherkin.Tests;

public sealed class PhraseLibraryTests
{
    private static readonly PhraseLibrary Library = PhraseLibrary.CreateDefault();

    [Fact]
    public void Resolve_KnownOp_ReturnsPhrase()
    {
        var phrase = Library.Resolve("http.get", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["url"] = "https://example.com" });
        Assert.NotNull(phrase);
        Assert.Equal(GherkinKeyword.When, phrase.Keyword);
    }

    [Fact]
    public void Resolve_UnknownOp_ReturnsNull()
    {
        var phrase = Library.Resolve("ui.no-such-step", new Dictionary<string, string>());
        Assert.Null(phrase);
    }

    [Fact]
    public void Resolve_UiClick_WithTestId_PrefersTestIdVariant()
    {
        var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["testId"] = "submit-btn"
        };
        var phrase = Library.Resolve("ui.click", with);
        Assert.NotNull(phrase);
        Assert.Contains("{testId}", phrase.Template);
    }

    [Fact]
    public void Resolve_UiClick_WithAutomationId_PicksBaseVariant()
    {
        var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["automationId"] = "BtnOk"
        };
        var phrase = Library.Resolve("ui.click", with);
        Assert.NotNull(phrase);
        Assert.Contains("{automationId}", phrase.Template);
        Assert.DoesNotContain("{testId}", phrase.Template);
    }

    [Fact]
    public void Resolve_UiClick_WithRoleAndLabel_PrefersRoleLabelVariant()
    {
        var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["role"] = "button",
            ["label"] = "Submit"
        };
        var phrase = Library.Resolve("ui.click", with);
        Assert.NotNull(phrase);
        Assert.Contains("{role}", phrase.Template);
        Assert.Contains("{label}", phrase.Template);
    }

    [Fact]
    public void Expand_ReplacesPlaceholders()
    {
        var phrase = new StepPhrase("http.get", GherkinKeyword.When, "I GET {url}");
        var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["url"] = "https://httpbin.org/get"
        };
        var text = PhraseLibrary.Expand(phrase, with);
        Assert.Equal("I GET https://httpbin.org/get", text);
    }

    [Fact]
    public void Expand_MissingPlaceholderKey_LeavesPlaceholderUnchanged()
    {
        var phrase = new StepPhrase("http.get", GherkinKeyword.When, "I GET {url}");
        var text = PhraseLibrary.Expand(phrase, new Dictionary<string, string>());
        Assert.Equal("I GET {url}", text);
    }

    [Fact]
    public void Resolve_HttpAssertStatus_ReturnsThenKeyword()
    {
        var phrase = Library.Resolve("http.assert-status", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["status"] = "200" });
        Assert.NotNull(phrase);
        Assert.Equal(GherkinKeyword.Then, phrase.Keyword);
    }

    [Fact]
    public void Resolve_UiAssertText_WithTestId_PrefersTestIdVariant()
    {
        var with = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["testId"] = "result-label",
            ["expected"] = "4"
        };
        var phrase = Library.Resolve("ui.assert-text", with);
        Assert.NotNull(phrase);
        Assert.Contains("{testId}", phrase.Template);
    }
}
