var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var studioProjectDirectory = Path.Combine(repoRoot, "src", "Cress.Studio");
var studioExecutable = ResolveStudioExecutablePath(studioProjectDirectory);

builder.AddProject<Projects.Cress_Studio_Web>("studio-web")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithExternalHttpEndpoints();

builder.AddExecutable("studio", studioExecutable, studioProjectDirectory)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development");

builder.Build().Run();

static string ResolveStudioExecutablePath(string studioProjectDirectory)
{
    foreach (var configuration in new[] { "Debug", "Release" })
    {
        var candidate = Path.Combine(
            studioProjectDirectory,
            "bin",
            configuration,
            "net10.0-windows",
            "Cress.Studio.exe");

        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return Path.Combine(
        studioProjectDirectory,
        "bin",
        "Debug",
        "net10.0-windows",
        "Cress.Studio.exe");
}

static string FindRepoRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Cress.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return startDirectory;
}
