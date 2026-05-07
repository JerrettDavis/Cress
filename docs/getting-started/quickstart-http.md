# HTTP quickstart

The HTTP sample is the fastest way to prove your environment and understand the Cress project model because it only needs the built-in HTTP runtime driver.

> [!TIP]
> Use this path first even if your final goal is web or desktop automation. It gives you a clean baseline for project structure, profiles, flows, and reports.

## 1. Restore the repo

```powershell
dotnet tool restore
dotnet restore Cress.sln
dotnet build Cress.sln --configuration Release
```

## 2. Inspect the sample project

The repository includes `specs\httpbin-smoke`, a CI-friendly sample that shows the full project layout:

- `.cress\config.yaml` and `.cress\profiles\`
- `capabilities\`
- `flows\`
- `fixtures\`
- `steps\`
- `artifacts\runs\`
- `reports\`

For the full file-by-file tour, open `specs\httpbin-smoke\README.md` in the repository.

## 3. Validate before you run

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate specs\httpbin-smoke
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- doctor
```

`validate` checks the project files and manifests. `doctor` confirms Cress can locate the project and gives environment-readiness diagnostics.

## 4. Run the flows

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run specs\httpbin-smoke --profile ci --report html,json,junit
```

Expected result:

- a run ID in the console
- an artifact path under `artifacts\runs`
- generated reports under `reports\`

## 5. Review the generated living documentation

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- doc generate specs\httpbin-smoke --output artifacts\docs\httpbin-smoke.html --template executive
```

This produces a shareable HTML page that combines the project structure, capability context, and selected flow detail into a single artifact.

## 6. Explore the project catalog

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- discover --json
```

The discovery command is useful during onboarding because it shows the flows, capabilities, fixtures, steps, and runtime drivers that Cress found in the project.

## 7. What to try next

1. Change a profile value in `specs\httpbin-smoke\.cress\profiles\ci.yaml`.
2. Run `cress plan --json` or `cress run --dry-run` to inspect the execution plan.
3. Move to the [web quickstart](quickstart-web.md) or [desktop quickstart](quickstart-desktop.md) once the project model feels familiar.
