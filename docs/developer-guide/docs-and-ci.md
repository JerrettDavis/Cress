# Docs and CI

The repository now uses DocFX as the primary documentation build, navigation, and publishing system.

## Local docs workflow

```powershell
dotnet tool restore
dotnet tool run docfx docs\docfx.json --output artifacts\docs-site
```

Use this after:

- changing navigation or table-of-contents files
- adding or moving conceptual docs
- changing public API surfaces that should appear in the generated reference

## Refresh the Studio screenshots from end-to-end tests

The reused Studio screenshots under `docs\images\studio\` are generated from the browser E2E flow so the docs stay aligned with the validated product surfaces.

```powershell
npm run test:e2e:docs
```

This docs-focused run:

1. enables `CRESS_CAPTURE_DOCS_SCREENSHOTS=1`
2. writes screenshot artifacts from Playwright into `artifacts\playwright\`
3. refreshes the reusable docs screenshots under `docs\images\studio\`

It refreshes screenshots such as the landing page, workspace picker, loaded workspace, flow designer, source tab, recording picker, results panel, and metrics tab directly from the same Playwright scenarios used to validate the documented Studio workflow.

For the regular browser suite, use:

```powershell
npm run test:e2e
```

That keeps Playwright failure screenshots, traces, and videos without rewriting the checked-in docs images.

## Site structure

- `docs\index.md` is the DocFX landing page
- `docs\toc.yml` is the primary navigation
- `docs\getting-started\`, `docs\user-guide\`, `docs\developer-guide\`, and `docs\api\` hold conceptual docs
- `docs\reference\api\` is the generated .NET API reference destination

## GitHub Actions publishing flow

The `docs.yml` workflow:

1. restores the local .NET tools, including DocFX
2. builds the solution so API metadata generation sees the current source tree
3. builds the DocFX site
4. uploads a preview artifact for pull requests and manual inspection
5. deploys to GitHub Pages from `main`

## Companion installer and release packaging

The repository now uses a dedicated packaging path for the Windows desktop companion:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-CompanionInstaller.ps1 -Version 0.1.0-local
```

That script:

1. publishes `src\Cress.Companion.Windows\Cress.Companion.Windows.csproj` as a self-contained `win-x64` app
2. creates a portable zip in `artifacts\packages\`
3. builds the WiX-based MSI installer in `installer\Cress.Companion.Installer\bin\Release\`

For CI and release automation:

1. `ci.yml` smoke-validates the companion publish + installer path on Windows
2. `release.yml` builds the companion MSI and portable zip on version tags and publishes them to GitHub Releases alongside the CLI package

## Coverage reporting in CI

The `ci.yml` workflow now publishes **two** coverage views:

1. a **full** Cobertura + HTML report for the entire CI-safe .NET test slice
2. a **core** report that excludes UI shells, Windows-only desktop automation layers, and a few integration-heavy adapters so the main cross-platform engine/export/import surface can be held to a stricter line-coverage bar

The core report is generated through `scripts\Generate-CoverageReport.ps1` and currently enforces a **90% minimum line-coverage gate** in CI.

Both reports are also uploaded to **Codecov** with separate flags:

- `full` for the broad CI-safe .NET report
- `core` for the stricter gated report

## What to update when behavior changes

If you add or change:

- CLI commands or options, update the [CLI reference](../api/cli-reference.md)
- project file conventions, update the [project schema guide](../api/project-schema.md)
- Studio flows or screenshots, update the relevant user guides **and the [feature matrix](../user-guide/feature-matrix.md)**
- desktop companion installation or release behavior, update the companion guide plus this packaging section
- repo structure or developer workflow, update this section and the root `README.md`

## Feature map and screenshot policy

The [feature map](../user-guide/feature-matrix.md) is the canonical inventory for user-visible features.

For **every feature change**:

1. update the matching row in the feature matrix
2. add a new row if the feature is new
3. refresh the screenshot if the UI changed materially
4. keep the linked guide current so the matrix never points at stale behavior

If the feature is primarily visual, do not consider the docs update done until the screenshot-backed matrix entry has been updated too.
