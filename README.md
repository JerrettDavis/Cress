# Cress

[![CI](https://github.com/JerrettDavis/Cress/actions/workflows/ci.yml/badge.svg)](https://github.com/JerrettDavis/Cress/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JerrettDavis/Cress/actions/workflows/codeql.yml/badge.svg)](https://github.com/JerrettDavis/Cress/actions/workflows/codeql.yml)
[![Docs](https://github.com/JerrettDavis/Cress/actions/workflows/docs.yml/badge.svg)](https://github.com/JerrettDavis/Cress/actions/workflows/docs.yml)

Cress is a **.NET 10 / C# latest** end-to-end testing platform for Windows, with a **Blazor-first Studio experience**, an optional **desktop companion** for anchored in-app recording overlays, Node-based automation components, and an Aspire AppHost for centralized orchestration and monitoring.

## Guides and examples

See [docs/README.md](docs/README.md) and the published DocFX site at <https://jerrettdavis.github.io/Cress/> for:

- step-by-step web app automation walkthroughs
- step-by-step desktop app automation walkthroughs
- Studio screenshots and user guides
- getting-started, user, developer, and API guides
- integration guidance for bringing Cress into your systems
- design guidance for making your apps easier to automate with Cress

## Requirements

- .NET SDK `10.0.107` or later in the .NET 10 feature band
- Node.js `22.x`
- Windows for desktop/Flawright execution

## Restore and build

```powershell
dotnet tool restore
npm ci
dotnet restore Cress.sln
dotnet build Cress.sln --configuration Release --no-restore
```

## Fastest first run

If you want the quickest proof that Cress can run a real flow, pack the CLI as a local dotnet tool and run the built-in HTTP sample:

```powershell
dotnet pack src\Cress.Cli\Cress.Cli.csproj --configuration Release --output artifacts\packages
dotnet tool install --tool-path .\.tools\cress --add-source artifacts\packages Cress.Cli
Set-Location specs\httpbin-smoke
..\..\.tools\cress\cress validate
..\..\.tools\cress\cress run flows\httpbin\get-smoke.flow.yaml --report html,json,junit
```

For the reusable CI path, see the GitHub Actions guide in `docs\developer-guide\github-actions-integration.md`.

## Run with Aspire orchestration

Start the centralized AppHost to launch the web app, wire in service defaults, and coordinate local monitoring:

```powershell
dotnet run --project src\Cress.AppHost\Cress.AppHost.csproj --configuration Release --launch-profile http
```

The AppHost orchestrates:

- `Cress.Studio.Web` as an Aspire project resource

The optional desktop companion runs separately when you want titlebar-adjacent recording overlays and multi-app desktop monitoring:

```powershell
dotnet run --project src\Cress.Companion.Windows\Cress.Companion.Windows.csproj --configuration Release
```

To build the Windows release assets for the companion locally, use:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-CompanionInstaller.ps1 -Version 0.1.0-local
```

That produces:

- a self-contained Windows x64 portable zip in `artifacts\packages\`
- a Windows MSI installer in `installer\Cress.Companion.Installer\bin\Release\`

## Installable Studio distributions

The Studio now ships as a **single Windows bundle** that supports both launch styles:

- **wrapped desktop shell** through `Cress.Studio.Windows.exe`
- **browser-hosted shell** through the same local bundle with `--browser`

Build the Studio release assets locally with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-StudioInstaller.ps1 -Version 0.1.0-local
```

That produces:

- `artifacts\packages\Cress.Studio-win-x64-<version>.zip` with both desktop and browser launchers
- `installer\Cress.Studio.Installer\bin\Release\Cress.Studio.Setup-<version>.msi`
- `artifacts\packages\Cress.Studio.Tool.<version>.nupkg` for the Windows `cress-studio` dotnet tool

The MSI installs Start Menu entries for **Cress Studio** and **Cress Studio (Browser)**. The portable zip includes matching `.cmd` launchers beside the bundled executables.

For the Studio dotnet tool path:

```powershell
dotnet tool install --tool-path .\.tools\studio --prerelease --add-source artifacts\packages Cress.Studio.Tool
.\.tools\studio\cress-studio --browser
```

The AppHost browser launch opens the Aspire dashboard first. Open the `studio-web` resource endpoint to see the Studio shell itself. When you run `Cress.Studio.Web` directly, `/` stays on the landing and onboarding view, while `/workspace` jumps straight to workspace setup.

## Validation

```powershell
# Full local .NET test suite, including desktop and end-to-end coverage on Windows
dotnet test Cress.sln --configuration Release --no-build

# Aspire AppHost composition, integration, and end-to-end coverage
dotnet test tests\Cress.AppHost.Tests\Cress.AppHost.Tests.csproj --configuration Release

# Node test suite
node --test node/tests/*.test.mjs

# Browser E2E suite with failure screenshots, traces, and videos
npm run test:e2e

# Browser E2E docs refresh that also rewrites docs\images\studio screenshots
npm run test:e2e:docs

# DocFX site build
dotnet tool run docfx docs\docfx.json --output artifacts\docs-site

# Living docs preview
dotnet run --project src\Cress.Cli\Cress.Cli.csproj --configuration Release -- doc generate specs\httpbin-smoke --output artifacts\docs\httpbin-smoke.html --template executive
```

## CI/CD

GitHub Actions now validates the repo with:

| Workflow | Purpose |
| --- | --- |
| `ci.yml` | Windows build, CI-safe .NET coverage run, Node tests, coverage publishing, and Aspire AppHost smoke validation |
| `codeql.yml` | Static analysis for C# and JavaScript |
| `conventional-commits.yml` | Enforces Conventional Commit pull request titles plus commit headlines on pull requests and direct pushes to `main` |
| `dependency-review.yml` | Pull request dependency risk review |
| `docs.yml` | DocFX site build, preview artifact publishing, and GitHub Pages deployment |
| `release.yml` | Builds the CLI package plus the companion and Studio portable zip/MSI/tool assets on version tags |

The CI workflows publish:

- TRX test results
- full Cobertura + HTML coverage artifacts
- a filtered core-coverage artifact and 90% line-coverage gate for the cross-platform engine/export/import surface
- Codecov uploads for both the full report and the gated core report
- AppHost startup logs
- desktop companion portable zip and MSI preview artifacts
- Studio portable zip, MSI installer, and `cress-studio` tool preview artifacts
- DocFX site preview artifacts
- a sticky PR summary with both the full report and the gated core report

For repository protection, mark **PR title** and **PR commits** from `conventional-commits.yml` as required status checks on `main`. The same workflow also validates direct pushes to `main` with the **Push commits** check.

Desktop-display automation remains covered by the local Windows suite, but the hosted GitHub runner uses the CI-safe subset because Flawright and full Studio E2E can be disrupted by the shared desktop session model on `windows-latest`.

## Sample spec project

`specs\httpbin-smoke` is the CI-friendly sample project used for documentation and end-to-end validation of the CLI, parser, and HTTP driver stack. See `specs\httpbin-smoke\README.md` for the project layout and flow details.
