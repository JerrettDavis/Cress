using Cress.Execution;
using Cress.Execution.Drivers;
using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Studio.Services;
using Cress.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Studio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCressStudioBackend(this IServiceCollection services)
        => services
            .AddSingleton<ProjectLocator>()
            .AddSingleton<ConfigLoader>()
            .AddSingleton<ProfileLoader>()
            .AddSingleton<FlowParser>()
            .AddSingleton<FlowNormalizer>()
            .AddSingleton<CapabilityParser>()
            .AddSingleton<StepManifestParser>()
            .AddSingleton<FixtureManifestParser>()
            .AddSingleton<ProjectValidator>()
            .AddSingleton<StepRegistry>()
            .AddSingleton<ProjectCatalogService>()
            .AddSingleton<PlanGenerator>()
            .AddSingleton<PluginHost>()
            .AddSingleton<StepStubGenerator>()
            .AddSingleton<ReportGenerator>()
            .AddSingleton<RunResultRepository>()
            .AddSingleton<RuntimeOrchestrator>()
            .AddSingleton<IRuntimeDriver, HttpRuntimeDriver>()
            .AddSingleton<IRuntimeDriver, FlaUiRuntimeDriver>()
            .AddSingleton<IRuntimeDriver, PlaywrightRuntimeDriver>()
            .AddSingleton<StudioProjectService>()
            .AddSingleton<StudioSuiteService>()
            .AddSingleton<FlowDocumentService>()
            .AddSingleton<StudioAuthoringService>()
            .AddSingleton<StudioRunInsightsService>()
            .AddSingleton<RunMetricsService>()
            .AddSingleton<IStudioRecorderService, StudioRecorderService>();
}
