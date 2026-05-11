using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Execution.Drivers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cress.Execution;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCressRuntime(this IServiceCollection services)
        => services
            .AddCoreRuntimeServices()
            .AddRuntimeDriver<HttpRuntimeDriver>()
            .AddRuntimeDriver<PlaywrightRuntimeDriver>();

    public static IServiceCollection AddRuntimeDriver<TRuntimeDriver>(this IServiceCollection services)
        where TRuntimeDriver : class, IRuntimeDriver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeDriver, TRuntimeDriver>());
        return services;
    }

    private static IServiceCollection AddCoreRuntimeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ProjectLocator>();
        services.TryAddSingleton<ConfigLoader>();
        services.TryAddSingleton<ProfileLoader>();
        services.TryAddSingleton<FlowParser>();
        services.TryAddSingleton<FlowNormalizer>();
        services.TryAddSingleton<CapabilityParser>();
        services.TryAddSingleton<StepManifestParser>();
        services.TryAddSingleton<FixtureManifestParser>();
        services.TryAddSingleton<StepRegistry>();
        services.TryAddSingleton<ProjectCatalogService>();
        services.TryAddSingleton<PlanGenerator>();
        services.TryAddSingleton<IDotNetPluginModuleLoader, ReflectionDotNetPluginModuleLoader>();
        services.TryAddSingleton<PluginHost>();
        services.TryAddSingleton<ReportGenerator>();
        services.TryAddSingleton<RuntimeOrchestrator>();

        return services;
    }
}
