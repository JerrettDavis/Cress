using Cress.Cli;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection()
    .AddCressCli()
    .BuildServiceProvider();

return await services.GetRequiredService<CressCliApplication>().InvokeAsync(args);
