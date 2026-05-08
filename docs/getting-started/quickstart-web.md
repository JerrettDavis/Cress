# Web quickstart

This path shows how a team moves from a blank project to a browser flow that can be run locally and later promoted into CI.

## 1. Create a project

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- init demos\web-shop
```

The initializer creates:

- `.cress\config.yaml`
- `.cress\profiles\local.yaml`
- `.cress\profiles\ci.yaml`
- `capabilities\`
- `flows\`
- `fixtures\`
- `steps\`
- `artifacts\runs\`
- `reports\`

## 2. Enable the right runtime drivers

For a web project, the usual starting point is:

```yaml
drivers:
  playwright:
    enabled: true
  http:
    enabled: true
  flawright:
    enabled: false
```

Keep browser and HTTP support together when the flow needs both UI actions and direct service assertions.

## 3. Configure the local profile

Set the target system base URL and any environment variables you need:

```yaml
profile: local
baseUrl: http://localhost:3000
timeouts:
  step: 30000
  expectation: 10000
evidence:
  mode: standard
variables:
  environment: local
```

## 4. Launch the authoring environment

```powershell
dotnet run --project src\Cress.AppHost\Cress.AppHost.csproj --configuration Release --launch-profile http
```

The AppHost orchestrates the Studio desktop app and Studio Web so you can use the same workspace while authoring.

![Cress Studio project view](../images/studio/project-loaded.png)

## 5. Walk through the browser recording flow

1. Open the project in Studio or Studio Web.
2. Start recording and choose the browser target from the recording picker.
3. Perform the user journey in the browser.
4. Save the generated flow, then switch to **Source** to normalize it.

![Web recording target picker](../images/studio/web-recording-picker.png)

![Source editing tab](../images/studio/source-tab.png)

## 6. Normalize the YAML before committing it

Prefer these locator strategies in order:

1. `testId`
2. `role` + `label`
3. `role`
4. `text`
5. `cssSelector` or `xpath` only as a last resort

Also add:

- a stable flow ID and business-facing name
- capability links
- tags for pipeline selection
- profile-driven values instead of environment-specific literals

## 7. Validate and run

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate demos\web-shop
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run demos\web-shop --profile local --report html,json
```

Use the results panel to inspect evidence:

![Results panel](../images/studio/results-panel.png)

## 8. Promote the flow into CI

Before moving to CI:

- create a `ci` profile with stable URLs and secrets strategy
- make sure the flow can pass without interactive setup
- publish the generated reports and screenshots as pipeline artifacts

The [running and debugging guide](../user-guide/running-and-debugging.md) and [Docs and CI](../developer-guide/docs-and-ci.md) pages show the next layer of commands and workflow automation.
