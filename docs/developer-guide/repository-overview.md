# Repository overview

The Cress solution is organized around the project system, execution pipeline, authoring experiences, and supporting import/export or reporting packages.

## Main source projects

| Project | Purpose |
| --- | --- |
| `Cress.Core` | shared models and core types |
| `Cress.ProjectSystem` | project discovery, config loading, and catalog loading |
| `Cress.Specs` | parsing and normalization for flows, capabilities, fixtures, and manifests |
| `Cress.Validation` | project validation and diagnostics |
| `Cress.Execution` | runtime orchestration and driver execution |
| `Cress.Sdk` | shared SDK-facing helpers |
| `Cress.Gherkin` | Gherkin conversion and phrase support |
| `Cress.Importers` | Playwright, Postman, and other inbound format conversion |
| `Cress.Exporters` | outbound format generation such as Gherkin, Cypress, and Selenium IDE |
| `Cress.LivingDocs` | HTML living-document generation |
| `Cress.Recorder` | recording models, inference, and serialization |
| `Cress.Studio.Core` | shared services used by Studio surfaces |
| `Cress.Studio.Web` | Blazor web authoring experience |
| `Cress.Studio` | WPF desktop authoring experience |
| `Cress.Cli` | command-line entry point and command surface |
| `Cress.AppHost` | Aspire-based local orchestration for Studio and Studio Web |

## Test projects

The test suite is split by concern:

- unit and execution behavior
- import/export behavior
- living docs
- recorder inference
- Studio Web behavior
- desktop end-to-end coverage

## How the pieces fit together

1. `Cress.Cli` locates a project and loads configuration.
2. `Cress.Specs` and `Cress.ProjectSystem` build the project catalog.
3. `Cress.Validation` and `doctor` surface diagnostics.
4. `Cress.Execution` resolves flows into plans and runs them through the configured drivers.
5. `Cress.LivingDocs`, `report`, `metrics`, and `flake-report` turn raw execution output into artifacts that humans and CI systems can consume.

## Key repository folders

| Path | Purpose |
| --- | --- |
| `src\` | product code |
| `tests\` | automated verification |
| `specs\` | sample projects and onboarding material |
| `docs\` | DocFX site content, screenshots, and repo-browsable guides |
| `node\` | Node-based workspaces and browser-related tooling |
