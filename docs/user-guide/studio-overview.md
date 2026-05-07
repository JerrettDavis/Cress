# Studio overview

Studio and Studio Web are the fastest way to understand the workspace because they put the project explorer, flow editor, evidence panels, and metrics in one place.

## Launch the environment

```powershell
dotnet run --project src\Cress.AppHost\Cress.AppHost.csproj --configuration Release --launch-profile http
```

## 1. Start at the landing page

The landing page is the orientation point for new users.

![Studio landing page](../images/studio/landing.png)

## 2. Load a project

Once loaded, the project view gives you the explorer, selected flow context, and the main authoring surfaces.

![Project loaded in Studio](../images/studio/project-loaded.png)

At this stage you can:

- browse capabilities, flows, fixtures, and run history
- switch between designer, source, results, and metrics
- confirm that the project structure loaded correctly

## 3. Use Source for durable edits

Recorded flows become maintainable when you normalize them in the source editor.

![Source tab](../images/studio/source-tab.png)

Typical edits:

- replace fragile locators with stable contracts
- rename draft flows into business-facing names
- add tags, capabilities, and acceptance criteria links
- move environment-specific data into profiles and variables

## 4. Inspect evidence in Results

The results view is where authors answer the two most important questions: **what failed** and **why**.

![Results panel](../images/studio/results-panel.png)

Use it to review:

- screenshots
- generated HTML or JSON reports
- traces and run artifacts
- per-step outcomes

## 5. Watch health in Metrics

The metrics view helps teams shift from one-off runs to sustainable automation quality.

![Metrics tab](../images/studio/metrics-tab.png)

Use it to spot:

- repeated failures
- flaky flows
- capability coverage gaps
- candidates for cleanup or shared templates
