using System.Text;
using Cress.Core.Models;

namespace Cress.Execution;

public sealed class StepStubGenerator
{
    public OperationResult<GeneratedStubSet> Generate(ProjectCatalog catalog, IEnumerable<NormalizedFlow> flows, string language, bool force)
    {
        var unresolved = flows
            .SelectMany(flow => flow.Actions.Concat(flow.Expectations)
                .Where(executable => !catalog.StepRegistry.TryResolve(executable.Name, out _))
                .Select(executable => new MissingStepReference(flow.SourceFile, executable.Name, executable.Inputs.Keys.ToList())))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GeneratedStepDefinition(
                group.Key,
                group.SelectMany(item => item.Inputs).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                group.Select(item => item.SourceFile).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        var diagnostics = new List<Diagnostic>();
        var created = new List<string>();
        foreach (var step in unresolved)
        {
            switch (language.Trim().ToLowerInvariant())
            {
                case "dotnet":
                    GenerateDotNetStub(catalog, step, force, created, diagnostics);
                    break;
                case "typescript":
                case "ts":
                    GenerateTypeScriptStub(catalog, step, force, created, diagnostics);
                    break;
                default:
                    diagnostics.Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "GEN001",
                        Message = $"Language '{language}' is not supported for step generation.",
                        File = catalog.ProjectRoot
                    });
                    break;
            }
        }

        return new OperationResult<GeneratedStubSet>
        {
            Value = new GeneratedStubSet
            {
                Language = language,
                Steps = unresolved.Select(step => step.Name).ToList(),
                Files = created
            },
            Diagnostics = diagnostics
        };
    }

    private static void GenerateDotNetStub(ProjectCatalog catalog, GeneratedStepDefinition step, bool force, ICollection<string> created, ICollection<Diagnostic> diagnostics)
    {
        var safeName = SafeName(step.Name);
        var pluginRoot = Path.Combine(catalog.ProjectRoot, "steps", "dotnet", safeName);
        if (Directory.Exists(pluginRoot) && !force)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = "GEN002",
                Message = $"Step stub '{step.Name}' already exists. Use --force to overwrite.",
                File = pluginRoot
            });
            return;
        }

        Directory.CreateDirectory(pluginRoot);
        var sdkReference = TryFindSdkProjectReference();
        var projectFile = Path.Combine(pluginRoot, $"{safeName}.csproj");
        File.WriteAllText(projectFile, $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            {{sdkReference}}
          </ItemGroup>
        </Project>
        """);

        var className = ToIdentifier(step.Name);
        var sourceComment = string.Join(Environment.NewLine, step.SourceFiles.Select(path => $"//   - {path}"));
        File.WriteAllText(Path.Combine(pluginRoot, $"{className}Module.cs"), $$"""
        using Cress.Sdk;

        namespace GeneratedSteps;

        // Referenced by:
        {{sourceComment}}
        public sealed class {{className}}Module : ICressPluginModule
        {
            public IEnumerable<StepHandlerRegistration> GetStepHandlers()
            {
                yield return new StepHandlerRegistration("Execute", ExecuteAsync);
            }

            private static Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken)
            {
                context.Logger.Info("TODO: implement {{step.Name}}.", new Dictionary<string, string>
                {
                    ["step"] = context.StepName
                });

                return Task.FromResult(new StepExecutionResult
                {
                    Success = false,
                    Message = "TODO: implement generated step '{{step.Name}}'."
                });
            }
        }
        """);

        WriteManifest(catalog.ProjectRoot, step, safeName);
        created.Add(projectFile);
        created.Add(Path.Combine(pluginRoot, $"{className}Module.cs"));
    }

    private static void GenerateTypeScriptStub(ProjectCatalog catalog, GeneratedStepDefinition step, bool force, ICollection<string> created, ICollection<Diagnostic> diagnostics)
    {
        var safeName = SafeName(step.Name);
        var pluginRoot = Path.Combine(catalog.ProjectRoot, "steps", "node", safeName);
        if (Directory.Exists(pluginRoot) && !force)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = "GEN003",
                Message = $"Step stub '{step.Name}' already exists. Use --force to overwrite.",
                File = pluginRoot
            });
            return;
        }

        Directory.CreateDirectory(Path.Combine(pluginRoot, "src"));
        var sdkDependency = TryFindTypeScriptSdkDependency(pluginRoot);
        File.WriteAllText(Path.Combine(pluginRoot, "package.json"), $$"""
        {
          "name": "{{safeName}}",
          "private": true,
          "type": "module",
          "main": "dist/index.js",
          "types": "dist/index.d.ts",
          "scripts": {
            "build": "tsc -p tsconfig.json"
          },
          "dependencies": {
            "@cress/sdk": "{{sdkDependency}}"
          },
          "devDependencies": {
            "typescript": "^5.6.3"
          }
        }
        """);
        File.WriteAllText(Path.Combine(pluginRoot, "tsconfig.json"), """
        {
          "compilerOptions": {
            "target": "ES2022",
            "module": "NodeNext",
            "moduleResolution": "NodeNext",
            "declaration": true,
            "outDir": "dist",
            "strict": true
          },
          "include": ["src/**/*.ts"]
        }
        """);
        File.WriteAllText(Path.Combine(pluginRoot, "src", "index.ts"), $$"""
        import { createPluginModule, createStepResult, defineStep, type StepExecutionContext } from "@cress/sdk";

        // Referenced by:
        {{string.Join(Environment.NewLine, step.SourceFiles.Select(path => $"//   - {path}"))}}

        export async function execute(context: StepExecutionContext) {
          context.logger.info("TODO: implement {{step.Name}}.", { step: context.stepName });
          return createStepResult({
            success: false,
            message: "TODO: implement generated step '{{step.Name}}'."
          });
        }

        export default createPluginModule({
          steps: [defineStep("Execute", execute)]
        });
        """);

        WriteManifest(catalog.ProjectRoot, step, safeName);
        created.Add(Path.Combine(pluginRoot, "package.json"));
        created.Add(Path.Combine(pluginRoot, "tsconfig.json"));
        created.Add(Path.Combine(pluginRoot, "src", "index.ts"));
    }

    private static void WriteManifest(string projectRoot, GeneratedStepDefinition step, string pluginName)
    {
        var manifestRoot = Path.Combine(projectRoot, "steps", "manifests", "generated");
        Directory.CreateDirectory(manifestRoot);
        var builder = new StringBuilder();
        builder.AppendLine("version: 1");
        builder.AppendLine("steps:");
        builder.AppendLine($"  - name: \"{step.Name}\"");
        builder.AppendLine($"    description: \"TODO: implement {step.Name}.\"");
        if (step.Inputs.Count > 0)
        {
            builder.AppendLine("    inputs:");
            foreach (var input in step.Inputs)
            {
                builder.AppendLine($"      \"{input}\":");
                builder.AppendLine("        type: string");
                builder.AppendLine("        required: true");
            }
        }

        builder.AppendLine("    retrySafe: false");
        builder.AppendLine("    implementation:");
        builder.AppendLine($"      plugin: \"{pluginName}\"");
        builder.AppendLine("      operation: \"Execute\"");
        File.WriteAllText(Path.Combine(manifestRoot, $"{pluginName}.yaml"), builder.ToString());
    }

    private static string TryFindSdkProjectReference()
    {
        var path = RepositoryAssetLocator.FindRepositoryAsset(Path.Combine("src", "Cress.Sdk", "Cress.Sdk.csproj"));
        return path is not null
            ? $"<ProjectReference Include=\"{path}\" />"
            : "<PackageReference Include=\"Cress.Sdk\" Version=\"0.1.0\" />";
    }

    private static string TryFindTypeScriptSdkDependency(string pluginRoot)
    {
        var sdkPath = RepositoryAssetLocator.FindRepositoryAsset(Path.Combine("node", "cress-sdk"));
        if (sdkPath is null)
        {
            return "^0.1.0";
        }

        var relative = Path.GetRelativePath(pluginRoot, sdkPath).Replace('\\', '/');
        if (!relative.StartsWith(".", StringComparison.Ordinal))
        {
            relative = $"./{relative}";
        }

        return $"file:{relative}";
    }

    private static string SafeName(string name) => ToIdentifier(name).ToLowerInvariant();

    private static string ToIdentifier(string value)
    {
        var builder = new StringBuilder();
        foreach (var segment in value.Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(char.ToUpperInvariant(segment[0]));
            if (segment.Length > 1)
            {
                builder.Append(segment[1..]);
            }
        }

        if (builder.Length == 0)
        {
            builder.Append("GeneratedStep");
        }

        if (!char.IsLetter(builder[0]))
        {
            builder.Insert(0, 'S');
        }

        return builder.ToString();
    }

    private sealed record MissingStepReference(string? SourceFile, string Name, IReadOnlyList<string> Inputs);
    private sealed record GeneratedStepDefinition(string Name, IReadOnlyList<string> Inputs, IReadOnlyList<string?> SourceFiles);
}

public sealed record GeneratedStubSet
{
    public string Language { get; init; } = string.Empty;
    public IReadOnlyList<string> Steps { get; init; } = [];
    public IReadOnlyList<string> Files { get; init; } = [];
}
