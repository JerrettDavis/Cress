# Integrating with your system

This guide explains how teams move from a demo project to real end-to-end coverage against their own environments and delivery pipeline.

## Project model

Cress works best when you treat each project as a thin automation boundary around a real system:

1. **profiles** describe where and how the system runs
2. **flows** describe user or system behavior
3. **capabilities** explain why the automation exists
4. **fixtures** and **variables** control data and environment assumptions

## Recommended environment split

Use at least these profiles:

- `local` for developer laptops and manual debugging
- `ci` for repeatable pipeline execution
- environment-specific profiles like `qa`, `staging`, or `preprod` when needed

For web systems, keep `baseUrl` in the profile. For desktop systems, keep `applicationPath`, `windowTitle`, and launch timing there.

## Suggested delivery flow

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate <project>
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run <project> --profile ci
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- doc generate <project> --output artifacts\docs\<project>.html
```

## What to publish from CI

Your CI system should preserve:

- screenshots
- traces and browser artifacts
- JSON/HTML/JUnit reports
- living-doc previews
- coverage summaries from the repo test suite

The repo workflows already demonstrate this pattern with:

- `ci.yml`
- `docs.yml`
- generated artifacts for coverage and docs preview

## Integrating with app teams

The fastest adoption pattern is:

1. app team exposes stable locators and test hooks
2. QA or platform authors the first flow
3. both teams review the generated YAML
4. the flow is promoted into CI only after it is deterministic

## Where to start

- Web teams should start with `specs\web-smoke`
- Desktop teams should start with `specs\calc-smoke`
- HTTP/service teams should start with `specs\httpbin-smoke`
