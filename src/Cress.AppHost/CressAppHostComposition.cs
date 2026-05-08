using Aspire.Hosting;

namespace Cress.AppHost;

internal static class CressAppHostComposition
{
    public static void Configure(IDistributedApplicationBuilder builder)
    {
        builder.AddProject<Projects.Cress_Studio_Web>("studio-web")
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithExternalHttpEndpoints();
    }
}
