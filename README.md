# Cress

[![CI](https://github.com/JerrettDavis/Cress/actions/workflows/ci.yml/badge.svg)](https://github.com/JerrettDavis/Cress/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JerrettDavis/Cress/actions/workflows/codeql.yml/badge.svg)](https://github.com/JerrettDavis/Cress/actions/workflows/codeql.yml)
[![Docs](https://github.com/JerrettDavis/Cress/actions/workflows/docs.yml/badge.svg)](https://github.com/JerrettDavis/Cress/actions/workflows/docs.yml)

Cress is a **.NET 10 / C# latest** end-to-end testing platform for Windows, with a WPF Studio, a Blazor web experience, Node-based automation components, and an Aspire AppHost for centralized orchestration and monitoring.

## Guides and examples

See [docs/README.md](docs/README.md) for:

- step-by-step web app automation walkthroughs
- step-by-step desktop app automation walkthroughs
- Studio screenshots and user guides
- integration guidance for bringing Cress into your systems
- design guidance for making your apps easier to automate with Cress

## Requirements

- .NET SDK `10.0.107` or later in the .NET 10 feature band
- Node.js `22.x`
- Windows for the full WPF Studio and desktop E2E experience

## Restore and build

```powershell
npm ci
dotnet restore Cress.sln
dotnet build Cress.sln --configuration Release --no-restore
```

## Run with Aspire orchestration

Start the centralized AppHost to launch the web app, wire in service defaults, and coordinate local monitoring:

```powershell
dotnet run --project src\Cress.AppHost\Cress.AppHost.csproj --configuration Release --launch-profile http
```

The AppHost orchestrates:

- `Cress.Studio.Web` as an Aspire project resource
- `Cress.Studio` as a desktop executable resource

## Validation

```powershell
# Full local .NET test suite, including desktop and end-to-end coverage on Windows
dotnet test Cress.sln --configuration Release --no-build

# Node test suite
node --test node/tests/*.test.mjs

# Living docs preview
dotnet run --project src\Cress.Cli\Cress.Cli.csproj --configuration Release -- doc generate specs\httpbin-smoke --output artifacts\docs\httpbin-smoke.html --template executive
```

## CI/CD

GitHub Actions now validates the repo with:

| Workflow | Purpose |
| --- | --- |
| `ci.yml` | Windows build, CI-safe .NET coverage run, Node tests, coverage publishing, and Aspire AppHost smoke validation |
| `codeql.yml` | Static analysis for C# and JavaScript |
| `dependency-review.yml` | Pull request dependency risk review |
| `docs.yml` | Living-doc generation preview from the sample spec project |

The CI workflow publishes:

- TRX test results
- Cobertura + HTML coverage artifacts
- AppHost startup logs
- A sticky PR coverage summary

Desktop-display automation remains covered by the local Windows suite, but the hosted GitHub runner uses the CI-safe subset because FlaUI and full Studio E2E can be disrupted by the shared desktop session model on `windows-latest`.

## Sample spec project

`specs\httpbin-smoke` is the CI-friendly sample project used for documentation and end-to-end validation of the CLI, parser, and HTTP driver stack. See `specs\httpbin-smoke\README.md` for the project layout and flow details.
