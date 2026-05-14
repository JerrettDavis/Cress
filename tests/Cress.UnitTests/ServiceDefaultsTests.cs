using Cress.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Cress.UnitTests;

public sealed class ServiceDefaultsTests
{
    [Fact]
    public async Task AddDefaultHealthChecks_registers_live_probe()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddDefaultHealthChecks();
        using var host = builder.Build();

        var healthChecks = host.Services.GetRequiredService<HealthCheckService>();
        var result = await healthChecks.CheckHealthAsync(registration => registration.Tags.Contains("live"));

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("self", result.Entries.Keys);
    }

    [Fact]
    public void AddServiceDefaults_registers_core_services_and_otlp_exporter_path()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:4317";

        var returned = builder.AddServiceDefaults();
        using var host = builder.Build();

        Assert.Same(builder, returned);
        Assert.NotNull(host.Services.GetService<HealthCheckService>());
        Assert.NotNull(host.Services.GetService<IHttpClientFactory>());
    }

    [Fact]
    public async Task AddServiceDefaults_allows_health_and_normal_requests_to_execute()
    {
        await using var app = CreateApp(Environments.Development, configureDefaults: true);
        app.MapGet("/ping", () => Results.Ok("pong"));
        app.MapDefaultEndpoints();
        await app.StartAsync();

        var baseAddress = app.Urls.Single();

        using var client = new HttpClient { BaseAddress = new Uri(baseAddress) };
        using var alive = await client.GetAsync("/alive");
        using var ping = await client.GetAsync("/ping");

        Assert.True(alive.IsSuccessStatusCode);
        Assert.True(ping.IsSuccessStatusCode);
    }

    [Fact]
    public void MapDefaultEndpoints_maps_health_routes_in_development_only()
    {
        var developmentApp = CreateApp(Environments.Development);
        developmentApp.MapDefaultEndpoints();

        var developmentRoutes = GetRoutePatterns(developmentApp);
        Assert.Contains("/health", developmentRoutes);
        Assert.Contains("/alive", developmentRoutes);

        var productionApp = CreateApp(Environments.Production);
        productionApp.MapDefaultEndpoints();

        var productionRoutes = GetRoutePatterns(productionApp);
        Assert.DoesNotContain("/health", productionRoutes);
        Assert.DoesNotContain("/alive", productionRoutes);
    }

    private static WebApplication CreateApp(string environmentName, bool configureDefaults = false)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "cress-service-defaults-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ContentRootPath = contentRoot
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (configureDefaults)
        {
            builder.AddServiceDefaults();
        }
        else
        {
            builder.AddDefaultHealthChecks();
        }

        return builder.Build();
    }

    private static IReadOnlyList<string?> GetRoutePatterns(WebApplication app)
        => ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();
}
