using Cress.LivingDocs;

namespace Cress.LivingDocs.Tests;

public sealed class TemplateHelpersTests
{
    [Theory]
    [InlineData(3_600_000d, "1.0h")]
    [InlineData(60_000d, "1.0m")]
    [InlineData(1_000d, "1.0s")]
    [InlineData(250d, "250ms")]
    public void FormatDuration_FormatsNumericMilliseconds(double value, string expected)
    {
        Assert.Equal(expected, TemplateHelpers.format_duration(value));
    }

    [Fact]
    public void FormatDuration_FormatsTimeSpanValues()
    {
        Assert.Equal("2.5m", TemplateHelpers.format_duration(TimeSpan.FromSeconds(150)));
    }

    [Fact]
    public void FormatDuration_FallsBackForUnconvertibleValues()
    {
        Assert.Equal("abc", TemplateHelpers.format_duration("abc"));
        Assert.Equal("0ms", TemplateHelpers.format_duration(null!));
    }

    [Fact]
    public void FormatPercent_UsesOneDecimalPercent()
    {
        Assert.Equal("12.3%", TemplateHelpers.format_percent(0.1234));
    }

    [Theory]
    [InlineData("passing", "badge-pass", "✓")]
    [InlineData("FAILING", "badge-fail", "✗")]
    [InlineData("Flaky", "badge-flaky", "~")]
    [InlineData("unknown", "badge-unknown", "?")]
    [InlineData(null, "badge-unknown", "?")]
    public void StatusHelpers_MapExpectedValues(string? status, string expectedBadge, string expectedEmoji)
    {
        Assert.Equal(expectedBadge, TemplateHelpers.status_badge_class(status!));
        Assert.Equal(expectedEmoji, TemplateHelpers.status_emoji(status!));
    }
}
