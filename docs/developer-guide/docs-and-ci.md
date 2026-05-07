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

## What to update when behavior changes

If you add or change:

- CLI commands or options, update the [CLI reference](../api/cli-reference.md)
- project file conventions, update the [project schema guide](../api/project-schema.md)
- Studio flows or screenshots, update the relevant user guides
- repo structure or developer workflow, update this section and the root `README.md`
