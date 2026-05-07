# Recording workflows

The recording flow is the closest thing to a wizard in Cress: you choose a target, capture the interaction, then refine the generated flow before trusting it in CI.

## Recording flow for web targets

1. Open the project in Studio or Studio Web.
2. Start recording and choose the browser target.
3. Perform the user journey.
4. Save the draft flow.
5. Open **Source** and normalize the output.
6. Run the flow and inspect the evidence.

![Web recording picker](../images/studio/web-recording-picker.png)

## Recording flow for desktop targets

1. Open the project.
2. Start recording and choose the desktop target.
3. Interact with the target window.
4. Save the draft.
5. Normalize the locators in source.
6. Re-run and inspect screenshots.

![Desktop recording picker](../images/studio/desktop-recording-picker.png)

## What to fix after recording

Treat the first recorded draft as scaffolding, not the final artifact.

Priorities after recording:

1. replace brittle selectors with `testId`, `role` + `label`, or `automationId`
2. add a meaningful `id`, `name`, and tags
3. connect the flow to its capability
4. move environment-specific values into profiles
5. keep the path deterministic enough for CI

## Review in source before you run again

![Source tab](../images/studio/source-tab.png)

This is where teams converge on the stable version of the flow that they want to store in source control.

## Validate with evidence

![Results panel](../images/studio/results-panel.png)

Run after every meaningful edit so the evidence stays close to the change that caused it.
