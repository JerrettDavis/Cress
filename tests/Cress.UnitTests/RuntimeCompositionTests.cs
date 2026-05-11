using Cress.Core.Models;
using Cress.Execution;
using Cress.Execution.Drivers;
using Microsoft.Extensions.DependencyInjection;

namespace Cress.UnitTests;

public sealed class RuntimeCompositionTests
{
    [Fact]
    public void AddCressRuntime_IsIdempotent_ForBuiltInDrivers()
    {
        var services = new ServiceCollection()
            .AddCressRuntime()
            .AddCressRuntime()
            .BuildServiceProvider();

        var drivers = services.GetServices<IRuntimeDriver>().ToList();

        Assert.Equal(2, drivers.Count);
        Assert.Single(drivers.OfType<HttpRuntimeDriver>());
        Assert.Single(drivers.OfType<PlaywrightRuntimeDriver>());
    }

    [Fact]
    public void AddRuntimeDriver_AllowsCustomDriverRegistration()
    {
        var services = new ServiceCollection()
            .AddCressRuntime()
            .AddRuntimeDriver<FakeRuntimeDriver>()
            .BuildServiceProvider();

        var drivers = services.GetServices<IRuntimeDriver>().ToList();

        Assert.Contains(drivers, driver => driver is FakeRuntimeDriver);
    }

    private sealed class FakeRuntimeDriver : IRuntimeDriver
    {
        public string Name => "fake";

        public IReadOnlyList<Diagnostic> HealthCheck(ProjectCatalog catalog) => [];

        public Task<IDriverSession> StartSessionAsync(DriverSessionStartContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
