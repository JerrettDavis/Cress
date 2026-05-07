# Project schema guide

Cress projects are intentionally source-control friendly. The top-level structure makes it clear where environment settings, behavior definitions, fixtures, and implementation bindings live.

## Standard project layout

```text
.cress/
  config.yaml
  policy.yaml
  profiles/
capabilities/
flows/
models/
fixtures/
steps/
plugins/
artifacts/runs/
reports/
schemas/
```

The default initializer also creates starter capability and flow files so the project is immediately discoverable.

## Key files and folders

| Path | Role |
| --- | --- |
| `.cress\config.yaml` | project name, default profile, path layout, plugin discovery, runtime drivers |
| `.cress\policy.yaml` | validation and execution policy |
| `.cress\profiles\*.yaml` | environment-specific settings such as `baseUrl`, evidence mode, timeouts, or desktop launch config |
| `capabilities\*.md` | product-facing capability descriptions with YAML front matter |
| `flows\**\*.flow.yaml` | executable user or system journeys |
| `fixtures\*.yaml` | static or dynamic fixture declarations |
| `steps\*.yaml` | step manifests and implementation bindings |
| `plugins\` | plugin discovery root |
| `artifacts\runs\` | per-run evidence |
| `reports\` | generated HTML, JSON, JUnit, markdown, or living-doc outputs |

## Configuration highlights

### `.cress\config.yaml`

Important sections:

- `project`
- `paths`
- `defaults`
- `plugins.discover`
- `drivers`

### Profiles

Use profiles to separate:

- local developer settings
- CI settings
- environment-specific URLs or application paths
- evidence and timeout choices

### Capabilities

Capability files are markdown with YAML front matter. The markdown body is where teams keep:

- human-readable behavior descriptions
- rules
- acceptance criteria
- ownership and risk metadata

### Flows

Flows define the executable path with `when` and `then` steps, plus metadata like `id`, `name`, `capability`, and `tags`.

## Design rules

- keep environment data in profiles
- keep reusable business context in capabilities
- keep flows behavior-focused
- keep step implementation details in manifests or plugins
- keep artifacts and reports generated, not hand-edited
