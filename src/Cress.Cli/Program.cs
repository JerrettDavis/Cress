using Cress.Cli.Commands;
using Cress.Execution;
using Cress.Execution.Drivers;
using Cress.LivingDocs;
using Cress.ProjectSystem;
using Cress.Specs;
using Cress.Studio.Services;
using Cress.Validation;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

var services = new ServiceCollection()
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
    .AddSingleton<RunMetricsService>()
    .AddSingleton<RuntimeOrchestrator>()
    .AddSingleton<IRuntimeDriver, HttpRuntimeDriver>()
    .AddSingleton<IRuntimeDriver, FlaUiRuntimeDriver>()
    .AddSingleton<IRuntimeDriver, PlaywrightRuntimeDriver>()
    .BuildServiceProvider();

var rootCommand = new RootCommand("Cress end-to-end testing CLI");
rootCommand.Name = "cress";
rootCommand.AddCommand(InitCommand.Create());
rootCommand.AddCommand(ConfigCommand.Create(services));
rootCommand.AddCommand(ValidateCommand.Create(services));
rootCommand.AddCommand(DiscoverCommand.Create(services));
rootCommand.AddCommand(PlanCommand.Create(services));
rootCommand.AddCommand(RunCommand.Create(services));
rootCommand.AddCommand(ReportCommand.Create(services));
rootCommand.AddCommand(GenerateCommand.Create(services));
rootCommand.AddCommand(ExportCommand.Create(services));
rootCommand.AddCommand(ImportCommand.Create(services));
rootCommand.AddCommand(DoctorCommand.Create(services));
rootCommand.AddCommand(MetricsCommand.Create(services));
rootCommand.AddCommand(FlakeReportCommand.Create(services));
rootCommand.AddCommand(DocCommand.Create(services));

return await rootCommand.InvokeAsync(args);
