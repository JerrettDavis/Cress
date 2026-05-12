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
            .AddCressRuntime()
            .AddCressFlawrightRuntime()
            .AddSingleton<ProjectValidator>()
            .AddSingleton<StepStubGenerator>()
            .AddSingleton<RunResultRepository>()
            .AddSingleton<StudioProjectService>()
            .AddSingleton<StudioSuiteService>()
            .AddSingleton<FlowDocumentService>()
            .AddSingleton<StudioAuthoringService>()
            .AddSingleton<StudioRunInsightsService>()
            .AddSingleton<RunMetricsService>()
            .AddSingleton<IStudioCompanionClient, StudioCompanionClient>()
            .AddSingleton<IStudioRunnerExecutor, StudioRuntimeRunnerExecutor>()
            .AddSingleton<IStudioRunnerNode, StudioEmbeddedRunnerNode>()
            .AddSingleton<IStudioRunnerService, StudioRunnerService>()
            .AddSingleton<IStudioRecorderService, StudioRecorderService>();
}
