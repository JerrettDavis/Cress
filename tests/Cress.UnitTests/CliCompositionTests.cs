using Cress.Cli;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace Cress.UnitTests;

public sealed class CliCompositionTests
{
    [Fact]
    public void AddCressCli_RegistersBuiltInCommands_AndSupportsExtensions()
    {
        var services = new ServiceCollection()
            .AddCressCli()
            .AddCressCliCommand(_ => new Command("custom", "Custom extension command"))
            .BuildServiceProvider();

        var rootCommand = services.GetRequiredService<CressCliApplication>().CreateRootCommand();
        var commandNames = rootCommand.Subcommands.Select(command => command.Name).ToList();

        Assert.Equal(15, commandNames.Count);
        Assert.Contains("init", commandNames);
        Assert.Contains("run", commandNames);
        Assert.Contains("doc", commandNames);
        Assert.Contains("custom", commandNames);
    }

    [Fact]
    public void CreateRootCommand_RejectsDuplicateCommandNames()
    {
        var services = new ServiceCollection()
            .AddCressCli()
            .AddCressCliCommand(_ => new Command("run", "Duplicate run command"))
            .BuildServiceProvider();

        var application = services.GetRequiredService<CressCliApplication>();
        var exception = Assert.Throws<InvalidOperationException>(() => application.CreateRootCommand());

        Assert.Contains("'run'", exception.Message, StringComparison.Ordinal);
    }
}
