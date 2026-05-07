# Extensibility

Cress is designed to let teams extend the platform without hiding the execution model.

## Step and plugin discovery

A generated project starts with plugin discovery rooted at:

- `plugins`
- `steps`

That means a project can evolve from simple manifest-driven steps into richer plugin-backed execution without changing the overall project model.

## Common extension points

### Step manifests

Use manifests when you want to declare reusable steps and bind them to existing driver operations.

![Source tab](../images/studio/source-tab.png)

### Plugins

Use plugins when you need custom runtime behavior that is not already covered by the built-in drivers.

### Importers and exporters

The CLI already includes:

- `import gherkin`
- `import playwright`
- `import postman`
- `export gherkin`
- `export cypress`
- `export selenium-ide`

These commands are useful reference points when adding another interchange format.

The same extension points now also support generated .NET test exports, which makes the exporter layer a bridge between source-authored flows and framework-native suites.

![Results panel](../images/studio/results-panel.png)

## Helpful diagnostics while extending

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- doctor --json
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- discover drivers --json
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- discover steps --json
```

## Extensibility design rules

- keep business intent in flows, not in plugin internals
- prefer profile-driven config over hard-coded machine assumptions
- surface clear diagnostics instead of silent fallback behavior
- keep generated evidence rich enough to debug custom behavior in CI
