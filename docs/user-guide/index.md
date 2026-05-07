# User guide

The user guide focuses on the day-to-day authoring workflow: opening a project, recording and editing flows, running them, and understanding the evidence that comes back.

## Typical authoring loop

1. Open the project in Studio or Studio Web.
2. Record or draft a flow.
3. Normalize the flow YAML in source.
4. Validate and run the flow.
5. Review results, screenshots, and reports.
6. Commit the flow once it is deterministic.

## Guide map

- [Studio overview](studio-overview.md)
- [Recording workflows](recording-workflows.md)
- [Authoring flows](authoring-flows.md)
- [Running and debugging](running-and-debugging.md)
- [Testing CLI apps](testing-cli-apps.md)
- [Testing services](testing-services.md)
- [Testing web apps](testing-web-apps.md)
- [Testing desktop apps](testing-desktop-apps.md)
- [Framework integrations](../developer-guide/test-framework-integrations.md)
- [Framework demos and development-cycle integration](../developer-guide/test-framework-demos.md)

## Testing targets

These target-specific guides show how to apply Cress to realistic automation surfaces:

| Target | Best starting point | What the guide covers |
| --- | --- | --- |
| CLI apps | [Testing CLI apps](testing-cli-apps.md) | plugin-backed command execution, assertions, and evidence patterns |
| Services and APIs | [Testing services](testing-services.md) | HTTP-driver workflows, service smoke tests, JSON assertions, and CI use cases |
| Web apps | [Testing web apps](testing-web-apps.md) | Playwright-backed browser flows, Studio recording, locator strategy, and mixed UI/API testing |
| Desktop apps | [Testing desktop apps](testing-desktop-apps.md) | FlaUI-driven Windows automation, launch/attach patterns, locator strategy, and screenshot-heavy troubleshooting |

## Running flows inside xUnit, NUnit, and MSTest

You can now generate framework-native C# tests that call the Cress engine directly. That lets:

1. designers keep authoring in Studio and YAML
2. product teams commit generated tests into existing test projects
3. CI pipelines run Cress-authored scenarios beside the rest of the product suite

Start with the [framework integration guide](../developer-guide/test-framework-integrations.md).

## Where screenshots and wizard-style flows fit

The product uses recording pickers, source/designer surfaces, results panels, and metrics views as the key guided workflow surfaces. The pages in this section walk through those screens step by step with the repository screenshots.
