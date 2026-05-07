# Testing web apps

Web app testing in Cress combines **flow YAML**, **Playwright-backed runtime execution**, and **Studio / Studio Web** authoring so teams can move from recording to durable browser automation without losing product intent.

## Recommended approach

Use this stack for web applications:

1. the built-in **Playwright** runtime driver for browser execution
2. Studio or Studio Web for recording and refinement
3. flow YAML for durable source-controlled authoring
4. HTTP steps when the scenario also needs service-level verification

This works especially well for:

- sign-in and onboarding journeys
- dashboard and reporting flows
- admin and back-office applications
- B2B portals with deterministic test accounts
- smoke and regression suites for CI

## Tooling

| Layer | Recommendation |
| --- | --- |
| Runtime driver | `playwright` |
| Authoring surfaces | Studio, Studio Web, Source view |
| Sample project | `specs\web-smoke` |
| Diagnostics | `validate`, `doctor`, `discover`, `run --dry-run` |
| Evidence | screenshots, traces, reports, results panel |

## Getting started

### 1. Create a project

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- init demos\web-portal
```

### 2. Enable browser support

```yaml
drivers:
  playwright:
    enabled: true
  http:
    enabled: true
  flaui:
    enabled: false
```

Keep `http` enabled if you want setup or verification calls around the UI journey.

### 3. Configure the profile

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

### 4. Launch Studio

```powershell
dotnet run --project src\Cress.AppHost\Cress.AppHost.csproj --configuration Release --launch-profile http
```

### 5. Record and normalize

1. open the project in Studio or Studio Web
2. start recording and choose the browser target
3. perform the user journey
4. save the draft flow
5. switch to **Source** and clean up the locators

![Web recording picker](../images/studio/web-recording-picker.png)

![Source tab](../images/studio/source-tab.png)

## Locator strategy

Prefer locators in this order:

1. `testId`
2. `role` + `label`
3. `role`
4. `label`
5. `text`
6. `cssSelector`
7. `xpath`

## Realistic example

Use case: an operations portal where a user signs in, searches for a report, and opens a detail page.

```yaml
version: 1
id: operations.report-search
name: Operations user can find a quarterly report
capability: reporting-search
tags:
  - web
  - smoke
  - reporting

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
  - step: ui.fill
    with:
      testId: search-input
      value: quarterly report
  - step: ui.press-key
    with:
      key: Enter
then:
  - expect: ui.assert-text
    with:
      testId: report-heading
      text: Quarterly Report Q1 2026
```

## Step-by-step: smoke-test a local web app

This is the fastest path for teams that already have a dev server and want a realistic browser flow before recording a larger regression suite.

### Goal

Open a local app, sign in, search, and verify that the results page loaded.

### 1. Start the app

Run the application locally so it is reachable at the `baseUrl` in your profile, for example `http://localhost:3000`.

### 2. Configure the project profile

```yaml
profile: local
baseUrl: http://localhost:3000
drivers:
  playwright:
    enabled: true
  http:
    enabled: true
```

### 3. Record the happy path

1. open the project in Studio or Studio Web
2. choose the browser target
3. sign in with a deterministic test account
4. search for a known record
5. stop recording and save the draft flow

### 4. Normalize the generated source

Move from brittle recorded selectors to durable ones:

1. prefer `data-testid`
2. use `role` and `label` when test ids are not available
3. keep hardcoded URLs and credentials out of the flow

### 5. Run the smoke suite

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate demos\web-portal
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run demos\web-portal --profile local --report html,json
```

### 6. Extend it into a mixed UI and service flow

After the first browser test passes:

1. seed data through `http.post`
2. verify it in the browser
3. confirm the final server state with `http.get`
4. export JUnit or generated test classes for CI integration

## Good use cases

### Business smoke tests

Keep a small suite of high-confidence flows for:

- sign-in
- primary search
- primary creation/edit path
- a single high-value report or checkout path

### Regression flows

Add deeper variations once the smoke suite is stable:

- role-based access differences
- error handling
- multi-step wizards
- export/download flows

### Hybrid UI + service checks

Use HTTP steps before or after browser actions when you need reliable data setup or verification.

## Practical command loop

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate demos\web-portal
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run demos\web-portal --profile local --report html,json
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run demos\web-portal --tag smoke --profile ci --report html,json,junit
```

## Design guidance

- add `data-testid` attributes to high-value workflow surfaces
- make authentication and seed data deterministic
- keep flow values profile-driven instead of hardcoding environment details
- publish reports and screenshots from CI so failures can be triaged asynchronously
