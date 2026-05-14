using System.Diagnostics;
using Cress.Execution;
using Cress.Sdk;

namespace Cress.UnitTests;

public sealed class ReflectionDotNetPluginModuleLoaderTests
{
    [Fact]
    public async Task LoadModules_LoadsBuiltPluginModule()
    {
        using var workspace = new TestWorkspace();
        var pluginRoot = CreatePluginProject(workspace, "custom-dotnet", "Execute");
        await BuildPluginProjectAsync(pluginRoot, workspace.GetPath("project"));

        var loader = new ReflectionDotNetPluginModuleLoader();
        var modules = loader.LoadModules(workspace.GetPath("project"), "custom-dotnet");

        var module = Assert.Single(modules);
        var stepHandler = Assert.Single(module.GetStepHandlers());
        Assert.Equal("Execute", stepHandler.Operation);
    }

    [Fact]
    public async Task LoadModules_CachesPerProjectRoot()
    {
        using var firstWorkspace = new TestWorkspace();
        using var secondWorkspace = new TestWorkspace();

        var firstPluginRoot = CreatePluginProject(firstWorkspace, "shared-plugin", "FirstOperation");
        var secondPluginRoot = CreatePluginProject(secondWorkspace, "shared-plugin", "SecondOperation");
        await BuildPluginProjectAsync(firstPluginRoot, firstWorkspace.GetPath("project"));
        await BuildPluginProjectAsync(secondPluginRoot, secondWorkspace.GetPath("project"));

        var loader = new ReflectionDotNetPluginModuleLoader();

        var firstModules = loader.LoadModules(firstWorkspace.GetPath("project"), "shared-plugin");
        var secondModules = loader.LoadModules(secondWorkspace.GetPath("project"), "shared-plugin");

        Assert.Equal("FirstOperation", Assert.Single(Assert.Single(firstModules).GetStepHandlers()).Operation);
        Assert.Equal("SecondOperation", Assert.Single(Assert.Single(secondModules).GetStepHandlers()).Operation);
    }

    private static string CreatePluginProject(TestWorkspace workspace, string pluginName, string operationName)
    {
        var pluginRoot = workspace.GetPath("project", "steps", "dotnet", pluginName);
        Directory.CreateDirectory(pluginRoot);
        var sdkReference = Path.Combine(GetRepositoryRoot(), "src", "Cress.Sdk", "Cress.Sdk.csproj");

        workspace.WriteFile(Path.Combine("project", "steps", "dotnet", pluginName, $"{pluginName}.csproj"), $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="{{sdkReference}}" />
          </ItemGroup>
        </Project>
        """);

        workspace.WriteFile(Path.Combine("project", "steps", "dotnet", pluginName, "CustomDotnetModule.cs"), $$"""
        using Cress.Sdk;

        namespace GeneratedSteps;

        public sealed class CustomDotnetModule : ICressPluginModule
        {
            public IEnumerable<StepHandlerRegistration> GetStepHandlers()
            {
                yield return new StepHandlerRegistration("{{operationName}}", ExecuteAsync);
            }

            private static Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken)
                => Task.FromResult(new StepExecutionResult
                {
                    Success = true,
                    Message = "{{operationName}}"
                });
        }
        """);

        return pluginRoot;
    }

    private static async Task BuildPluginProjectAsync(string pluginRoot, string workingDirectory)
        => await RunProcessAsync("dotnet", workingDirectory, "build", Path.Combine(pluginRoot, $"{Path.GetFileName(pluginRoot)}.csproj"), "-v", "minimal");

    private static async Task RunProcessAsync(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;

        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
}
