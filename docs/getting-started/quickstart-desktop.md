# Desktop quickstart

This path shows how to stand up a Windows desktop automation project with the FlaUI runtime driver and the Studio recording flow.

## 1. Create a project

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- init demos\desktop-sample
```

## 2. Enable the FlaUI driver

Update `.cress\config.yaml` so desktop execution is turned on:

```yaml
drivers:
  playwright:
    enabled: false
  http:
    enabled: false
  flaui:
    enabled: true
```

## 3. Configure the desktop profile

Desktop projects usually need the app path, a predictable window title, and launch timing:

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

## 4. Open the project in Studio

The desktop authoring loop uses the same workspace layout as web automation, but the target picker and locator choices are desktop-specific.

![Desktop flow selected](../images/desktop/flow-selected.png)

## 5. Follow the desktop recording flow

1. Start the recorder.
2. Choose the desktop target from the picker.
3. Interact with the application window.
4. Save the draft flow and review the generated source.

![Desktop recording target picker](../images/studio/desktop-recording-picker.png)

## 6. Normalize desktop locators

Use this priority order:

1. `automationId`
2. `name` + `controlType`
3. `label`
4. `role`

After editing the source, apply it back into the designer if needed:

![Desktop source edited](../images/desktop/source-edited.png)

![Desktop designer updated](../images/desktop/designer-updated.png)

## 7. Run and inspect evidence

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate demos\desktop-sample
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run demos\desktop-sample --profile local --report html,json
```

Review the run output:

![Desktop run completed](../images/desktop/run-completed.png)

![Desktop report preview](../images/desktop/report-preview.png)

## 8. Make the flow durable

For desktop automation, the biggest reliability gains come from:

- stable `AutomationId` values in the app
- deterministic launch and test data setup
- predictable dialogs and window titles
- screenshots and full evidence capture during early authoring
