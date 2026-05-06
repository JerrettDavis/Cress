# Studio workflow

This guide shows the main workflow inside Cress Studio and Cress Studio Web: load a project, inspect flows, edit source, run automation, and review evidence.

## Launch the orchestrated environment

```powershell
dotnet run --project src\Cress.AppHost\Cress.AppHost.csproj --configuration Release --launch-profile http
```

The AppHost starts the web experience, wires service defaults, and gives you one place to watch the local environment.

## 1. Start in the Studio landing view

The landing view is the fastest way to orient a new user to the workspace.

![Studio landing](../images/studio/landing.png)

## 2. Load a project and inspect the workspace

Once a project is open, Cress shows the explorer, summary, and the currently selected flow context.

![Project loaded](../images/studio/project-loaded.png)

At this point you can:

1. browse capabilities, flows, fixtures, and run history
2. inspect flow metadata and coverage context
3. jump between designer, source, results, and metrics

## 3. Review or refine the generated source

The source view is where teams normalize recorded output into maintainable, reviewable flow YAML.

![Source tab](../images/studio/source-tab.png)

Typical edits here:

- rename draft flows into business-facing names
- replace fragile locators with `testId`, `role` + `label`, or `automationId`
- add tags, capability links, and summary text
- move from one-off recorded values to profile variables

## 4. Run the flow and inspect evidence

The results panel shows run outcomes, artifacts, reports, and previews so authors can iterate quickly.

![Results panel](../images/studio/results-panel.png)

Use this surface to answer:

- did the flow pass?
- which step failed?
- what screenshot, JSON, trace, or report explains the result?

## 5. Use the target picker for recording

For browser recording:

![Web recording target picker](../images/studio/web-recording-picker.png)

For desktop recording:

![Desktop recording target picker](../images/studio/desktop-recording-picker.png)

The picker is where authors decide whether they are targeting a web session or a Windows desktop application.

## 6. Watch quality trends in metrics

The metrics tab helps teams move from “can we automate this?” to “is this automation healthy over time?”

![Metrics tab](../images/studio/metrics-tab.png)

Use it to spot:

- repeated failures
- flake-prone flows
- capability gaps
- evidence patterns worth turning into quick actions or templates

## Recommended authoring loop

1. Open the project in Studio or Studio Web.
2. Record or draft the first scenario.
3. Move to Source and normalize the flow YAML.
4. Run the flow locally.
5. Review evidence and update the flow or app contracts.
6. Commit the project and wire it into CI.
