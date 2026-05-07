using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Execution.Drivers;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Execution;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCressRuntime(this IServiceCollection services)
        => services
            .AddSingleton<ProjectLocator>()
            .AddSingleton<ConfigLoader>()
            .AddSingleton<ProfileLoader>()
            .AddSingleton<FlowParser>()
            .AddSingleton<FlowNormalizer>()
            .AddSingleton<CapabilityParser>()
            .AddSingleton<StepManifestParser>()
            .AddSingleton<FixtureManifestParser>()
            .AddSingleton<StepRegistry>()
            .AddSingleton<ProjectCatalogService>()
            .AddSingleton<PlanGenerator>()
            .AddSingleton<PluginHost>()
            .AddSingleton<ReportGenerator>()
            .AddSingleton<RuntimeOrchestrator>()
            .AddSingleton<IRuntimeDriver, HttpRuntimeDriver>()
            .AddSingleton<IRuntimeDriver, FlaUiRuntimeDriver>()
            .AddSingleton<IRuntimeDriver, PlaywrightRuntimeDriver>();
}
