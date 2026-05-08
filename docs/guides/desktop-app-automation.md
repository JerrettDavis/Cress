# Desktop app automation

This walkthrough shows how to author a Windows desktop automation flow with Cress and Flawright-backed steps.

## 1. Create a desktop-focused project

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- init demos\desktop-sample
```

Then enable the desktop driver in `.cress\config.yaml`.

The repo's `specs\calc-smoke\.cress\config.yaml` shows the expected shape:

```yaml
drivers:
  http:
    enabled: false
  playwright:
    enabled: false
  flawright:
    enabled: true
```

## 2. Configure the desktop profile

Desktop projects usually need an application path and an expected window title.

The Studio sample fixture uses:

```yaml
profile: local
timeouts:
  driver: 15000
evidence:
  mode: full
  screenshots: true
flawright:
  applicationPath: C:\Path\To\YourDesktopApp.exe
  windowTitle: Cress Flawright Test App
  launchTimeoutMs: 15000
```

## 3. Load the project in Studio and select the target flow

The desktop authoring flow looks the same in Studio, but the locators and target picker are desktop-specific.

![Desktop flow selected](../images/desktop/flow-selected.png)

## 4. Draft the interaction flow

The calculator smoke flow is a good baseline:

```yaml
when:
  - step: desktop.open
  - step: desktop.fill
    with:
      selector: "#NameInput"
      value: Grace Hopper
  - step: desktop.click
    with:
      selector: "name:Continue"
then:
  - expect: desktop.text
    with:
      selector: "#GreetingLabel"
      text: Hello Grace Hopper
  - expect: desktop.window_title
    with:
      title: Cress Flawright Test App
```

For Windows desktop automation, prefer:

1. `#AutomationId`
2. `name:Visible Name`
3. `role:Button`
4. `label:Field Label`

The same selector model maps cleanly into natural Gherkin:

```gherkin
Given the user launches the `Cress.Flawright.TestApp.exe` application
When the user fills the `#NameInput` element with `Grace Hopper`
And the user clicks the `name:Continue` element
Then the `#GreetingLabel` element shows `Hello Grace Hopper`
And the window title is `Cress Flawright Test App`
```

## 5. Refine in Source and push back into the designer

Desktop teams often record once, then normalize heavily in source.

![Desktop source edited](../images/desktop/source-edited.png)

After applying the source back into the designer:

![Desktop designer updated](../images/desktop/designer-updated.png)

## 6. Run and inspect evidence

Once a run completes, Cress surfaces screenshots and generated reports alongside the flow result.

![Desktop run completed](../images/desktop/run-completed.png)

![Desktop results panel](../images/studio/results-panel.png)

And you can drill into report output directly:

![Desktop report preview](../images/desktop/report-preview.png)

## 7. Practical guidance

- Use stable `AutomationId` values in your app whenever possible and expose them through `#AutomationId` selectors.
- Keep dialogs and startup flows deterministic.
- Prefer dedicated test accounts, fixtures, and launch switches over brittle “click through setup” flows.
- Treat the first recorded pass as scaffolding; the durable artifact is the reviewed YAML flow.
