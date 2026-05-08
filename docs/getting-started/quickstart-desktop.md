# Desktop quickstart

This path shows how to stand up a Windows desktop automation project with the Flawright runtime driver and the Studio recording flow.

## 1. Create a project

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- init demos\desktop-sample
```

## 2. Enable the Flawright driver

Update `.cress\config.yaml` so desktop execution is turned on:

```yaml
drivers:
  playwright:
    enabled: false
  http:
    enabled: false
  flawright:
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
flawright:
  applicationPath: C:\Path\To\YourDesktopApp.exe
  windowTitle: Cress Flawright Test App
  launchTimeoutMs: 15000
```

## 4. Open the project in Studio

The desktop authoring loop uses the same workspace layout as web automation, but the target picker and Flawright selector choices are desktop-specific.

![Desktop flow selected](../images/desktop/flow-selected.png)

## 5. Follow the desktop recording flow

1. Start the recorder.
2. Choose the desktop target from the picker.
3. Interact with the application window.
4. Save the draft flow and review the generated source.

![Desktop recording target picker](../images/studio/desktop-recording-picker.png)

## 6. Normalize desktop locators

Use this priority order:

1. `#AutomationId`
2. `name:Visible Name`
3. `role:Button`
4. `label:Field Label`

```yaml
when:
  - step: ui.fill
    with:
      selector: "#NameInput"
      value: Grace Hopper
  - step: ui.invoke
    with:
      selector: "name:Continue"
then:
  - expect: ui.assert-text
    with:
      selector: "#GreetingLabel"
      text: Hello Grace Hopper
```

After editing the source, apply it back into the designer if needed:

![Desktop source edited](../images/desktop/source-edited.png)

![Desktop designer updated](../images/desktop/designer-updated.png)

![Desktop flow designer](../images/studio/flow-designer.png)

## 7. Run and inspect evidence

```powershell
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- validate demos\desktop-sample
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- run demos\desktop-sample --profile local --report html,json
```

Review the run output:

![Desktop run completed](../images/desktop/run-completed.png)

![Desktop report preview](../images/desktop/report-preview.png)

![Desktop results panel](../images/studio/results-panel.png)

## 8. Make the flow durable

For desktop automation, the biggest reliability gains come from:

- stable `AutomationId` values surfaced through `#AutomationId` selectors
- deterministic launch and test data setup
- predictable dialogs and window titles
- screenshots and full evidence capture during early authoring
