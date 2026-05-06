# Web app automation

This walkthrough shows a practical browser workflow: create a Cress project, point it at your app, record or author a flow, run it, and integrate it into CI.

## 1. Create a project

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- init demos\web-shop
```

That creates the expected Cress layout:

- `.cress\config.yaml`
- `.cress\profiles\`
- `capabilities\`
- `flows\`
- `fixtures\`
- `steps\`
- `artifacts\runs\`
- `reports\`

## 2. Configure the local profile

For web projects, the main thing to establish first is the `baseUrl` and your browser/runtime defaults.

`specs\web-smoke\.cress\profiles\local.yaml` is a good starting reference:

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

## 3. Start Studio and load the project

Launch the orchestrated environment:

```powershell
dotnet run --project src\Cress.AppHost\Cress.AppHost.csproj --configuration Release --launch-profile http
```

Then open the project in Studio Web or the desktop Studio.

![Project loaded](../images/studio/project-loaded.png)

## 4. Record or author the first browser flow

When recording against a web target, the browser target picker helps you attach the recorder to the right runtime surface.

![Web recording picker](../images/studio/web-recording-picker.png)

The `specs\web-smoke\flows\example.flow.yaml` sample shows the kind of flow you want to converge toward:

```yaml
when:
  - step: browser.navigate
    with:
      url: http://localhost:3000/login
  - step: ui.fill
    with:
      testId: email-input
      value: user@example.com
  - step: ui.fill
    with:
      label: Password
      value: s3cr3t
  - step: ui.invoke
    with:
      role: button
      label: Sign in
then:
  - expect: ui.assert-text
    with:
      testId: report-heading
      text: Quarterly Report Q1 2026
```

## 5. Normalize the recorded flow

After recording, switch to Source and make the flow production-ready.

![Source tab](../images/studio/source-tab.png)

Priorities:

1. prefer `testId` when your app exposes it
2. otherwise prefer semantic locators like `role` + `label`
3. keep `cssSelector` and `xpath` as fallbacks, not the default design
4. tag the flow so it can be selected in CI
5. link it to a capability and acceptance criteria

## 6. Validate and run

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate demos\web-shop
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run demos\web-shop --profile local
```

Use the results view to inspect screenshots, traces, and reports.

![Results panel](../images/studio/results-panel.png)

## 7. Integrate the project into CI

Recommended pattern:

1. keep a `local` profile for developer machines
2. add a `ci` profile with CI base URLs, timeouts, and credentials strategy
3. run `validate` before `run`
4. publish Cress reports and screenshots as CI artifacts
5. keep browser locators deterministic so failures point to real regressions, not DOM noise

For a CI-friendly docs/reporting example, see `specs\httpbin-smoke` and the repo's `docs.yml` workflow.
