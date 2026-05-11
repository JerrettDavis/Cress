using Cress.Cli.Commands;
using Cress.Execution;
using Cress.LivingDocs;
using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Studio.Services;
using Cress.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.CommandLine;

namespace Cress.Cli;

public interface ICressCliCommand
{
    Command Create(IServiceProvider services);
}

public sealed class CressCliApplication
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<ICressCliCommand> _commands;

    public CressCliApplication(IServiceProvider services, IEnumerable<ICressCliCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(commands);

        _services = services;
        _commands = commands.ToList();
    }

    public RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Cress end-to-end testing CLI");
        var commandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in _commands)
        {
            var command = registration.Create(_services);
            if (!commandNames.Add(command.Name))
            {
                throw new InvalidOperationException($"A CLI command named '{command.Name}' is already registered.");
            }

            rootCommand.AddCommand(command);
        }

        return rootCommand;
    }

    public Task<int> InvokeAsync(string[] args)
        => CreateRootCommand().InvokeAsync(args);
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCressCli(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCressRuntime();
        services.TryAddSingleton<ProjectValidator>();
        services.TryAddSingleton<StepStubGenerator>();
        services.TryAddSingleton<RunResultRepository>();
        services.TryAddSingleton<RunMetricsService>();
        services.TryAddSingleton<CressCliApplication>();

        return services.AddBuiltInCressCliCommands();
    }

    public static IServiceCollection AddCressCliCommand(this IServiceCollection services, Func<IServiceProvider, Command> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        services.AddSingleton<ICressCliCommand>(new DelegateCressCliCommand(factory));
        return services;
    }

    private static IServiceCollection AddBuiltInCressCliCommands(this IServiceCollection services)
        => services
            .AddCressCliCommand(_ => InitCommand.Create())
            .AddCressCliCommand(ConfigCommand.Create)
            .AddCressCliCommand(ValidateCommand.Create)
            .AddCressCliCommand(DiscoverCommand.Create)
            .AddCressCliCommand(PlanCommand.Create)
            .AddCressCliCommand(RunCommand.Create)
            .AddCressCliCommand(ReportCommand.Create)
            .AddCressCliCommand(GenerateCommand.Create)
            .AddCressCliCommand(ExportCommand.Create)
            .AddCressCliCommand(ImportCommand.Create)
            .AddCressCliCommand(DoctorCommand.Create)
            .AddCressCliCommand(MetricsCommand.Create)
            .AddCressCliCommand(FlakeReportCommand.Create)
            .AddCressCliCommand(DocCommand.Create);

    private sealed class DelegateCressCliCommand : ICressCliCommand
    {
        private readonly Func<IServiceProvider, Command> _factory;

        public DelegateCressCliCommand(Func<IServiceProvider, Command> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
        }

        public Command Create(IServiceProvider services) => _factory(services);
    }
}
