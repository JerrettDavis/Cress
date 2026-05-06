# Cress guides and examples

This doc set shows how to use **Cress Studio**, **Cress Studio Web**, the **CLI**, and the **sample spec projects** to design, author, run, and integrate end-to-end automation.

## Start here

| Guide | What it covers |
| --- | --- |
| [Studio workflow](guides/studio-workflow.md) | Tour the main authoring surfaces, screenshots, and day-to-day workflow |
| [Web app automation](guides/web-app-automation.md) | Step-by-step workflow for recording and refining browser automation |
| [Desktop app automation](guides/desktop-app-automation.md) | Step-by-step workflow for authoring FlaUI-backed Windows desktop automation |
| [Integrating with your system](guides/integrating-with-your-system.md) | How to connect Cress to your environments, CI, and reporting flow |
| [Designing for Cress](guides/designing-for-cress.md) | How to make your web and desktop apps easier to automate reliably |

## Built-in demo material

These repo examples back the guides and are good starting points for your own projects:

| Example | Focus |
| --- | --- |
| `specs\httpbin-smoke` | HTTP-only project, validation, reporting, living docs |
| `specs\web-smoke` | Browser flow structure, locator strategy, Playwright-backed steps |
| `specs\calc-smoke` | Desktop/FlaUI project structure and profile shape |
| `tests\Cress.Studio.E2ETests\Fixtures\StudioSampleProject` | End-to-end Studio sample project used in the desktop walkthrough |

## Recommended learning path

1. Read the [Studio workflow](guides/studio-workflow.md) to understand the surfaces.
2. Follow either the [web](guides/web-app-automation.md) or [desktop](guides/desktop-app-automation.md) guide.
3. Use [Integrating with your system](guides/integrating-with-your-system.md) to wire the project into your delivery pipeline.
4. Share [Designing for Cress](guides/designing-for-cress.md) with the teams building the systems you want to automate.
