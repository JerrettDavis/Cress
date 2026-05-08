using Cress.Execution.Drivers;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Execution;

public static class FlawrightServiceCollectionExtensions
{
    public static IServiceCollection AddCressFlawrightRuntime(this IServiceCollection services)
        => services.AddSingleton<IRuntimeDriver, FlawrightRuntimeDriver>();
}
