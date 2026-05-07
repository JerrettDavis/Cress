# Test framework integrations

Cress can now export **framework-native C# tests** for **xUnit**, **NUnit**, and **MSTest** without translating the scenario away from the Cress engine. The generated test class calls `Cress.Testing` at runtime, which means designers can keep authoring flows in Studio while product teams run those same flows inside standard .NET test suites and CI pipelines.

## Why this matters

This integration is designed for teams that want all of these at the same time:

1. source-controlled Cress flows and manifests
2. Studio-based authoring for designers, QA, or SDETs
3. product-owned test projects that already use xUnit, NUnit, or MSTest
4. pipeline-native execution, filtering, and reporting

## Supported export commands

Each command generates a C# test file for one flow:

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- export xunit specs\httpbin-smoke --flow httpbin-get-smoke
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- export nunit specs\httpbin-smoke --flow httpbin-get-smoke
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- export mstest specs\httpbin-smoke --flow httpbin-get-smoke
```

By default, Cress writes generated files to `<project>\exports\`.

## Common options

| Option | Purpose |
| --- | --- |
| `--flow` | Select the flow id to export. Partial matching is supported through the existing flow resolution logic. |
| `--output` | Override the output `.cs` file path. |
| `--namespace` | Set the generated namespace. |
| `--class-name` | Force a specific generated class name. |
| `--profile` | Embed a Cress profile so the generated test uses the same environment or driver settings in CI. |

## Minimal test-project setup

Generated files are intentionally small. The host test project owns package references, test filters, and pipeline conventions.

### xUnit

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.5.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Cress.Testing\Cress.Testing.csproj" />
  </ItemGroup>
</Project>
```

### NUnit

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NUnit" Version="4.4.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="5.1.0">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.5.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Cress.Testing\Cress.Testing.csproj" />
  </ItemGroup>
</Project>
```

### MSTest

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MSTest.TestFramework" Version="3.8.3" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.8.3">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.5.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Cress.Testing\Cress.Testing.csproj" />
  </ItemGroup>
</Project>
```

## Step-by-step: export a Calculator smoke test into xUnit

This is a practical bridge between Studio-authored desktop flows and a product-owned regression suite.

### 1. Author and validate the flow

Use `specs\calc-smoke` or your own desktop project:

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate specs\calc-smoke
```

### 2. Export the flow

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- export xunit specs\calc-smoke --flow calc.add-two-plus-two --profile local
```

### 3. Add the generated file to an xUnit test project

The generated test will:

1. reference `Cress.Testing`
2. resolve the Cress project path
3. call `CressTestEngine.RunFlowAsync(...)`
4. fail the test with a framework-native assertion failure if the Cress run fails

### 4. Run it with the rest of the suite

```powershell
dotnet test tests\MyProduct.Tests\MyProduct.Tests.csproj --filter FullyQualifiedName~Calc
```

## Step-by-step: export a `cmd.exe` smoke flow into MSTest

This is a strong pattern for infrastructure or tooling teams who already have MSTest-based validation suites.

Use the `demos\cmd-smoke` project from the CLI walkthrough, or substitute your own CLI-focused Cress project path.

### 1. Create or reuse a CLI flow

For example, `cmd.echo` from the CLI guide.

### 2. Export it

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- export mstest demos\cmd-smoke --flow cmd.echo --namespace Contoso.Tools.Tests --class-name CmdSmokeTests
```

### 3. Commit the file into the product test suite

The scenario still runs through Cress, so you keep:

- profiles
- evidence collection
- source-authored steps
- generated reports

while also gaining:

- MSTest discovery
- normal `dotnet test` execution
- easy pipeline adoption on existing agents

## Example generated shape

An xUnit export from `specs\httpbin-smoke` looks like this:

```csharp
using System.Threading.Tasks;
using Cress.Testing;
using Xunit;

namespace Cress.Generated.Xunit;

public sealed class HttpbinGetSmokeXunitTests
{
    [Fact]
    public async Task GETGetReturns200WithRequestMetadata()
    {
        await CressTestEngine.RunFlowAsync(
            projectPath: CressTestPaths.ResolveProjectPath(@"specs\httpbin-smoke"),
            flowPath: @"flows\httpbin\get-smoke.flow.yaml");
    }
}
```

## How the generated tests work

The exported test code keeps a small surface area on purpose:

1. framework attribute such as `[Fact]`, `[Test]`, or `[TestMethod]`
2. one async test method
3. a call to `CressTestPaths.ResolveProjectPath(...)`
4. a call to `CressTestEngine.RunFlowAsync(...)`

That design keeps the execution model centralized in Cress instead of scattering runner logic across generated files.

## Recommended repository layout

Use one of these patterns:

| Pattern | When to use it |
| --- | --- |
| Generate into `<cress-project>\exports\` and copy into a test project | good for experimentation while you settle on the final ownership model |
| Generate directly into a product test project with `--output` | good when you are ready to check generated tests into the main suite |
| Regenerate in CI from source-controlled flows | good when generated code is treated as a build artifact rather than authored source |

## CI and pipeline guidance

For pipeline use:

1. keep the Cress project in the repo so generated tests can resolve it predictably
2. pass `--profile ci` during export when the pipeline should pin a known environment profile
3. publish standard framework test results and Cress evidence artifacts together
4. run desktop flows on Windows agents with the right UI automation prerequisites

## When to use this instead of plain `cress run`

Prefer generated framework tests when:

- the team already has a large .NET test suite
- test selection and ownership are organized around xUnit, NUnit, or MSTest
- pipeline policy expects one framework-native entry point

Prefer plain `cress run` when:

- you are exploring or iterating rapidly
- the suite is primarily Cress-owned
- you want the simplest authoring-to-execution path

For fuller demos that show how these exported tests fit into an engineering workflow, see [Framework demos and development-cycle integration](test-framework-demos.md).
