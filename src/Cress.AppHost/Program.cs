using Cress.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

CressAppHostComposition.Configure(
    builder,
    Directory.GetCurrentDirectory(),
    Environment.GetEnvironmentVariable("CRESS_APPHOST_DISABLE_DESKTOP"));

builder.Build().Run();

public partial class Program;
