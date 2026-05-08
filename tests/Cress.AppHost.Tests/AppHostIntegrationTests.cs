using Aspire.Hosting.Testing;

namespace Cress.AppHost.Tests;

[Collection(AspireAppHostCollection.Name)]
public sealed class AppHostIntegrationTests
{
    private readonly AspireAppHostFixture _fixture;

    public AppHostIntegrationTests(AspireAppHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Studio_web_endpoint_is_published_by_the_apphost()
    {
        var endpoint = _fixture.App.GetEndpoint("studio-web", "http");

        Assert.NotNull(endpoint);
        Assert.False(string.IsNullOrWhiteSpace(endpoint.ToString()));
    }

    [Fact]
    public async Task Studio_web_homepage_is_reachable_through_the_apphost()
    {
        using var client = _fixture.App.CreateHttpClient("studio-web", "http");

        using var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("studio-shell", html, StringComparison.OrdinalIgnoreCase);
    }
}
