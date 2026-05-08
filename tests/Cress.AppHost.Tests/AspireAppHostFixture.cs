using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace Cress.AppHost.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AspireAppHostCollection : ICollectionFixture<AspireAppHostFixture>
{
    public const string Name = "Aspire AppHost";
}

public sealed class AspireAppHostFixture : IAsyncLifetime
{
    private string? _originalDisableDesktopValue;
    private IDistributedApplicationTestingBuilder? _builder;

    public DistributedApplication App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _originalDisableDesktopValue = Environment.GetEnvironmentVariable("CRESS_APPHOST_DISABLE_DESKTOP");
        Environment.SetEnvironmentVariable("CRESS_APPHOST_DISABLE_DESKTOP", "1");

        _builder = await DistributedApplicationTestingBuilder.CreateAsync<Program>();
        App = await _builder.BuildAsync();
        await App.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (App is IAsyncDisposable asyncApp)
        {
            await asyncApp.DisposeAsync();
        }
        else
        {
            App.Dispose();
        }

        if (_builder is IAsyncDisposable asyncBuilder)
        {
            await asyncBuilder.DisposeAsync();
        }
        else if (_builder is IDisposable disposableBuilder)
        {
            disposableBuilder.Dispose();
        }

        Environment.SetEnvironmentVariable("CRESS_APPHOST_DISABLE_DESKTOP", _originalDisableDesktopValue);
    }
}
