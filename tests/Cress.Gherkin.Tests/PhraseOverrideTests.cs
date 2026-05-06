using Cress.Gherkin.Phrases;

namespace Cress.Gherkin.Tests;

public sealed class PhraseOverrideTests
{
    [Fact]
    public void CreateWithOverrides_NonExistentPath_FallsBackToDefaults()
    {
        var library = PhraseLibrary.CreateWithOverrides("/nonexistent/path/phrases.yaml");
        var phrase = library.Resolve("http.get", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["url"] = "https://x.com" });
        Assert.NotNull(phrase);
        Assert.Equal(GherkinKeyword.When, phrase.Keyword);
    }

    [Fact]
    public void CreateWithOverrides_ValidYaml_OverridesDefaultPhrase()
    {
        var yaml = """
            phrases:
              - stepOp: ui.click
                keyword: When
                template: I tap {automationId}
            """;

        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, yaml);
            var library = PhraseLibrary.CreateWithOverrides(tmpFile);
            var phrase = library.Resolve("ui.click",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["automationId"] = "BtnOk"
                });
            Assert.NotNull(phrase);
            Assert.Equal("I tap {automationId}", phrase.Template);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void CreateWithOverrides_ValidYaml_AddsNewPhrase()
    {
        var yaml = """
            phrases:
              - stepOp: custom.my-step
                keyword: When
                template: I do the custom thing with {param}
            """;

        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, yaml);
            var library = PhraseLibrary.CreateWithOverrides(tmpFile);
            var phrase = library.Resolve("custom.my-step",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["param"] = "foo"
                });
            Assert.NotNull(phrase);
            Assert.Equal("I do the custom thing with {param}", phrase.Template);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }
}
