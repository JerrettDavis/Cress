# Cress

[![CI](https://github.com/JerrettDavis/Cress/actions/workflows/ci.yml/badge.svg)](https://github.com/JerrettDavis/Cress/actions/workflows/ci.yml)

Cress is a .NET 8 / C# test automation framework with a WPF Studio IDE and a Node.js plugin host, targeting Windows.

## Build

```
dotnet restore Cress.sln
dotnet build Cress.sln --configuration Release --no-restore
```

## Test

```
# .NET tests (CI-safe subset — excludes FlaUI and E2E tests that require a live display)
dotnet test tests/Cress.UnitTests/Cress.UnitTests.csproj --configuration Release --filter "FullyQualifiedName!~FlaUiRuntimeDriverTests"
dotnet test tests/Cress.Studio.Web.Tests/Cress.Studio.Web.Tests.csproj --configuration Release

# Full local run (requires running WPF Studio + FlaUI test app)
dotnet test Cress.sln --configuration Release

# Node tests
node --test node/tests/*.test.mjs
```

## CI

CI runs on `windows-latest` (required for WPF/Windows targets). The following are excluded from CI and must be run locally:

- `FlaUiRuntimeDriverTests` — requires a live FlaUI automation target
- `Cress.Studio.E2ETests` — requires the WPF Studio application and a Windows display session
