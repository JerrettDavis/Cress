using Cress.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

CressAppHostComposition.Configure(
    builder);

builder.Build().Run();

public partial class Program;
