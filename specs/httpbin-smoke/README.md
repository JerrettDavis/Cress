# httpbin-smoke

A self-contained Cress sample project that exercises the **HTTP driver only**, using the
public [httpbin.org](https://httpbin.org) echo service as the system under test.

It is CI-friendly (no desktop or browser driver required) and serves two purposes:

1. **Canary** — confirms the Cress CLI, parser stack, and HTTP driver all work end-to-end.
2. **Onboarding reference** — shows the complete project layout and every YAML schema feature
   needed to author a real spec project.

## What It Tests

| Flow | Description |
|------|-------------|
| `httpbin-get-smoke` | GET `/get` → assert status 200, assert `url` JSON field |
| `httpbin-post-smoke` | POST `/post` with JSON body → assert status 200, assert echoed `json.probe` field |

Both flows are tagged `smoke` and traced to the `http-connectivity` capability.

## Project Layout

```
httpbin-smoke/
├── .cress/
│   ├── config.yaml          # Project config — drivers, paths, defaults
│   └── profiles/
│       └── ci.yaml          # Profile: baseUrl + timeouts for CI
├── capabilities/
│   └── http-connectivity.md # Capability definition (markdown + YAML front matter)
├── fixtures/
│   └── http-fixtures.yaml   # Fixture manifest (static http-target fixture)
├── flows/
│   └── httpbin/
│       ├── get-smoke.flow.yaml   # Flow: GET smoke test
│       └── post-smoke.flow.yaml  # Flow: POST echo test
├── steps/
│   └── http-steps.yaml      # Step manifest: http.get, http.post, http.assert-*
├── models/                  # Reserved (empty)
├── artifacts/runs/          # Run evidence written here at runtime
└── reports/                 # HTML / JSON / JUnit / Markdown reports written here
```

## Running Locally

Validate the project (no network required):

```bash
cd specs/httpbin-smoke
dotnet run --project ../../src/Cress.Cli -- validate
```

Run all flows (requires outbound HTTPS to httpbin.org):

```bash
cd specs/httpbin-smoke
dotnet run --project ../../src/Cress.Cli -- run
```

Run a single flow by path:

```bash
dotnet run --project ../../src/Cress.Cli -- run flows/httpbin/get-smoke.flow.yaml
```

Run only flows tagged `smoke`:

```bash
dotnet run --project ../../src/Cress.Cli -- run --tag smoke
```

## YAML Schema Quick Reference

### Config (`.cress/config.yaml`)

Required fields: `version`, `project.name`, `project.defaultProfile`, all `paths.*` entries.
The `drivers` block enables/disables runtime drivers; only `http: enabled: true` is needed here.

### Profile (`.cress/profiles/{name}.yaml`)

`profile` (name), `baseUrl`, `timeouts.step`, `timeouts.expectation`, `variables.*`.

### Capability (`capabilities/*.md`)

Markdown file with a YAML front matter block (`version`, `id`, `owner`, `risk`, `tags`).
The `# Capability: …` H1 heading becomes the capability name.
`## Rules` lists plain-text business rules; `## Acceptance Criteria` / `### ID` headings declare ACs.

### Fixture Manifest (`fixtures/*.yaml`)

`version` + `fixtures` map. Each entry needs `type` and `strategy` (`static` | `dynamic` | etc.).

### Step Manifest (`steps/*.yaml`)

`version` + `steps` list. Each step needs `name`, `drivers` list, and `implementation.operation`.
The operation must match one of the HTTP driver's built-in operations:
`get`, `post`, `put`, `patch`, `delete`, `request`, `send`,
`assert-status`, `assert-json`, `assert-body-contains`, `assert-header`.

### Flow (`flows/**/*.flow.yaml`)

`version`, `id`, `name` are required. `when` must have at least one `step:` entry;
`then` must have at least one `expect:` entry.
Both `when` and `then` items accept a `with:` map of string inputs passed to the driver.

## HTTP Driver Input Reference

| Step | Key inputs |
|------|-----------|
| `http.get` | `url` (absolute) **or** `path` (relative to `baseUrl`) |
| `http.post` | `url`/`path`, `json` (JSON string body) |
| `http.assert-status` | `status` (expected code as string, e.g. `"200"`) |
| `http.assert-json` | `path` (dot-separated JSON path), `equals` (expected value) |
| `http.assert-body-contains` | `text` (substring to find in body) |
