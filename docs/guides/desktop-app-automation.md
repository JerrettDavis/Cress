# Desktop app automation

This walkthrough shows how to author a Windows desktop automation flow with Cress and FlaUI-backed steps.

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
  flaui:
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
flaui:
  applicationPath: C:\Path\To\YourDesktopApp.exe
  windowTitle: Cress FlaUI Test App
  launchTimeoutMs: 15000
```

## 3. Load the project in Studio and select the target flow

The desktop authoring flow looks the same in Studio, but the locators and target picker are desktop-specific.

![Desktop flow selected](../images/desktop/flow-selected.png)

## 4. Draft the interaction flow

The Studio sample project's desktop flow is a good baseline:

```yaml
when:
  - step: desktop.open
  - step: desktop.fill
    with:
      automationId: NameInput
      value: Grace Hopper
  - step: desktop.click
    with:
      automationId: ContinueButton
then:
  - expect: desktop.text
    with:
      automationId: GreetingLabel
      text: Hello Grace Hopper
  - expect: desktop.window_title
    with:
      title: Cress FlaUI Test App
```

For Windows desktop automation, prefer:

1. `automationId`
2. `name` plus `controlType`
3. `label`
4. `role`

## 5. Refine in Source and push back into the designer

Desktop teams often record once, then normalize heavily in source.

![Desktop source edited](../images/desktop/source-edited.png)

After applying the source back into the designer:

![Desktop designer updated](../images/desktop/designer-updated.png)

## 6. Run and inspect evidence

Once a run completes, Cress surfaces screenshots and generated reports alongside the flow result.

![Desktop run completed](../images/desktop/run-completed.png)

And you can drill into report output directly:

![Desktop report preview](../images/desktop/report-preview.png)

## 7. Practical guidance

- Use stable `AutomationId` values in your app whenever possible.
- Keep dialogs and startup flows deterministic.
- Prefer dedicated test accounts, fixtures, and launch switches over brittle “click through setup” flows.
- Treat the first recorded pass as scaffolding; the durable artifact is the reviewed YAML flow.
