# CLI reference

The `cress` CLI is the main entry point for project creation, validation, execution, diagnostics, reporting, and documentation.

## Top-level command map

| Command | Purpose |
| --- | --- |
| `init` | create a new Cress project with the standard folders, profiles, and starter assets |
| `config` | work with effective project configuration |
| `validate` | validate a project before execution |
| `discover` | inspect flows, steps, capabilities, fixtures, and drivers |
| `plan` | generate an execution plan without running it |
| `run` | execute one or more flows |
| `report` | list, open, or summarize generated reports |
| `generate` | generate project assets such as missing step stubs |
| `export` | emit flows to Gherkin, Cypress, Selenium IDE, or framework-native xUnit / NUnit / MSTest tests |
| `import` | bring in Gherkin, Playwright codegen, or Postman collections |
| `doctor` | check environment readiness and driver health |
| `metrics` | summarize run history trends |
| `flake-report` | identify flaky flows or steps |
| `doc generate` | build a living-document HTML page for a project |

## Common onboarding commands

### Initialize a project

```text
Usage:
  cress init [<path>] [options]
```

Options:

- `--force` overwrites an existing Cress project

### Discover the current command set

```text
Usage:
  cress [command] [options]
```

## Execution workflow commands

### Validate

Run this before execution to catch project-shape or manifest issues early.

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate
```

### Run

```text
Usage:
  cress run [<flow>] [options]
```

Options:

- `--tag <tag>`
- `--profile <profile>`
- `--parallel <parallel>`
- `--report <report>`
- `--continue-on-failure`
- `--dry-run`
- `--json`

Typical patterns:

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run --tag smoke --profile ci
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run flows\example\example-flow.flow.yaml --report html,json,junit
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run --dry-run --json
```

### Doctor

```text
Usage:
  cress doctor [options]
```

Use `doctor` when:

- the project root is not being found
- a configured driver does not appear to be available
- plugin-backed steps are not being discovered correctly

### Discover

```text
Usage:
  cress discover [command] [options]
```

Subcommands:

- `flows`
- `steps`
- `capabilities`
- `fixtures`
- `drivers`

## Reporting and documentation commands

### Living docs

```text
Usage:
  cress doc generate [<project>] [options]
```

Options:

- `--template <template>`
- `--output <output>`
- `--title <title>`
- `--logo <logo>`
- `--accent <accent>`

Example:

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- doc generate specs\httpbin-smoke --output artifacts\docs\httpbin-smoke.html --template executive
```

## Import and export commands

Use these when you need to bootstrap a flow from an existing artifact or share it with another ecosystem:

- `import gherkin`
- `import playwright`
- `import postman`
- `export gherkin`
- `export cypress`
- `export selenium-ide`
- `export xunit`
- `export nunit`
- `export mstest`

### Framework-native test exports

Use these commands when you want a Cress-authored flow to run inside an existing .NET test suite instead of through a standalone `cress run`.

#### xUnit

```text
Usage:
  cress export xunit [<project>] [options]
```

Common options:

- `--flow <flow>`
- `--output <output>`
- `--namespace <namespace>`
- `--class-name <class-name>`
- `--profile <profile>`

Example:

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- export xunit specs\httpbin-smoke --flow httpbin-get-smoke --output tests\Contoso.Api.Tests\Generated\HttpbinSmokeTests.cs --profile ci
```

#### NUnit

```text
Usage:
  cress export nunit [<project>] [options]
```

Example:

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- export nunit specs\calc-smoke --flow calc.add-two-plus-two --output tests\Contoso.Desktop.Tests\Generated\CalculatorSmokeTests.cs --profile local
```

#### MSTest

```text
Usage:
  cress export mstest [<project>] [options]
```

Example:

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- export mstest demos\cmd-smoke --flow cmd.echo --output tests\Contoso.Tools.Tests\Generated\CmdSmokeTests.cs --profile ci
```

Generated test files call `CressTestEngine.RunFlowAsync(...)`, so the host framework owns discovery and filtering while Cress still owns flow execution, profiles, and evidence.

In this example, `demos\cmd-smoke` is the demo project created in the CLI testing walkthrough rather than a built-in repository sample.

## Diagnostic commands for mature suites

Once the suite has run history, add:

- `metrics` for historical trends
- `flake-report` for instability analysis
- `report summarize` for targeted run review
