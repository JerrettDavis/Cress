using Aspire.Hosting.Testing;

namespace Cress.AppHost.Tests;

[Collection(AspireAppHostCollection.Name)]
public sealed class AppHostEndToEndTests
{
    private readonly AspireAppHostFixture _fixture;

    public AppHostEndToEndTests(AspireAppHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("/workspace", "workspace-section")]
    [InlineData("/designer", "designer-section")]
    [InlineData("/results", "results-panel")]
    public async Task Apphost_serves_the_documented_studio_routes(string route, string expectedTestId)
    {
        using var client = _fixture.App.CreateHttpClient("studio-web", "http");

        using var response = await client.GetAsync(route);

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedTestId, html, StringComparison.OrdinalIgnoreCase);
    }
}
