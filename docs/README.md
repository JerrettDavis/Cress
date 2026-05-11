# Cress documentation

The repository now includes a **DocFX-powered documentation site** with:

- getting-started guides
- user guides with screenshots and step-by-step flows
- developer guides for local setup, extensibility, and CI
- API guides and generated .NET reference

## Published site

- GitHub Pages: <https://jerrettdavis.github.io/Cress/>
- DocFX config: `docs\docfx.json`

## Start here

| Section | What it covers |
| --- | --- |
| [Getting started](getting-started/index.md) | prerequisites, onboarding path selection, a fastest first-run path, and target-specific quickstarts |
| [User guide](user-guide/index.md) | Studio walkthroughs with `Cress.Studio.Web` as the primary shell, a screenshot-backed feature map, recording flows, authoring guidance, debugging, target-specific guides for CLI, service, web, and desktop testing, and step-by-step familiar-app walkthroughs |
| [Developer guide](developer-guide/index.md) | repository structure, local development, extensibility, native xUnit/NUnit/MSTest integration, framework demos, GitHub Actions integration, development-cycle adoption guidance, and docs/CI |
| [API guide](api/index.md) | CLI command reference, project schema, and generated API reference |

## Built-in demo material

These repo examples back the guides and are good starting points for your own projects:

| Example | Focus |
| --- | --- |
| `specs\httpbin-smoke` | HTTP-only project, validation, reporting, and living docs |
| `specs\web-smoke` | browser flow structure, locator strategy, and Playwright-backed steps |
| `specs\calc-smoke` | desktop/Flawright project structure and profile shape |

## Legacy repo-browsing guides

The original markdown walkthroughs remain available in `docs\guides\` for direct repo browsing:

- [Studio workflow](guides/studio-workflow.md)
- [Web app automation](guides/web-app-automation.md)
- [Desktop app automation](guides/desktop-app-automation.md)
- [Integrating with your system](guides/integrating-with-your-system.md)
- [Designing for Cress](guides/designing-for-cress.md)
