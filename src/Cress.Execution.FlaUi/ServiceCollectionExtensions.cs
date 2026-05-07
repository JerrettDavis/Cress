using Cress.Execution.Drivers;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.Execution;

public static class FlaUiServiceCollectionExtensions
{
    public static IServiceCollection AddCressFlaUiRuntime(this IServiceCollection services)
        => services.AddSingleton<IRuntimeDriver, FlaUiRuntimeDriver>();
}
