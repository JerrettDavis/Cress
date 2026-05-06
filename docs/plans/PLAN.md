# Cress Application Specification

## 1. Executive Summary

Cress is a standalone, language-agnostic end-to-end testing framework for defining, executing, observing, and governing real user workflows across greenfield and brownfield applications.

Cress is not intended to be another browser automation wrapper. It is a workflow-first validation platform. A user journey is authored as a product-facing specification, compiled into an executable plan, executed through one or more platform drivers, and verified through observable outcomes across UI, API, database, file system, messaging, notification, and external system boundaries.

The framework must support SDD, BDD, ATDD, model-based testing, scripted automation, exploratory workflow generation, and brownfield recording workflows without forcing teams into a single authoring style.

The north-star concept is:

> A flow is a user intent compiled into an executable, observable, evidence-producing plan.

## 2. Product Name

Working name: **Cress**

Alternative names retained for future review:

* JourneySpec
* ProofPilot
* ScenarioOS
* Veriflow
* UserProof
* Conductor

Unless otherwise stated, this document uses **Cress** as the product and project name.

## 3. Product Vision

Cress enables teams to describe how software should behave using natural user journeys, then execute those journeys against real applications and systems with repeatable, auditable, evidence-rich automation.

The system should allow a product manager, QA engineer, software engineer, platform engineer, or domain expert to read a flow and understand what behavior is being validated without needing to understand selectors, waits, page objects, driver APIs, database schemas, or implementation details.

Developers should be able to bind those natural flows to executable automation using their preferred language, framework, and platform tools.

## 4. Product Mission

Cress exists to make high-value E2E testing:

* Portable across languages, frameworks, platforms, and application architectures.
* Understandable to both product stakeholders and developers.
* Useful for greenfield specification-first development.
* Practical for brownfield systems with imperfect testability.
* Observable enough to debug failures without archaeological excavation.
* Governable enough for enterprise environments.
* Extensible enough to support UI, API, CLI, desktop, mobile, database, queue, file, email, and external system workflows.

## 5. Problem Statement

Most E2E testing suites are one-off automation projects built tightly around a specific application, language, test runner, UI framework, and deployment environment.

They commonly suffer from:

* Brittle selectors.
* Poor fixture management.
* Overuse of sleeps and timing assumptions.
* Tests written at the wrong abstraction level.
* Gherkin scenarios that merely wrap implementation details.
* Page objects that become parallel application architectures.
* Limited evidence capture.
* Weak failure classification.
* Inability to validate cross-boundary outcomes.
* Difficult brownfield onboarding.
* Little to no model-based coverage.
* Poor traceability to requirements and acceptance criteria.
* Framework code that cannot be reused across products.
* Heavy coupling to one programming language or automation driver.

Cress addresses this by treating user workflows, capability maps, execution plans, fixtures, driver actions, observations, and evidence bundles as first-class concepts.

## 6. Goals

### 6.1 Product Goals

Cress must:

1. Allow users to author product-facing workflow specifications.
2. Compile specifications into normalized intermediate representations.
3. Resolve normalized flow definitions into executable plans.
4. Execute plans through one or more driver adapters.
5. Validate observable outcomes across multiple system boundaries.
6. Capture comprehensive evidence for every run.
7. Generate human-readable and machine-readable reports.
8. Support both greenfield and brownfield adoption paths.
9. Enable SDD, BDD, ATDD, and model-based testing workflows.
10. Support reusable step and capability libraries.
11. Support language-agnostic extension through plugin protocols.
12. Provide governance, traceability, policy, and audit capabilities.
13. Support AI-assisted authoring, planning, and triage without allowing unreviewed mutation of test intent.

### 6.2 Engineering Goals

Cress must be:

1. Modular.
2. Extensible.
3. Driver-agnostic.
4. Language-agnostic.
5. Source-control friendly.
6. CI/CD friendly.
7. Deterministic by default.
8. Observable by default.
9. Safe for enterprise use.
10. Designed for local development and large-scale execution.

### 6.3 User Experience Goals

Cress must make common workflows obvious:

* Initialize a project.
* Define a capability.
* Write a flow.
* Generate missing step stubs.
* Bind steps to automation.
* Run tests locally.
* Run tests in CI.
* Debug failures.
* Promote recordings into stable flows.
* Publish reports.
* Trace flows back to requirements.

The CLI and generated project structure must help users do the right thing by default.

## 7. Non-Goals

Cress is not:

1. A replacement for unit testing.
2. A replacement for integration testing.
3. A single universal automation driver.
4. A no-code-only test platform.
5. A proprietary visual test editor.
6. A browser-only automation library.
7. A magical AI test generator that removes the need for engineering judgment.
8. A production monitoring system, though it may support synthetic monitoring later.
9. A load testing framework, though flows may be reused by load tools in the future.
10. A security testing platform, though security-oriented flows may be authored.

## 8. Target Users

### 8.1 Software Engineers

Need to:

* Bind product-facing flows to code.
* Debug failing flows.
* Create reusable step libraries.
* Validate feature work before merge.
* Maintain automation with minimal brittleness.

### 8.2 QA Engineers / SDETs

Need to:

* Author and maintain workflows.
* Build reusable automation assets.
* Validate cross-system behavior.
* Manage fixture strategies.
* Analyze failures and flakiness.

### 8.3 Product Owners / Business Analysts

Need to:

* Read and review behavior specifications.
* Confirm acceptance criteria coverage.
* Understand what user journeys are validated.
* Avoid implementation-detail-heavy test artifacts.

### 8.4 Platform Engineers

Need to:

* Standardize E2E patterns across teams.
* Provide shared drivers, policies, fixtures, and reporting.
* Integrate with CI/CD and observability tooling.
* Govern usage in enterprise environments.

### 8.5 Release Managers

Need to:

* Determine release confidence.
* Review pass/fail status by risk area.
* Track flaky tests.
* Understand failure impact.
* Enforce required evidence and traceability.

## 9. Core Concepts

### 9.1 Capability

A capability is a product-level behavior the system provides to a user, actor, or external system.

Examples:

* Patient requests prescription refill.
* Admin approves pending refill.
* Customer places guest order.
* User exports monthly report.
* Operator imports CSV file.

A capability may contain rules, examples, acceptance criteria, traceability metadata, and one or more flows.

### 9.2 Flow

A flow is a user journey or system journey that validates one or more capabilities.

A flow contains:

* Identity.
* Name.
* Description.
* Persona or actor context.
* Preconditions.
* Actions.
* Expectations.
* Fixtures.
* Tags.
* Traceability metadata.
* Execution constraints.

### 9.3 Scenario

A scenario is a concrete example of a flow.

A capability may have multiple scenarios:

* Happy path.
* Negative path.
* Edge case.
* Role-specific path.
* Permission failure path.
* Dependency unavailable path.

### 9.4 Step

A step is a reusable executable unit of workflow behavior.

Examples:

* `patient.sign_in`
* `medication.request_refill`
* `pharmacy.approve_refill`
* `order.submit_guest_checkout`
* `report.export_csv`

Steps are registered in the step registry and may be implemented in any supported language through plugin SDKs.

### 9.5 Action

An action is an executable instruction in a plan.

Actions may be:

* High-level domain actions.
* Driver-specific operations.
* Fixture setup operations.
* Observation operations.
* Cleanup operations.

### 9.6 Expectation

An expectation is an observable proof that behavior occurred.

Expectations may be validated through:

* UI state.
* API response.
* Database row.
* Message queue event.
* Email.
* File system artifact.
* Log event.
* Audit event.
* Notification.
* External system state.

### 9.7 Driver

A driver is an adapter that can interact with a platform, system, or boundary.

Examples:

* Playwright driver.
* Appium driver.
* FlaUI driver.
* HTTP driver.
* Database driver.
* CLI driver.
* File system driver.
* Message queue driver.
* Email driver.
* Image/OCR driver.

### 9.8 Fixture

A fixture is controlled state required by a flow.

Fixture examples:

* Existing user account.
* Patient with refillable medication.
* Product in catalog.
* Empty shopping cart.
* Mocked third-party service.
* Existing database record.
* Temporary file.
* Generated email inbox.

### 9.9 Persona

A persona is an actor profile used by flows.

Examples:

* Existing patient.
* New customer.
* Pharmacy admin.
* Read-only auditor.
* System operator.

A persona may include credentials, role metadata, preferences, permissions, seeded data references, and environment-specific bindings.

### 9.10 Model

A model is a state graph describing possible journeys through a capability or application area.

A model contains:

* States.
* Transitions.
* Actions.
* Guards.
* Effects.
* Coverage goals.
* Risk weights.

### 9.11 Plan

A plan is the concrete executable representation of a flow after parsing, normalization, fixture resolution, and step planning.

A plan must be deterministic and inspectable.

### 9.12 Evidence Bundle

An evidence bundle is the complete set of artifacts captured during a run.

Evidence may include:

* Normalized flow.
* Execution plan.
* Timeline.
* Screenshots.
* Videos.
* Traces.
* Logs.
* Network captures.
* DOM snapshots.
* Accessibility snapshots.
* API transcripts.
* Database observations.
* Queue messages.
* File artifacts.
* Failure analysis.

### 9.13 Report

A report summarizes execution results for humans and machines.

Reports must support:

* HTML.
* JSON.
* Markdown.
* JUnit XML.
* CI annotations.
* Optional OpenTelemetry export.

## 10. Product Scope

### 10.1 MVP Scope

The first production-quality MVP must include:

1. CLI.
2. Project initialization.
3. YAML flow authoring.
4. Markdown capability authoring.
5. Normalized intermediate representation.
6. Plan generation.
7. Local step registry.
8. TypeScript step SDK.
9. .NET step SDK.
10. Playwright driver.
11. HTTP driver.
12. Static and generated fixtures.
13. Evidence bundle generation.
14. HTML, JSON, Markdown, and JUnit reporting.
15. CI-friendly exit codes.
16. Schema validation.
17. Step stub generation.
18. Basic traceability metadata.
19. Failure classification primitives.
20. Local run history.

### 10.2 Post-MVP Scope

Post-MVP features include:

1. Gherkin import/export.
2. Brownfield recorder.
3. Flow promotion from recordings.
4. Model-based testing.
5. Appium driver.
6. FlaUI driver.
7. CLI driver.
8. Database driver.
9. Queue driver.
10. Email driver.
11. Image/OCR fallback driver.
12. Dashboard.
13. AI-assisted authoring.
14. AI-assisted failure triage.
15. Flake detection.
16. Step marketplace or registry.
17. Remote execution.
18. Distributed execution.
19. Enterprise policy engine.
20. OpenTelemetry integration.

## 11. System Architecture

## 11.1 High-Level Architecture

Cress consists of the following major components:

```text
Cress
├─ CLI
├─ Project System
├─ Spec Parser
├─ Schema Validator
├─ Normalization Engine
├─ Planner
├─ Step Registry
├─ Fixture Engine
├─ Runtime Orchestrator
├─ Driver Host
├─ Plugin Host
├─ Evidence Store
├─ Report Generator
├─ Run History Store
├─ Policy Engine
├─ AI Assistance Layer
└─ Dashboard
```

## 11.2 Component Responsibilities

### 11.2.1 CLI

The CLI provides the primary developer and automation interface.

Responsibilities:

* Initialize projects.
* Validate specs.
* Discover flows, steps, drivers, and fixtures.
* Generate stubs.
* Run flows.
* Open reports.
* Inspect evidence.
* Manage local configuration.
* Execute diagnostics.

### 11.2.2 Project System

The project system defines the standard folder layout, configuration files, schema references, and conventions.

Responsibilities:

* Locate project root.
* Load configuration.
* Resolve paths.
* Discover specs.
* Discover plugins.
* Discover step packages.
* Discover driver packages.

### 11.2.3 Spec Parser

The spec parser reads supported authoring formats and emits a normalized internal structure.

Supported MVP formats:

* YAML flow files.
* Markdown capability files.

Post-MVP formats:

* Gherkin feature files.
* JSON flow files.
* Code DSL manifests.
* Recorder-generated files.

### 11.2.4 Schema Validator

The schema validator validates structured documents against versioned schemas.

Documents include:

* Flow files.
* Capability files.
* Model files.
* Fixture files.
* Persona files.
* Driver config files.
* Step manifests.
* Report files.

### 11.2.5 Normalization Engine

The normalization engine converts all authoring formats into the canonical Cress Intermediate Representation.

Responsibilities:

* Resolve aliases.
* Normalize names.
* Expand shorthand syntax.
* Attach metadata.
* Link requirements.
* Link fixtures.
* Link personas.
* Validate structural correctness.

### 11.2.6 Planner

The planner converts normalized flows into executable plans.

Responsibilities:

* Resolve high-level actions to registered steps.
* Expand composite steps.
* Resolve fixtures.
* Determine driver requirements.
* Insert setup and cleanup actions.
* Validate preconditions.
* Validate expected effects.
* Detect missing step bindings.
* Produce inspectable execution plans.

### 11.2.7 Step Registry

The step registry stores metadata about executable steps.

Responsibilities:

* Register steps.
* Resolve steps by name, alias, tag, capability, or intent.
* Validate step inputs.
* Track supported drivers.
* Track step effects.
* Track idempotency and retry safety.
* Track ownership and versioning.

### 11.2.8 Fixture Engine

The fixture engine creates, locates, claims, tracks, and cleans up test state.

Responsibilities:

* Resolve fixture declarations.
* Generate synthetic data.
* Call fixture providers.
* Support static fixtures.
* Support environment-bound fixtures.
* Support seeded fixtures.
* Support existing brownfield fixtures.
* Track created resources.
* Cleanup resources when configured.

### 11.2.9 Runtime Orchestrator

The runtime orchestrator executes plans.

Responsibilities:

* Create run sessions.
* Start drivers.
* Execute actions.
* Evaluate expectations.
* Capture evidence.
* Apply retry policies.
* Invoke hooks.
* Track timeline events.
* Classify failures.
* Produce run results.

### 11.2.10 Driver Host

The driver host manages platform drivers.

Responsibilities:

* Load driver plugins.
* Start driver sessions.
* Route actions to drivers.
* Normalize driver results.
* Capture driver-specific evidence.
* Dispose driver resources.

### 11.2.11 Plugin Host

The plugin host enables language-agnostic extension.

Responsibilities:

* Launch plugin processes.
* Communicate using JSON-RPC, gRPC, or a compatible transport.
* Register capabilities exposed by plugins.
* Invoke plugin operations.
* Monitor plugin health.
* Enforce plugin timeouts.
* Capture plugin logs.

### 11.2.12 Evidence Store

The evidence store writes run artifacts to durable storage.

Responsibilities:

* Create run artifact directories.
* Write normalized flow files.
* Write plans.
* Write timeline files.
* Store screenshots.
* Store videos.
* Store traces.
* Store logs.
* Store observation payloads.
* Store failure analysis.

### 11.2.13 Report Generator

The report generator creates human-readable and machine-readable reports.

Responsibilities:

* Generate HTML reports.
* Generate Markdown summaries.
* Generate JSON reports.
* Generate JUnit XML.
* Generate CI annotations.
* Include evidence links.
* Include failure classification.
* Include traceability metadata.

### 11.2.14 Run History Store

The run history store tracks previous runs.

Responsibilities:

* Store run summaries.
* Store pass/fail history.
* Track durations.
* Track failure categories.
* Track flaky flow indicators.
* Enable comparison to previous runs.

### 11.2.15 Policy Engine

The policy engine enforces project and environment rules.

Responsibilities:

* Block unsafe actions.
* Enforce production restrictions.
* Require evidence capture.
* Require traceability metadata.
* Enforce retry limits.
* Enforce driver allowlists.
* Enforce fixture cleanup rules.

### 11.2.16 AI Assistance Layer

The AI assistance layer provides optional assisted workflows.

Responsibilities:

* Suggest flows from requirements.
* Suggest missing scenarios.
* Suggest step stubs.
* Summarize failures.
* Suggest locator repairs.
* Classify failures.
* Explain plan resolution.

AI assistance must not silently alter business expectations, remove assertions, skip steps, or mutate authoritative specs without explicit user approval.

## 12. Standard Project Layout

A Cress project should use the following structure:

```text
.cress/
  config.yaml
  policy.yaml
  profiles/
    local.yaml
    ci.yaml
    staging.yaml
  run-history/

capabilities/
  prescription-refill.md

flows/
  prescription-refill/
    patient-requests-standard-refill.flow.yaml
    patient-requests-controlled-substance-refill.flow.yaml

models/
  prescription-refill.model.yaml

fixtures/
  personas/
    existing-patient.yaml
    pharmacy-admin.yaml
  data/
    refillable-medication.yaml
  providers/

steps/
  manifests/
    patient.steps.yaml
    medication.steps.yaml
  dotnet/
  node/

plugins/
  drivers/
  steps/
  fixtures/

artifacts/
  runs/

reports/

schemas/
```

## 13. Configuration Specification

## 13.1 Root Configuration

File: `.cress/config.yaml`

Example:

```yaml
version: 1
project:
  name: ReMeds E2E
  defaultProfile: local

paths:
  capabilities: capabilities
  flows: flows
  models: models
  fixtures: fixtures
  steps: steps
  artifacts: artifacts/runs
  reports: reports

defaults:
  timeout: 30000
  retries: 0
  evidence: standard
  cleanup: on-success

plugins:
  discover:
    - plugins
    - steps

drivers:
  playwright:
    enabled: true
    config: .cress/drivers/playwright.yaml
  http:
    enabled: true
    config: .cress/drivers/http.yaml
```

### Acceptance Criteria

* AC-13.1.1: The CLI must detect the project root by locating `.cress/config.yaml`.
* AC-13.1.2: The configuration file must include a required `version` field.
* AC-13.1.3: The configuration parser must reject unsupported configuration versions with a clear error.
* AC-13.1.4: The configuration parser must validate all configured paths.
* AC-13.1.5: Missing optional path values must fall back to documented defaults.
* AC-13.1.6: Invalid YAML must produce a diagnostic with file path, line, and column when available.
* AC-13.1.7: Unknown top-level keys must produce warnings by default.
* AC-13.1.8: Unknown top-level keys must produce errors when strict mode is enabled.
* AC-13.1.9: Profile-specific configuration must override root configuration predictably.
* AC-13.1.10: The effective configuration must be printable with `cress config print`.

## 13.2 Profile Configuration

Profiles allow environment-specific configuration.

Example:

```yaml
profile: staging
baseUrl: https://staging.example.com

timeouts:
  default: 30000
  navigation: 60000

evidence:
  level: full
  video: retain-on-failure
  screenshots: every-step

secrets:
  provider: environment

variables:
  pharmacyApiUrl: https://staging-api.example.com
```

### Acceptance Criteria

* AC-13.2.1: Users must be able to select a profile using `--profile`.
* AC-13.2.2: If no profile is provided, the configured default profile must be used.
* AC-13.2.3: Profile values must override root defaults.
* AC-13.2.4: Environment variables must be resolvable inside profile values.
* AC-13.2.5: Missing required profile values must fail validation before execution begins.
* AC-13.2.6: Secrets must not be printed in full by diagnostic commands.
* AC-13.2.7: The selected profile must be included in the run report.

## 14. CLI Specification

## 14.1 CLI Command Overview

The CLI executable is `cress`.

Required MVP commands:

```text
cress init
cress validate
cress discover
cress plan
cress run
cress report
cress generate steps
cress doctor
cress config print
```

Post-MVP commands:

```text
cress record
cress promote
cress explore
cress model validate
cress history
cress explain
cress ai suggest
cress dashboard
```

## 14.2 `cress init`

Initializes a new Cress project.

Usage:

```bash
cress init
cress init --template web
cress init --template api-and-web
cress init --name "My App E2E"
```

Required behavior:

* Create `.cress/config.yaml`.
* Create standard directories.
* Create example flow.
* Create example capability.
* Create example fixture.
* Create README guidance.
* Optionally create starter step implementation.

### Acceptance Criteria

* AC-14.2.1: Running `cress init` in an empty directory must create a valid Cress project.
* AC-14.2.2: Running `cress init` in an existing Cress project must fail unless `--force` is provided.
* AC-14.2.3: The generated project must pass `cress validate` without modifications.
* AC-14.2.4: The generated example flow must be runnable or explicitly marked as a template requiring user implementation.
* AC-14.2.5: The command must not overwrite existing files unless `--force` is provided.
* AC-14.2.6: The command must display the next recommended commands after initialization.

## 14.3 `cress validate`

Validates the project, configuration, specs, schemas, steps, fixtures, and drivers.

Usage:

```bash
cress validate
cress validate --strict
cress validate flows/refill.flow.yaml
```

### Acceptance Criteria

* AC-14.3.1: The command must validate root configuration.
* AC-14.3.2: The command must validate profile configuration.
* AC-14.3.3: The command must validate all discovered flow files.
* AC-14.3.4: The command must validate all discovered capability files.
* AC-14.3.5: The command must validate all discovered fixture files.
* AC-14.3.6: The command must validate all step manifests.
* AC-14.3.7: The command must report unresolved step references.
* AC-14.3.8: The command must report unresolved fixture references.
* AC-14.3.9: The command must report unresolved persona references.
* AC-14.3.10: The command must produce machine-readable output when `--json` is provided.
* AC-14.3.11: The command must exit with code `0` when validation passes.
* AC-14.3.12: The command must exit with non-zero code when validation fails.

## 14.4 `cress discover`

Discovers available flows, capabilities, steps, fixtures, drivers, and plugins.

Usage:

```bash
cress discover
cress discover flows
cress discover steps
cress discover drivers
cress discover fixtures
```

### Acceptance Criteria

* AC-14.4.1: The command must list discovered flows.
* AC-14.4.2: The command must list discovered capabilities.
* AC-14.4.3: The command must list discovered steps.
* AC-14.4.4: The command must list discovered fixtures.
* AC-14.4.5: The command must list discovered drivers.
* AC-14.4.6: The command must indicate invalid or disabled entries.
* AC-14.4.7: The command must support JSON output.
* AC-14.4.8: The command must support filtering by tag.
* AC-14.4.9: The command must support filtering by capability.

## 14.5 `cress plan`

Generates an execution plan without running it.

Usage:

```bash
cress plan flows/refill.flow.yaml
cress plan --tag smoke
cress plan --profile staging
cress plan --output plan.json
```

### Acceptance Criteria

* AC-14.5.1: The command must parse selected flows.
* AC-14.5.2: The command must normalize selected flows.
* AC-14.5.3: The command must resolve fixtures.
* AC-14.5.4: The command must resolve registered steps.
* AC-14.5.5: The command must detect missing steps.
* AC-14.5.6: The command must detect missing drivers.
* AC-14.5.7: The command must output an inspectable execution plan.
* AC-14.5.8: The command must not execute driver actions.
* AC-14.5.9: The command must support JSON output.
* AC-14.5.10: The command must exit non-zero when a valid plan cannot be produced.

## 14.6 `cress run`

Runs one or more flows.

Usage:

```bash
cress run
cress run flows/refill.flow.yaml
cress run --tag smoke
cress run --profile staging
cress run --parallel 4
cress run --report html,json,junit
```

### Acceptance Criteria

* AC-14.6.1: The command must discover selected flows.
* AC-14.6.2: The command must validate selected flows before execution.
* AC-14.6.3: The command must generate execution plans before execution.
* AC-14.6.4: The command must start required drivers.
* AC-14.6.5: The command must execute plan actions in order unless parallelization is explicitly supported by the plan.
* AC-14.6.6: The command must evaluate all expectations.
* AC-14.6.7: The command must capture configured evidence.
* AC-14.6.8: The command must write a run result.
* AC-14.6.9: The command must generate configured reports.
* AC-14.6.10: The command must exit code `0` only when all required flows pass.
* AC-14.6.11: The command must exit non-zero when any required flow fails.
* AC-14.6.12: The command must support `--continue-on-failure`.
* AC-14.6.13: The command must support dry-run planning through `--dry-run`.
* AC-14.6.14: The command must print the artifact path at the end of execution.

## 14.7 `cress report`

Works with generated reports.

Usage:

```bash
cress report open
cress report list
cress report summarize artifacts/runs/run-001
```

### Acceptance Criteria

* AC-14.7.1: The command must list recent reports.
* AC-14.7.2: The command must open the latest HTML report when `open` is used.
* AC-14.7.3: The command must summarize a selected run.
* AC-14.7.4: The command must support JSON summary output.
* AC-14.7.5: The command must handle missing reports with a clear diagnostic.

## 14.8 `cress generate steps`

Generates missing step stubs.

Usage:

```bash
cress generate steps
cress generate steps flows/refill.flow.yaml
cress generate steps --language dotnet
cress generate steps --language typescript
```

### Acceptance Criteria

* AC-14.8.1: The command must identify unresolved step references.
* AC-14.8.2: The command must generate stubs for selected language SDKs.
* AC-14.8.3: The command must not overwrite existing step implementations unless `--force` is provided.
* AC-14.8.4: Generated stubs must include step name, description, input contract, and TODO body.
* AC-14.8.5: Generated stubs must compile in the target SDK project.
* AC-14.8.6: Generated stubs must include references back to source flow files.

## 14.9 `cress doctor`

Diagnoses local environment readiness.

### Acceptance Criteria

* AC-14.9.1: The command must verify the project configuration.
* AC-14.9.2: The command must verify driver availability.
* AC-14.9.3: The command must verify plugin availability.
* AC-14.9.4: The command must verify required external tools for configured drivers.
* AC-14.9.5: The command must report actionable remediation steps.
* AC-14.9.6: The command must support JSON output for CI diagnostics.

## 15. Authoring Formats

## 15.1 YAML Flow Format

Example:

```yaml
version: 1
id: patient-requests-standard-refill
name: Patient requests standard refill
capability: prescription-refill
summary: Existing patient requests a standard medication refill.

tags:
  - smoke
  - patient
  - refill

traceability:
  requirement: RX-1234
  acceptanceCriteria:
    - RX-1234-AC1
    - RX-1234-AC2
  owner: PharmacyPlatform
  risk: high

personas:
  patient: existing-patient

fixtures:
  medication:
    use: medication.refillable
    for: patient

given:
  - patient account exists
  - patient has a refillable medication

when:
  - step: app.open
  - step: patient.sign_in
    with:
      persona: patient
  - step: medication.request_refill
    with:
      medication: medication

then:
  - expect: patient.sees_refill_confirmation
  - expect: pharmacy.queue_contains_refill_request
  - expect: audit.event_recorded
    with:
      event: RefillRequested
```

### Acceptance Criteria

* AC-15.1.1: YAML flow files must require a `version` field.
* AC-15.1.2: YAML flow files must require a stable `id` field.
* AC-15.1.3: YAML flow files must require a human-readable `name` field.
* AC-15.1.4: YAML flow files must support optional `capability` linkage.
* AC-15.1.5: YAML flow files must support tags.
* AC-15.1.6: YAML flow files must support traceability metadata.
* AC-15.1.7: YAML flow files must support personas.
* AC-15.1.8: YAML flow files must support fixtures.
* AC-15.1.9: YAML flow files must support `given`, `when`, and `then` sections.
* AC-15.1.10: The parser must preserve source locations for diagnostics.
* AC-15.1.11: The parser must reject duplicate flow IDs within a project.
* AC-15.1.12: The parser must allow plain-language given statements.
* AC-15.1.13: The parser must allow structured executable step references.
* AC-15.1.14: The parser must allow structured expectations.

## 15.2 Markdown Capability Format

Example:

```markdown
---
version: 1
id: prescription-refill
owner: PharmacyPlatform
risk: high
tags:
  - refill
  - pharmacy
---

# Capability: Patient requests prescription refill

A patient should be able to request a refill for an eligible medication without calling the pharmacy.

## Rules

- Inactive medications cannot be refilled.
- Controlled substances require manual review.
- Expired prescriptions show renewal instructions.
- Successful refill requests appear in the pharmacy review queue.

## Acceptance Criteria

### RX-1234-AC1

Given an existing patient has a refillable medication, when the patient requests a refill, then the refill request is submitted.

### RX-1234-AC2

Given a submitted refill request, then the pharmacy review queue contains the request.

## Examples

### Existing patient requests standard refill

Given an existing patient has a refillable medication.
When the patient requests a refill.
Then the refill request should be submitted.
And the pharmacy should see the request in the review queue.
And the patient should receive confirmation.
```

### Acceptance Criteria

* AC-15.2.1: Markdown capability files must support YAML front matter.
* AC-15.2.2: Markdown capability files must require a capability ID.
* AC-15.2.3: Markdown capability files must support rules.
* AC-15.2.4: Markdown capability files must support acceptance criteria.
* AC-15.2.5: Markdown capability files must support examples.
* AC-15.2.6: The parser must preserve headings and source locations.
* AC-15.2.7: The parser must link examples to generated or explicit flows when possible.
* AC-15.2.8: The parser must warn when acceptance criteria have no associated flow.
* AC-15.2.9: The parser must support strict mode requiring every acceptance criterion to be covered.

## 15.3 Gherkin Support

Gherkin support is post-MVP but must be designed into the model.

### Acceptance Criteria

* AC-15.3.1: The system must be architected so Gherkin can be parsed into the same intermediate representation as YAML and Markdown.
* AC-15.3.2: Gherkin scenarios must preserve feature, scenario, background, examples, tags, and step text.
* AC-15.3.3: Gherkin import must not require step regexes to be the primary internal representation.
* AC-15.3.4: Gherkin export must be possible for flows that map cleanly to Given/When/Then.

## 16. Intermediate Representation

The Cress Intermediate Representation, or CIR, is the normalized representation used internally.

Example:

```json
{
  "version": 1,
  "flowId": "patient-requests-standard-refill",
  "name": "Patient requests standard refill",
  "capabilityId": "prescription-refill",
  "tags": ["smoke", "patient", "refill"],
  "traceability": {
    "requirement": "RX-1234",
    "acceptanceCriteria": ["RX-1234-AC1", "RX-1234-AC2"],
    "owner": "PharmacyPlatform",
    "risk": "high"
  },
  "personas": {
    "patient": "existing-patient"
  },
  "fixtures": [
    {
      "name": "medication",
      "use": "medication.refillable",
      "bindings": {
        "for": "patient"
      }
    }
  ],
  "actions": [
    {
      "kind": "step",
      "name": "app.open"
    },
    {
      "kind": "step",
      "name": "patient.sign_in",
      "inputs": {
        "persona": "patient"
      }
    },
    {
      "kind": "step",
      "name": "medication.request_refill",
      "inputs": {
        "medication": "medication"
      }
    }
  ],
  "expectations": [
    {
      "name": "patient.sees_refill_confirmation"
    },
    {
      "name": "pharmacy.queue_contains_refill_request"
    },
    {
      "name": "audit.event_recorded",
      "inputs": {
        "event": "RefillRequested"
      }
    }
  ]
}
```

### Acceptance Criteria

* AC-16.1: All supported authoring formats must compile to CIR before planning.
* AC-16.2: CIR must be serializable to JSON.
* AC-16.3: CIR must include source mapping metadata for diagnostics.
* AC-16.4: CIR must preserve traceability metadata.
* AC-16.5: CIR must preserve tags.
* AC-16.6: CIR must preserve persona references.
* AC-16.7: CIR must preserve fixture references.
* AC-16.8: CIR must distinguish setup actions, user actions, expectations, and cleanup actions.
* AC-16.9: CIR schema must be versioned.
* AC-16.10: CIR schema changes must be backward-compatible within a major version.

## 17. Step Registry Specification

## 17.1 Step Manifest

Example:

```yaml
version: 1
steps:
  - name: patient.sign_in
    aliases:
      - sign in as patient
      - patient logs in
    description: Sign in as a patient persona.
    inputs:
      persona:
        type: PersonaRef
        required: true
    effects:
      - browser.session.authenticated
      - audit.login.recorded
    drivers:
      - playwright
      - appium
    idempotency: not-idempotent
    retrySafe: false
    owner: IdentityTeam
    implementation:
      plugin: steps.dotnet.identity
      operation: PatientSignIn
```

## 17.2 Step Metadata Requirements

Each step must support:

* Name.
* Description.
* Aliases.
* Input schema.
* Output schema.
* Preconditions.
* Effects.
* Driver requirements.
* Idempotency classification.
* Retry safety.
* Timeout override.
* Owner.
* Version.
* Implementation binding.

### Acceptance Criteria

* AC-17.2.1: Step names must be globally unique within the project after plugin discovery.
* AC-17.2.2: Duplicate step names must fail validation unless explicitly version-qualified.
* AC-17.2.3: Step aliases must be searchable.
* AC-17.2.4: Step inputs must be schema-validated before execution.
* AC-17.2.5: Step outputs must be schema-validated after execution when an output schema is declared.
* AC-17.2.6: Step driver requirements must be validated during planning.
* AC-17.2.7: Missing step implementations must be reported during planning.
* AC-17.2.8: Steps marked `retrySafe: false` must not be retried automatically.
* AC-17.2.9: Step timeout overrides must be honored by the runtime.
* AC-17.2.10: Step ownership must appear in reports when available.

## 18. Fixture Engine Specification

## 18.1 Fixture Declaration

Example:

```yaml
version: 1
fixtures:
  medication.refillable:
    type: domain.medication
    strategy: generated
    traits:
      - active
      - refillable
      - non-controlled
    cleanup: on-success
```

## 18.2 Fixture Strategies

Supported fixture strategies:

* `static`
* `generated`
* `seeded`
* `existing`
* `claimed`
* `simulated`
* `contract-backed`
* `manual`

### Acceptance Criteria

* AC-18.2.1: The fixture engine must resolve fixture references before flow actions execute.
* AC-18.2.2: Static fixtures must load from configured files.
* AC-18.2.3: Generated fixtures must be created by fixture providers.
* AC-18.2.4: Seeded fixtures must support setup through API, database, CLI, or custom provider.
* AC-18.2.5: Existing fixtures must not be mutated unless the fixture declares mutation is allowed.
* AC-18.2.6: Claimed fixtures must be locked for the duration of a run when supported by the provider.
* AC-18.2.7: Simulated fixtures must support dependency virtualization.
* AC-18.2.8: Manual fixtures must produce clear instructions and block automated execution unless manual mode is enabled.
* AC-18.2.9: Fixture cleanup behavior must support `never`, `always`, `on-success`, and `on-failure`.
* AC-18.2.10: All created fixture resources must be tracked in the evidence bundle.

## 19. Driver Specification

## 19.1 Driver Contract

Drivers must expose a common contract through the plugin protocol.

Required operations:

* `capabilities`
* `startSession`
* `performAction`
* `observe`
* `captureEvidence`
* `stopSession`
* `healthCheck`

### Acceptance Criteria

* AC-19.1.1: Drivers must declare their capabilities.
* AC-19.1.2: Drivers must support session startup.
* AC-19.1.3: Drivers must support session shutdown.
* AC-19.1.4: Drivers must support action execution.
* AC-19.1.5: Drivers must support observations.
* AC-19.1.6: Drivers must support evidence capture where applicable.
* AC-19.1.7: Driver errors must be normalized into Cress diagnostics.
* AC-19.1.8: Driver logs must be capturable in the evidence bundle.
* AC-19.1.9: Driver sessions must be isolated per run unless explicit session reuse is enabled.

## 19.2 Playwright Driver

The Playwright driver is required for MVP.

Required capabilities:

* Browser launch.
* Browser context creation.
* Page navigation.
* Role-based locator actions.
* Label-based fill actions.
* Text assertions.
* URL assertions.
* Screenshot capture.
* Video capture.
* Trace capture.
* Console log capture.
* Network capture.
* Accessibility snapshot capture where supported.

### Acceptance Criteria

* AC-19.2.1: The Playwright driver must support Chromium.
* AC-19.2.2: The Playwright driver should support Firefox.
* AC-19.2.3: The Playwright driver should support WebKit.
* AC-19.2.4: The Playwright driver must support headless mode.
* AC-19.2.5: The Playwright driver must support headed mode.
* AC-19.2.6: The Playwright driver must support configured base URL.
* AC-19.2.7: The Playwright driver must prefer semantic locators over brittle selectors.
* AC-19.2.8: The Playwright driver must capture screenshots on failure.
* AC-19.2.9: The Playwright driver must retain traces according to evidence policy.
* AC-19.2.10: The Playwright driver must expose enough metadata to classify timeout, locator, navigation, and assertion failures.

## 19.3 HTTP Driver

The HTTP driver is required for MVP.

Required capabilities:

* GET.
* POST.
* PUT.
* PATCH.
* DELETE.
* Headers.
* Authentication injection.
* JSON body.
* Form body.
* Response assertions.
* JSON path assertions.
* Request/response evidence capture.

### Acceptance Criteria

* AC-19.3.1: The HTTP driver must support common HTTP verbs.
* AC-19.3.2: The HTTP driver must support base URL configuration.
* AC-19.3.3: The HTTP driver must support per-request headers.
* AC-19.3.4: The HTTP driver must support profile-provided authentication.
* AC-19.3.5: The HTTP driver must capture sanitized request and response evidence.
* AC-19.3.6: The HTTP driver must redact configured secrets from evidence.
* AC-19.3.7: The HTTP driver must support JSON path assertions.
* AC-19.3.8: The HTTP driver must support status code assertions.

## 20. Runtime Execution Specification

## 20.1 Execution Lifecycle

Execution lifecycle:

1. Load configuration.
2. Select profile.
3. Discover plugins.
4. Discover specs.
5. Parse specs.
6. Normalize specs.
7. Validate specs.
8. Resolve fixtures.
9. Generate plan.
10. Apply policies.
11. Start run session.
12. Start drivers.
13. Execute setup actions.
14. Execute flow actions.
15. Evaluate expectations.
16. Execute cleanup actions.
17. Capture final evidence.
18. Stop drivers.
19. Write results.
20. Generate reports.
21. Update run history.

### Acceptance Criteria

* AC-20.1.1: The runtime must produce a unique run ID for every run.
* AC-20.1.2: The runtime must create an artifact directory for every run.
* AC-20.1.3: The runtime must write the effective configuration into the evidence bundle.
* AC-20.1.4: The runtime must write the normalized flow into the evidence bundle.
* AC-20.1.5: The runtime must write the execution plan into the evidence bundle.
* AC-20.1.6: The runtime must record start and end timestamps for every action.
* AC-20.1.7: The runtime must stop all drivers even when a flow fails.
* AC-20.1.8: The runtime must attempt configured cleanup even when a flow fails, unless policy prevents it.
* AC-20.1.9: The runtime must record cleanup failures separately from flow failures.
* AC-20.1.10: The runtime must produce a final run result even after unexpected errors.

## 20.2 Retry Behavior

### Acceptance Criteria

* AC-20.2.1: Retries must be disabled by default for MVP.
* AC-20.2.2: Retries must be configurable at project, flow, and step level.
* AC-20.2.3: Non-retry-safe steps must not be retried automatically.
* AC-20.2.4: Retried actions must be clearly shown in the timeline.
* AC-20.2.5: Evidence must indicate all retry attempts.
* AC-20.2.6: A flow that passes only after retry must be marked as passed with retry.
* AC-20.2.7: Retry status must be available in JSON and JUnit reports.

## 20.3 Parallel Execution

### Acceptance Criteria

* AC-20.3.1: Parallel execution must be opt-in.
* AC-20.3.2: Parallel execution must isolate driver sessions by flow.
* AC-20.3.3: Parallel execution must isolate fixtures unless fixtures declare shared usage.
* AC-20.3.4: Parallel execution must preserve deterministic reporting.
* AC-20.3.5: Parallel execution must support a configurable maximum worker count.
* AC-20.3.6: Flow-level logs must not interleave in evidence files.

## 21. Evidence Specification

## 21.1 Evidence Levels

Supported evidence levels:

* `minimal`
* `standard`
* `full`
* `custom`

### Minimal

Includes:

* Run summary.
* Normalized flow.
* Plan.
* Failure details.

### Standard

Includes minimal plus:

* Step timeline.
* Screenshots on failure.
* Driver logs.
* HTTP request/response metadata.

### Full

Includes standard plus:

* Screenshots every step.
* Videos.
* Browser traces.
* Network captures.
* DOM snapshots.
* Accessibility snapshots.
* Full sanitized API transcripts.

### Acceptance Criteria

* AC-21.1.1: The evidence level must be configurable by profile.
* AC-21.1.2: The evidence level must be overridable from the CLI.
* AC-21.1.3: Minimal evidence must always include enough information to identify the flow, plan, result, and failure.
* AC-21.1.4: Standard evidence must include failure screenshots for UI drivers.
* AC-21.1.5: Full evidence must capture every supported artifact type for active drivers unless unsupported by that driver.
* AC-21.1.6: Evidence capture failures must not hide the original test failure.
* AC-21.1.7: Evidence capture failures must be reported as warnings or secondary failures.

## 21.2 Evidence Directory Structure

```text
artifacts/runs/{runId}/
  run.json
  config.effective.yaml
  flow.normalized.json
  plan.json
  timeline.json
  result.json
  logs/
  screenshots/
  videos/
  traces/
  network/
  dom/
  accessibility/
  api/
  fixtures/
  failure-analysis.md
```

### Acceptance Criteria

* AC-21.2.1: Every run must create a root artifact directory.
* AC-21.2.2: Artifact paths must be deterministic within a run.
* AC-21.2.3: Artifact filenames must be safe for Windows, macOS, and Linux.
* AC-21.2.4: Large artifacts must be referenced from report files rather than embedded.
* AC-21.2.5: The evidence bundle must include an index file.

## 22. Reporting Specification

## 22.1 HTML Report

The HTML report must include:

* Project name.
* Run ID.
* Profile.
* Environment.
* Start time.
* End time.
* Duration.
* Flow list.
* Result summary.
* Failure summary.
* Timeline.
* Step details.
* Evidence links.
* Traceability metadata.
* Fixture summary.
* Driver summary.

### Acceptance Criteria

* AC-22.1.1: The HTML report must be generated after every run when enabled.
* AC-22.1.2: The HTML report must be viewable without a server.
* AC-22.1.3: The HTML report must link to evidence artifacts using relative links.
* AC-22.1.4: The HTML report must clearly distinguish passed, failed, skipped, blocked, and errored flows.
* AC-22.1.5: The HTML report must show failed step details.
* AC-22.1.6: The HTML report must show failure classification when available.
* AC-22.1.7: The HTML report must show retries.
* AC-22.1.8: The HTML report must show cleanup failures.

## 22.2 JSON Report

The JSON report is the canonical machine-readable result.

### Acceptance Criteria

* AC-22.2.1: The JSON report must conform to a versioned schema.
* AC-22.2.2: The JSON report must include run metadata.
* AC-22.2.3: The JSON report must include flow results.
* AC-22.2.4: The JSON report must include step results.
* AC-22.2.5: The JSON report must include artifact references.
* AC-22.2.6: The JSON report must include traceability metadata.
* AC-22.2.7: The JSON report must include retry metadata.
* AC-22.2.8: The JSON report must include failure classification metadata.

## 22.3 JUnit Report

### Acceptance Criteria

* AC-22.3.1: The JUnit report must be compatible with common CI systems.
* AC-22.3.2: Each flow must map to a test case.
* AC-22.3.3: Failed flows must map to failed test cases.
* AC-22.3.4: Skipped flows must map to skipped test cases.
* AC-22.3.5: The JUnit report must include artifact paths in failure output when available.

## 22.4 Markdown Summary

### Acceptance Criteria

* AC-22.4.1: The Markdown summary must be suitable for PR comments.
* AC-22.4.2: The Markdown summary must include pass/fail counts.
* AC-22.4.3: The Markdown summary must include failed flow names.
* AC-22.4.4: The Markdown summary must include report and artifact links when available.
* AC-22.4.5: The Markdown summary must include traceability identifiers when available.

## 23. Failure Classification

Failure categories:

* `product-bug`
* `test-bug`
* `environment-issue`
* `data-issue`
* `dependency-issue`
* `timeout`
* `locator-failure`
* `assertion-failure`
* `driver-error`
* `fixture-error`
* `policy-blocked`
* `unknown`

### Acceptance Criteria

* AC-23.1: Every failed flow must receive a failure category.
* AC-23.2: Failure classification must include confidence.
* AC-23.3: Failure classification must include supporting evidence references.
* AC-23.4: Initial classification may be heuristic-based.
* AC-23.5: AI classification may augment but not replace deterministic failure metadata.
* AC-23.6: Users must be able to override failure classification in run history.
* AC-23.7: Classification overrides must be auditable.

## 24. Brownfield Onboarding Specification

## 24.1 Recorder

Post-MVP recorder workflow:

```bash
cress record --driver playwright --name login-flow
```

Recorder must capture:

* User actions.
* Navigation.
* Semantic locator candidates.
* DOM snapshots.
* Accessibility tree.
* Screenshots.
* Network calls.
* Console logs.
* Candidate assertions.

### Acceptance Criteria

* AC-24.1.1: The recorder must produce a valid draft flow.
* AC-24.1.2: The recorder must prefer semantic locators over generated selectors.
* AC-24.1.3: The recorder must mark uncertain locators as needing review.
* AC-24.1.4: The recorder must capture candidate assertions.
* AC-24.1.5: The recorder must include evidence from the recording session.
* AC-24.1.6: The recorder must not automatically mark recorded flows as stable.

## 24.2 Flow Promotion

Promotion converts low-level recordings into reusable domain steps.

```bash
cress promote recordings/login-flow.yaml --to patient.sign_in
```

### Acceptance Criteria

* AC-24.2.1: The promotion command must create or update a step manifest.
* AC-24.2.2: The promotion command must create a step implementation stub.
* AC-24.2.3: The promotion command must preserve the original recording.
* AC-24.2.4: The promotion command must mark generated code as review-required.
* AC-24.2.5: The promoted flow must validate after required review items are completed.

## 25. Model-Based Testing Specification

## 25.1 Model Format

Example:

```yaml
version: 1
id: prescription-refill
name: Prescription refill model

states:
  - Anonymous
  - Authenticated
  - ViewingMedications
  - RefillDrafted
  - RefillSubmitted
  - AwaitingReview
  - RefillApproved
  - RefillRejected

transitions:
  - name: sign-in
    from: Anonymous
    to: Authenticated
    action: patient.sign_in

  - name: view-medications
    from: Authenticated
    to: ViewingMedications
    action: medication.view_active

  - name: start-refill
    from: ViewingMedications
    to: RefillDrafted
    action: medication.start_refill

  - name: submit-refill
    from: RefillDrafted
    to: RefillSubmitted
    action: medication.submit_refill
    effects:
      - refill.request.created
```

### Acceptance Criteria

* AC-25.1.1: Model files must be versioned.
* AC-25.1.2: Model files must declare states.
* AC-25.1.3: Model files must declare transitions.
* AC-25.1.4: Transitions must reference valid states.
* AC-25.1.5: Transitions may reference registered steps.
* AC-25.1.6: Models must validate independently of flow files.

## 25.2 Model Exploration

Post-MVP command:

```bash
cress explore prescription-refill.model.yaml --strategy transition-coverage
```

Strategies:

* Happy path.
* Negative path.
* Transition coverage.
* State coverage.
* Pairwise.
* Risk-weighted.
* Random walk with seed.

### Acceptance Criteria

* AC-25.2.1: Exploration must generate executable draft flows.
* AC-25.2.2: Exploration must report model coverage.
* AC-25.2.3: Random exploration must support deterministic seeds.
* AC-25.2.4: Generated flows must be reviewable before adoption.
* AC-25.2.5: Generated flows must reference model states and transitions.

## 26. AI Assistance Specification

## 26.1 AI Use Cases

Supported AI-assisted use cases:

* Generate candidate flows from requirements.
* Generate scenario suggestions.
* Generate step stubs.
* Suggest semantic locator repairs.
* Summarize failure evidence.
* Classify failures.
* Explain execution plans.
* Suggest missing coverage.

## 26.2 AI Safety Requirements

### Acceptance Criteria

* AC-26.2.1: AI assistance must be optional.
* AC-26.2.2: AI assistance must be disabled by default unless configured.
* AC-26.2.3: AI must not silently modify authoritative specs.
* AC-26.2.4: AI must not remove expectations without explicit approval.
* AC-26.2.5: AI must not skip failing steps without explicit approval.
* AC-26.2.6: AI suggestions must be represented as suggestions, patches, or review items.
* AC-26.2.7: AI prompts must support evidence redaction.
* AC-26.2.8: AI usage must be recorded in audit metadata when enabled.
* AC-26.2.9: AI-generated changes must be traceable.

## 27. Policy Engine Specification

## 27.1 Policy File

Example:

```yaml
version: 1
policies:
  production:
    allowDestructiveActions: false
    requireReadOnlyPersonas: true
    disableDatabaseMutation: true
    requireEvidenceBundle: true
    maxRetries: 0

  ci:
    maxRetries: 1
    requireJUnitReport: true
    requireTraceabilityForTags:
      - release-gate
      - regulated
```

### Acceptance Criteria

* AC-27.1.1: Policy files must be versioned.
* AC-27.1.2: Policies must be selectable by profile.
* AC-27.1.3: Policy violations must block execution when severity is error.
* AC-27.1.4: Policy violations must be included in reports.
* AC-27.1.5: Policies must support warnings and errors.
* AC-27.1.6: Policies must support driver allowlists.
* AC-27.1.7: Policies must support destructive action restrictions.
* AC-27.1.8: Policies must support traceability requirements.
* AC-27.1.9: Policies must support evidence requirements.

## 28. Security Requirements

### Acceptance Criteria

* AC-28.1: Secrets must never be printed in full in logs.
* AC-28.2: Secrets must be redacted from evidence bundles.
* AC-28.3: Secrets must be redacted from reports.
* AC-28.4: Environment variables used as secrets must be registered as sensitive.
* AC-28.5: Plugins must receive only the secrets required for their execution.
* AC-28.6: Production profiles must be able to block destructive actions.
* AC-28.7: Reports must avoid embedding sensitive payloads by default.
* AC-28.8: HTTP evidence must support header and body redaction.
* AC-28.9: Database evidence must support column redaction.
* AC-28.10: AI assistance must support prompt redaction and data minimization.

## 29. Observability Requirements

### Acceptance Criteria

* AC-29.1: Every run must have a unique traceable run ID.
* AC-29.2: Every flow execution must have a unique flow run ID.
* AC-29.3: Every step execution must have a unique step run ID.
* AC-29.4: Timeline events must include timestamps and durations.
* AC-29.5: Runtime logs must include correlation IDs.
* AC-29.6: Driver logs must include driver session IDs.
* AC-29.7: Reports must include duration breakdowns.
* AC-29.8: OpenTelemetry export should be supported post-MVP.
* AC-29.9: Metrics should include pass count, fail count, skipped count, duration, retry count, and failure categories.

## 30. CI/CD Requirements

### Acceptance Criteria

* AC-30.1: `cress run` must be usable in CI without interactive prompts.
* AC-30.2: The CLI must support non-interactive mode.
* AC-30.3: The CLI must produce meaningful exit codes.
* AC-30.4: The CLI must generate JUnit XML.
* AC-30.5: The CLI must generate Markdown summaries suitable for PR comments.
* AC-30.6: Artifact paths must be stable enough for CI upload steps.
* AC-30.7: The CLI must support profile selection.
* AC-30.8: The CLI must support tag selection.
* AC-30.9: The CLI must support parallel execution.
* AC-30.10: The CLI must support retry configuration.

## 31. SDK Requirements

## 31.1 TypeScript SDK

### Acceptance Criteria

* AC-31.1.1: The TypeScript SDK must allow defining steps.
* AC-31.1.2: The TypeScript SDK must allow defining fixture providers.
* AC-31.1.3: The TypeScript SDK must expose typed context accessors.
* AC-31.1.4: The TypeScript SDK must support driver access.
* AC-31.1.5: The TypeScript SDK must support structured logging.
* AC-31.1.6: The TypeScript SDK must support returning structured outputs.
* AC-31.1.7: Generated TypeScript stubs must compile.

## 31.2 .NET SDK

### Acceptance Criteria

* AC-31.2.1: The .NET SDK must allow defining steps.
* AC-31.2.2: The .NET SDK must allow defining fixture providers.
* AC-31.2.3: The .NET SDK must expose typed context accessors.
* AC-31.2.4: The .NET SDK must support driver access.
* AC-31.2.5: The .NET SDK must support structured logging.
* AC-31.2.6: The .NET SDK must support returning structured outputs.
* AC-31.2.7: Generated .NET stubs must compile.
* AC-31.2.8: The .NET SDK should support dependency injection.

## 32. Dashboard Requirements

Dashboard is post-MVP but should be designed into artifact schemas.

### Dashboard Features

* Run history.
* Flow status.
* Capability coverage.
* Requirement coverage.
* Failure trends.
* Flake trends.
* Evidence viewer.
* Step registry browser.
* Fixture browser.
* Driver health.
* Model coverage.

### Acceptance Criteria

* AC-32.1: Dashboard must read from generated run artifacts or an imported run store.
* AC-32.2: Dashboard must not be required for CLI usage.
* AC-32.3: Dashboard must show pass/fail trends by flow.
* AC-32.4: Dashboard must show coverage by capability.
* AC-32.5: Dashboard must show coverage by acceptance criterion.
* AC-32.6: Dashboard must show evidence artifacts.
* AC-32.7: Dashboard must support dark and light mode.
* AC-32.8: Dashboard must support local static report mode initially.

## 33. Performance Requirements

### Acceptance Criteria

* AC-33.1: Project validation for 100 flow files should complete in under 5 seconds on a typical developer machine, excluding plugin startup.
* AC-33.2: Planning for 100 simple flows should complete in under 10 seconds on a typical developer machine, excluding plugin startup.
* AC-33.3: The runtime must avoid starting unused drivers.
* AC-33.4: The runtime must avoid loading unused plugins when possible.
* AC-33.5: Evidence capture must be configurable to avoid excessive storage use.
* AC-33.6: Large evidence files must be streamed to disk instead of fully buffered in memory when practical.

## 34. Compatibility Requirements

### Acceptance Criteria

* AC-34.1: The CLI must run on Windows.
* AC-34.2: The CLI must run on macOS.
* AC-34.3: The CLI must run on Linux.
* AC-34.4: Path handling must be cross-platform.
* AC-34.5: Artifact filenames must be cross-platform safe.
* AC-34.6: The plugin protocol must support cross-language implementations.
* AC-34.7: The project format must be source-control friendly.

## 35. Versioning Requirements

### Acceptance Criteria

* AC-35.1: All persisted schemas must be versioned.
* AC-35.2: Configuration files must be versioned.
* AC-35.3: Flow files must be versioned.
* AC-35.4: Capability files must be versioned.
* AC-35.5: Fixture files must be versioned.
* AC-35.6: Model files must be versioned.
* AC-35.7: Report files must be versioned.
* AC-35.8: The CLI must provide clear diagnostics for unsupported versions.
* AC-35.9: Migration tooling should be provided for breaking schema changes.

## 36. Error Handling Requirements

### Acceptance Criteria

* AC-36.1: All diagnostics must include severity.
* AC-36.2: Diagnostics should include file, line, and column when applicable.
* AC-36.3: Diagnostics must include actionable messages.
* AC-36.4: Diagnostics must distinguish validation errors, planning errors, runtime errors, driver errors, fixture errors, and policy errors.
* AC-36.5: Unexpected exceptions must be captured in the run result.
* AC-36.6: Unexpected exceptions must not prevent report generation when enough run context exists.

## 37. Initial Repository Structure

Recommended repository layout:

```text
Cress/
  src/
    Cress.Cli/
    Cress.Core/
    Cress.ProjectSystem/
    Cress.Specs/
    Cress.Validation/
    Cress.Planning/
    Cress.Runtime/
    Cress.Drivers.Abstractions/
    Cress.Plugins/
    Cress.Fixtures/
    Cress.Evidence/
    Cress.Reporting/
    Cress.Policy/
    Cress.Sdk.DotNet/
    Cress.Driver.Http/
    Cress.Driver.Playwright.Host/
  node/
    cress-sdk/
    cress-driver-playwright/
  schemas/
  examples/
  docs/
  tests/
    Cress.UnitTests/
    Cress.IntegrationTests/
    Cress.AcceptanceTests/
```

## 38. MVP Implementation Plan

## 38.1 Phase 1: Project and CLI Foundation

Deliver:

* CLI shell.
* Project initialization.
* Config loading.
* Profile loading.
* Validation command.
* Basic diagnostics.

Acceptance:

* `cress init` creates a valid project.
* `cress validate` validates the generated project.
* `cress config print` prints effective config.

## 38.2 Phase 2: Spec Parsing and CIR

Deliver:

* YAML flow parser.
* Markdown capability parser.
* CIR model.
* Schema validation.
* Source mapping.

Acceptance:

* YAML flows compile to CIR.
* Markdown capabilities parse into capability models.
* Invalid specs produce actionable diagnostics.

## 38.3 Phase 3: Step Registry and Planning

Deliver:

* Step manifest parser.
* Step registry.
* Plan generator.
* Missing step detection.
* `cress plan`.

Acceptance:

* Flow steps resolve against manifests.
* Missing steps are reported.
* Plans are written as JSON.

## 38.4 Phase 4: Runtime and HTTP Driver

Deliver:

* Runtime orchestrator.
* Execution session.
* HTTP driver.
* Basic expectations.
* Evidence directory.

Acceptance:

* HTTP-based flows can run.
* Request and response evidence is captured.
* Reports are generated.

## 38.5 Phase 5: Playwright Driver

Deliver:

* Playwright driver host.
* Browser actions.
* UI expectations.
* Screenshots.
* Traces.
* Videos.

Acceptance:

* Web flows can run through Playwright.
* Failure screenshots are captured.
* Browser evidence is linked in reports.

## 38.6 Phase 6: SDKs and Stub Generation

Deliver:

* TypeScript SDK.
* .NET SDK.
* Step stub generation.
* Plugin invocation.

Acceptance:

* Missing steps can generate TypeScript and .NET stubs.
* Generated stubs compile.
* Runtime can invoke SDK-backed steps.

## 38.7 Phase 7: Reporting and CI

Deliver:

* HTML report.
* JSON report.
* JUnit report.
* Markdown summary.
* CI-friendly exit codes.

Acceptance:

* Reports are generated after every run.
* JUnit integrates with CI.
* Markdown summary can be posted to PRs.

## 39. MVP Definition of Done

The MVP is complete when:

1. A user can initialize a Cress project.
2. A user can write a YAML flow.
3. A user can write a Markdown capability.
4. A user can validate the project.
5. A user can define step manifests.
6. A user can generate missing step stubs.
7. A user can implement steps in TypeScript or .NET.
8. A user can run flows using HTTP and Playwright drivers.
9. A user can capture evidence.
10. A user can generate HTML, JSON, Markdown, and JUnit reports.
11. A user can run the framework in CI.
12. A user can trace a flow to a requirement or acceptance criterion.
13. A failed flow produces enough evidence to debug the failure.
14. The generated example project demonstrates a complete passing flow.

## 40. Open Questions

1. Should the core runtime be implemented in .NET, Node, Rust, or another platform?
2. Should plugin communication use JSON-RPC, gRPC, or both?
3. Should Playwright run as an embedded dependency or external driver process?
4. Should the first SDK be .NET or TypeScript?
5. Should the dashboard be generated static HTML first or a local web app?
6. Should Cress use its own assertion language or delegate assertions to drivers and steps?
7. Should model-based testing be part of v1 or deferred until after recorder support?
8. Should Gherkin support be import-only initially?
9. Should the product support cloud/distributed execution early or remain local-first?
10. Should AI assistance be a plugin package rather than built into the core?

## 41. Guiding Architecture Principles

1. Specs are authoritative. Generated or AI-suggested artifacts are subordinate.
2. Flow intent must remain separate from execution mechanics.
3. Drivers are replaceable.
4. Step implementations are reusable.
5. Evidence is not optional for serious E2E testing.
6. Brownfield adoption must not require perfect architecture.
7. Greenfield adoption must support specification-first development.
8. The framework must be source-control native.
9. Failure output must help users act, not merely inform them that sadness occurred.
10. AI may assist, but deterministic artifacts own the truth.

## 42. Example End-to-End User Journey

```bash
cress init --template web
cress validate
cress generate steps --language dotnet
cress run --profile local
cress report open
```

Expected result:

* Project initializes.
* Specs validate.
* Missing step stubs are generated.
* User implements stubs.
* Flow runs.
* Evidence is captured.
* Report opens.
* CI can execute the same flow.

## 43. Detailed Domain Model

## 43.1 Domain Entity Overview

Cress's domain should be modeled around stable, portable concepts rather than specific automation libraries.

Primary entities:

* `Project`
* `Profile`
* `Capability`
* `AcceptanceCriterion`
* `Flow`
* `Scenario`
* `Persona`
* `Fixture`
* `FixtureInstance`
* `StepDefinition`
* `StepImplementation`
* `ExpectationDefinition`
* `DriverDefinition`
* `DriverSession`
* `PluginDefinition`
* `ExecutionPlan`
* `PlanAction`
* `Run`
* `FlowRun`
* `StepRun`
* `EvidenceArtifact`
* `Diagnostic`
* `Policy`
* `PolicyViolation`
* `FailureClassification`
* `RunReport`
* `RunHistoryRecord`

## 43.2 Project

A `Project` represents a Cress workspace.

Required fields:

* `id`
* `name`
* `rootPath`
* `configPath`
* `defaultProfile`
* `schemaVersion`

Relationships:

* Has many profiles.
* Has many capabilities.
* Has many flows.
* Has many fixtures.
* Has many step definitions.
* Has many plugin definitions.
* Has many run history records.

### Acceptance Criteria

* AC-43.2.1: A project must have one root configuration file.
* AC-43.2.2: A project must have a deterministic root path.
* AC-43.2.3: Project discovery must walk upward from the current working directory until `.cress/config.yaml` is found.
* AC-43.2.4: Project discovery must stop at filesystem root.
* AC-43.2.5: Project discovery must fail with a clear message when no project is found.
* AC-43.2.6: A project ID must be stable unless explicitly regenerated.

## 43.3 Capability

A `Capability` represents a product behavior or business function.

Required fields:

* `id`
* `name`
* `description`
* `owner`
* `risk`

Optional fields:

* `rules`
* `acceptanceCriteria`
* `examples`
* `tags`
* `links`
* `sourceFile`
* `sourceRange`

Relationships:

* Has many acceptance criteria.
* Has many flows.
* May reference one or more models.

### Acceptance Criteria

* AC-43.3.1: A capability ID must be unique within a project.
* AC-43.3.2: A capability must be discoverable from Markdown front matter.
* AC-43.3.3: A capability may exist without executable flows.
* AC-43.3.4: A capability without executable flows must appear as uncovered in coverage reports.
* AC-43.3.5: A capability must support links to external requirements systems.
* AC-43.3.6: A capability must support risk classification.

## 43.4 AcceptanceCriterion

An `AcceptanceCriterion` represents a specific observable requirement.

Required fields:

* `id`
* `text`
* `capabilityId`

Optional fields:

* `priority`
* `risk`
* `status`
* `coveredByFlows`
* `sourceRange`

Status values:

* `draft`
* `approved`
* `implemented`
* `covered`
* `deprecated`

### Acceptance Criteria

* AC-43.4.1: Acceptance criterion IDs must be unique within a capability.
* AC-43.4.2: Acceptance criteria must be linkable from flows.
* AC-43.4.3: Coverage reports must show which acceptance criteria are covered.
* AC-43.4.4: Strict validation may require every high-risk acceptance criterion to be covered.
* AC-43.4.5: Deprecated acceptance criteria must not be required for coverage.

## 43.5 Flow

A `Flow` is an executable or partially executable user journey.

Required fields:

* `id`
* `name`
* `version`
* `actions`
* `expectations`

Optional fields:

* `capabilityId`
* `summary`
* `personas`
* `fixtures`
* `tags`
* `traceability`
* `risk`
* `priority`
* `execution`
* `sourceFile`
* `sourceRange`

Flow status values:

* `draft`
* `ready`
* `blocked`
* `deprecated`

### Acceptance Criteria

* AC-43.5.1: Flow IDs must be unique within a project.
* AC-43.5.2: Flow names should be human-readable.
* AC-43.5.3: A flow may be draft and not runnable.
* AC-43.5.4: Draft flows must be excluded from default runs unless explicitly included.
* AC-43.5.5: Deprecated flows must be excluded from default runs.
* AC-43.5.6: Flow status must be visible in discovery output.

## 43.6 StepDefinition

A `StepDefinition` describes a reusable executable behavior.

Required fields:

* `name`
* `description`
* `implementation`

Optional fields:

* `aliases`
* `inputs`
* `outputs`
* `preconditions`
* `effects`
* `drivers`
* `idempotency`
* `retrySafe`
* `timeout`
* `owner`
* `version`
* `tags`

### Acceptance Criteria

* AC-43.6.1: Step names must use dotted lowercase naming by convention.
* AC-43.6.2: Step names may include hyphens only when allowed by project policy.
* AC-43.6.3: Step definitions must reference a valid implementation binding.
* AC-43.6.4: Step definitions must be discoverable without loading implementation code when possible.
* AC-43.6.5: Step definitions must support semantic search by alias and description.

## 43.7 ExecutionPlan

An `ExecutionPlan` is the complete concrete plan for one flow execution.

Required fields:

* `id`
* `flowId`
* `profile`
* `createdAt`
* `actions`
* `requiredDrivers`
* `requiredFixtures`

Optional fields:

* `policyDecisions`
* `warnings`
* `sourceMap`
* `estimatedDuration`

### Acceptance Criteria

* AC-43.7.1: Plans must be deterministic for identical inputs and environment configuration.
* AC-43.7.2: Plans must include setup actions.
* AC-43.7.3: Plans must include cleanup actions when applicable.
* AC-43.7.4: Plans must include expectation actions.
* AC-43.7.5: Plans must include required driver list.
* AC-43.7.6: Plans must include required fixture list.
* AC-43.7.7: Plans must be serializable to JSON.
* AC-43.7.8: Plans must be safe to store in evidence bundles.

## 43.8 PlanAction

A `PlanAction` is a single executable unit within a plan.

Fields:

* `id`
* `kind`
* `name`
* `sequence`
* `inputs`
* `expectedOutputs`
* `driver`
* `step`
* `timeout`
* `retryPolicy`
* `sourceRef`
* `safety`

Kinds:

* `setup`
* `action`
* `expectation`
* `observation`
* `cleanup`
* `hook`

### Acceptance Criteria

* AC-43.8.1: Every plan action must have a stable action ID within the plan.
* AC-43.8.2: Every plan action must have a sequence value.
* AC-43.8.3: Every executable plan action must resolve to a driver operation or plugin operation.
* AC-43.8.4: Plan actions must declare whether they are destructive when known.
* AC-43.8.5: Policy evaluation must inspect destructive action metadata.

## 44. Runtime State Machines

## 44.1 Run State Machine

Run states:

```text
Created
  -> Configuring
  -> Discovering
  -> Validating
  -> Planning
  -> PolicyEvaluation
  -> Starting
  -> Running
  -> CleaningUp
  -> Reporting
  -> Completed
```

Terminal states:

* `Completed`
* `Failed`
* `Cancelled`
* `Blocked`
* `Errored`

Failure transitions:

* Any non-terminal state may transition to `Errored` for unexpected runtime failure.
* `PolicyEvaluation` may transition to `Blocked`.
* `Running` may transition to `Failed`.
* `CleaningUp` may transition to `Failed` or `Errored` depending on primary result.

### Acceptance Criteria

* AC-44.1.1: Every run must record state transitions.
* AC-44.1.2: State transitions must include timestamp and reason.
* AC-44.1.3: Terminal state must be written to `run.json`.
* AC-44.1.4: Blocked runs must not execute flow actions.
* AC-44.1.5: Errored runs must still attempt report generation when possible.

## 44.2 FlowRun State Machine

Flow run states:

```text
Queued
  -> Planning
  -> WaitingForFixtures
  -> StartingDrivers
  -> Running
  -> EvaluatingExpectations
  -> CleaningUp
  -> Completed
```

Terminal states:

* `Passed`
* `PassedWithRetries`
* `Failed`
* `Skipped`
* `Blocked`
* `Errored`

### Acceptance Criteria

* AC-44.2.1: Every selected flow must produce a flow run record.
* AC-44.2.2: Skipped flows must include a skip reason.
* AC-44.2.3: Blocked flows must include policy or planning diagnostics.
* AC-44.2.4: Passed-with-retries must be distinct from passed.
* AC-44.2.5: Flow run state must be included in JSON and HTML reports.

## 44.3 StepRun State Machine

Step run states:

```text
Pending
  -> ResolvingInputs
  -> Running
  -> CapturingEvidence
  -> Completed
```

Terminal states:

* `Passed`
* `Failed`
* `Skipped`
* `TimedOut`
* `Errored`

### Acceptance Criteria

* AC-44.3.1: Every executed plan action must produce a step run or action run record.
* AC-44.3.2: Step runs must record input references.
* AC-44.3.3: Step runs must record sanitized input values when allowed.
* AC-44.3.4: Step runs must record output references.
* AC-44.3.5: Step runs must record duration.
* AC-44.3.6: Timed-out steps must be distinguishable from assertion failures.

## 45. Plugin Protocol Specification

## 45.1 Plugin Design Goals

Plugins allow Cress to support multiple languages, drivers, fixtures, and step implementations without coupling the core runtime to every ecosystem.

Plugin goals:

* Process isolation.
* Cross-language support.
* Versioned protocol.
* Health checks.
* Capability discovery.
* Structured errors.
* Structured logging.
* Safe timeout handling.
* Deterministic invocation.

## 45.2 Plugin Manifest

Example:

```yaml
version: 1
id: steps.dotnet.identity
name: Identity Steps
kind: step-provider
runtime: dotnet
entry:
  command: dotnet
  args:
    - run
    - --project
    - steps/dotnet/IdentitySteps/IdentitySteps.csproj
protocol:
  type: json-rpc
  version: 1
capabilities:
  - steps
  - fixtures
healthCheck:
  timeout: 10000
```

### Acceptance Criteria

* AC-45.2.1: Plugin manifests must be versioned.
* AC-45.2.2: Plugin IDs must be unique within a project.
* AC-45.2.3: Plugin manifests must declare plugin kind.
* AC-45.2.4: Plugin manifests must declare startup command.
* AC-45.2.5: Plugin manifests must declare protocol type and version.
* AC-45.2.6: Invalid plugin manifests must fail validation.
* AC-45.2.7: Disabled plugins must not be started.

## 45.3 Plugin Kinds

Supported plugin kinds:

* `step-provider`
* `driver`
* `fixture-provider`
* `reporter`
* `policy-provider`
* `ai-provider`
* `recorder`

### Acceptance Criteria

* AC-45.3.1: The plugin host must reject unknown plugin kinds unless experimental plugins are enabled.
* AC-45.3.2: A plugin may declare multiple kinds.
* AC-45.3.3: Plugin capabilities must be queried during discovery.
* AC-45.3.4: Plugin capability discovery must not execute test actions.

## 45.4 JSON-RPC Protocol

MVP transport should support JSON-RPC over stdio.

Required methods:

```text
cress/initialize
cress/health
cress/capabilities
cress/shutdown
```

Step provider methods:

```text
steps/list
steps/execute
```

Fixture provider methods:

```text
fixtures/list
fixtures/create
fixtures/claim
fixtures/cleanup
```

Driver methods:

```text
driver/capabilities
driver/startSession
driver/perform
driver/observe
driver/captureEvidence
driver/stopSession
```

### Acceptance Criteria

* AC-45.4.1: JSON-RPC messages must include protocol version metadata during initialization.
* AC-45.4.2: The plugin host must enforce request timeouts.
* AC-45.4.3: The plugin host must capture stderr as plugin diagnostic logs.
* AC-45.4.4: The plugin host must terminate unresponsive plugins after timeout and grace period.
* AC-45.4.5: Plugin errors must be converted into Cress diagnostics.
* AC-45.4.6: Plugin responses must be schema-validated.
* AC-45.4.7: Plugin startup failures must block only flows requiring that plugin unless the plugin is globally required.

## 45.5 Initialize Request

Example request:

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "cress/initialize",
  "params": {
    "protocolVersion": 1,
    "projectRoot": "/repo",
    "profile": "local",
    "capabilities": {
      "supportsStreamingLogs": true,
      "supportsCancellation": true
    }
  }
}
```

Example response:

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "protocolVersion": 1,
    "pluginId": "steps.dotnet.identity",
    "capabilities": ["steps"],
    "ready": true
  }
}
```

### Acceptance Criteria

* AC-45.5.1: Plugins must reject unsupported protocol versions.
* AC-45.5.2: The host must reject plugins that respond with incompatible protocol versions.
* AC-45.5.3: Initialization must complete before any capability discovery.
* AC-45.5.4: Initialization must not receive unredacted secrets unless required by plugin configuration.

## 45.6 Step Execute Request

Example:

```json
{
  "jsonrpc": "2.0",
  "id": "step-001",
  "method": "steps/execute",
  "params": {
    "runId": "run-001",
    "flowRunId": "flow-001",
    "stepRunId": "step-001",
    "stepName": "patient.sign_in",
    "inputs": {
      "persona": {
        "ref": "patient"
      }
    },
    "context": {
      "profile": "local",
      "variables": {
        "baseUrl": "http://localhost:5000"
      }
    }
  }
}
```

Example response:

```json
{
  "jsonrpc": "2.0",
  "id": "step-001",
  "result": {
    "status": "passed",
    "outputs": {
      "authenticated": true
    },
    "evidence": [
      {
        "kind": "screenshot",
        "path": "screenshots/step-001.png"
      }
    ],
    "logs": [
      {
        "level": "information",
        "message": "Patient signed in successfully."
      }
    ]
  }
}
```

### Acceptance Criteria

* AC-45.6.1: Step execution requests must include run, flow, and step correlation IDs.
* AC-45.6.2: Step execution responses must include a status.
* AC-45.6.3: Step execution responses may include outputs.
* AC-45.6.4: Step execution responses may include evidence references.
* AC-45.6.5: Step execution responses may include structured logs.
* AC-45.6.6: Failed step responses must include error details.
* AC-45.6.7: Plugin-generated evidence paths must be validated to prevent path traversal.

## 46. Built-In Action Language

## 46.1 Purpose

Cress should support direct driver actions for simple flows and bootstrapping, while encouraging promotion to domain steps for long-term maintainability.

Example:

```yaml
when:
  - action: browser.goto
    with:
      url: /login
  - action: browser.fill
    with:
      label: Email
      value: ${patient.email}
  - action: browser.click
    with:
      role: button
      name: Sign in
```

## 46.2 Built-In Browser Actions

Required MVP browser actions:

* `browser.goto`
* `browser.click`
* `browser.fill`
* `browser.select`
* `browser.check`
* `browser.uncheck`
* `browser.press`
* `browser.wait_for_url`
* `browser.wait_for_text`
* `browser.screenshot`

Required browser expectations:

* `browser.expect_visible`
* `browser.expect_hidden`
* `browser.expect_text`
* `browser.expect_url`
* `browser.expect_title`
* `browser.expect_enabled`
* `browser.expect_disabled`

### Acceptance Criteria

* AC-46.2.1: Built-in browser actions must be executable through the Playwright driver.
* AC-46.2.2: Built-in browser actions must support semantic locator inputs.
* AC-46.2.3: Built-in browser actions must support test ID locators when configured.
* AC-46.2.4: Built-in browser actions must discourage raw CSS/XPath selectors with warnings unless allowed by policy.
* AC-46.2.5: Built-in expectations must produce assertion failure diagnostics.
* AC-46.2.6: Built-in expectations must capture evidence on failure.

## 46.3 Built-In HTTP Actions

Required MVP HTTP actions:

* `http.get`
* `http.post`
* `http.put`
* `http.patch`
* `http.delete`

Required HTTP expectations:

* `http.expect_status`
* `http.expect_json_path`
* `http.expect_header`
* `http.expect_body_contains`

### Acceptance Criteria

* AC-46.3.1: HTTP actions must be executable through the HTTP driver.
* AC-46.3.2: HTTP actions must support variable substitution.
* AC-46.3.3: HTTP actions must support request body templates.
* AC-46.3.4: HTTP expectations must be attachable to the previous response by default.
* AC-46.3.5: HTTP evidence must redact sensitive values.

## 47. Variable and Expression System

## 47.1 Variable Sources

Supported variable sources:

* Project configuration.
* Profile configuration.
* Environment variables.
* Persona fields.
* Fixture outputs.
* Step outputs.
* Built-in runtime variables.
* Secret providers.

Built-in variables:

* `${run.id}`
* `${flow.id}`
* `${profile.name}`
* `${project.root}`
* `${artifacts.root}`
* `${now}`

### Acceptance Criteria

* AC-47.1.1: Variables must be resolvable in flow inputs.
* AC-47.1.2: Variables must be resolvable in fixture declarations.
* AC-47.1.3: Variables must be resolvable in driver configuration when allowed.
* AC-47.1.4: Unresolved variables must fail validation or planning before execution.
* AC-47.1.5: Secret variables must remain redacted in logs and reports.
* AC-47.1.6: Variable resolution order must be documented.

## 47.2 Expression Syntax

MVP should support simple interpolation:

```text
${patient.email}
${fixtures.medication.id}
${steps.createOrder.outputs.orderId}
```

Post-MVP may support expressions:

```text
${now.plusDays(1)}
${random.email()}
${env.RELEASE_VERSION}
```

### Acceptance Criteria

* AC-47.2.1: MVP interpolation must be deterministic.
* AC-47.2.2: Unknown variable paths must produce actionable diagnostics.
* AC-47.2.3: Expression evaluation must not allow arbitrary code execution.
* AC-47.2.4: Random values must be seedable when generated by Cress.
* AC-47.2.5: Expression output must be type-compatible with target inputs.

## 48. Locator Strategy Specification

## 48.1 Locator Priority

Browser automation must prefer stable, user-centric locators.

Recommended priority:

1. Role and accessible name.
2. Label.
3. Placeholder.
4. Text when unique and stable.
5. Configured test ID.
6. Semantic component name.
7. CSS selector.
8. XPath.
9. Image/OCR fallback.

### Acceptance Criteria

* AC-48.1.1: The Playwright driver must support role locators.
* AC-48.1.2: The Playwright driver must support label locators.
* AC-48.1.3: The Playwright driver must support test ID locators.
* AC-48.1.4: Raw CSS selectors must produce maintainability warnings by default.
* AC-48.1.5: XPath selectors must produce stronger maintainability warnings by default.
* AC-48.1.6: Policy must be able to block XPath usage.
* AC-48.1.7: Locator diagnostics must show which locator strategy failed.

## 48.2 Locator Declaration

Examples:

```yaml
locator:
  role: button
  name: Sign in
```

```yaml
locator:
  label: Email
```

```yaml
locator:
  testId: submit-refill
```

```yaml
locator:
  css: "#submit"
  allowBrittle: true
  reason: Legacy app has no accessible labels yet.
```

### Acceptance Criteria

* AC-48.2.1: Locator declarations must be schema-validated.
* AC-48.2.2: Brittle locators must support a reason field when strict policy requires it.
* AC-48.2.3: Locator failure diagnostics must include candidate alternatives when available.
* AC-48.2.4: Recorder-generated locators must include confidence values.

## 49. Traceability and Coverage Specification

## 49.1 Traceability Model

Traceability links flows to business intent.

Supported traceability fields:

* `requirement`
* `acceptanceCriteria`
* `workItem`
* `ticket`
* `risk`
* `owner`
* `system`
* `domain`
* `release`
* `externalLinks`

### Acceptance Criteria

* AC-49.1.1: Flows must support traceability metadata.
* AC-49.1.2: Capabilities must support traceability metadata.
* AC-49.1.3: Reports must display traceability metadata.
* AC-49.1.4: JSON reports must include traceability metadata.
* AC-49.1.5: Policies must be able to require traceability for selected tags.

## 49.2 Coverage Types

Coverage reports should support:

* Capability coverage.
* Acceptance criterion coverage.
* Flow execution coverage.
* Model state coverage.
* Model transition coverage.
* Driver coverage.
* Risk coverage.

### Acceptance Criteria

* AC-49.2.1: Capability coverage must show capabilities with no flows.
* AC-49.2.2: Acceptance criterion coverage must show criteria with no flows.
* AC-49.2.3: Flow execution coverage must show selected versus executed flows.
* AC-49.2.4: Risk coverage must show high-risk items lacking passing flows.
* AC-49.2.5: Model coverage must be available when models are used.

## 50. Test Selection Specification

## 50.1 Selection Inputs

Users must be able to select flows by:

* File path.
* Flow ID.
* Capability ID.
* Tag.
* Risk.
* Owner.
* Status.
* Changed files.
* Previous failure.
* Requirement ID.

Examples:

```bash
cress run --tag smoke
cress run --capability prescription-refill
cress run --risk high
cress run --failed-last-run
cress run flows/refill.flow.yaml
```

### Acceptance Criteria

* AC-50.1.1: File path selection must run only matching flows.
* AC-50.1.2: Tag selection must support multiple tags.
* AC-50.1.3: Multiple tags must support AND and OR semantics.
* AC-50.1.4: Capability selection must include flows linked to the capability.
* AC-50.1.5: Risk selection must include flows matching risk levels.
* AC-50.1.6: Previous failure selection must use run history.
* AC-50.1.7: Selection results must be printable before execution.

## 50.2 Selection Reports

### Acceptance Criteria

* AC-50.2.1: The runtime must record how flows were selected.
* AC-50.2.2: Reports must show selection criteria.
* AC-50.2.3: Reports must distinguish selected, skipped, filtered, and executed flows.

## 51. Hook System Specification

## 51.1 Hook Points

Supported hook points:

* `beforeProject`
* `afterProject`
* `beforeRun`
* `afterRun`
* `beforeFlow`
* `afterFlow`
* `beforeStep`
* `afterStep`
* `onFailure`
* `beforeCleanup`
* `afterCleanup`
* `beforeReport`
* `afterReport`

### Acceptance Criteria

* AC-51.1.1: Hooks must be configurable by project.
* AC-51.1.2: Hooks must be able to run through plugins.
* AC-51.1.3: Hook failures must have configurable behavior.
* AC-51.1.4: Hook execution must be recorded in the timeline.
* AC-51.1.5: Hooks must receive run context.
* AC-51.1.6: Hooks must not receive unredacted secrets unless explicitly configured.

## 51.2 Hook Failure Behavior

Supported behavior:

* `fail-run`
* `warn`
* `ignore`
* `skip-flow`

### Acceptance Criteria

* AC-51.2.1: Hook failure behavior must be explicit or defaulted by policy.
* AC-51.2.2: Failed required hooks must block execution.
* AC-51.2.3: Ignored hook failures must still be recorded.

## 52. Data Redaction Specification

## 52.1 Redaction Configuration

Example:

```yaml
redaction:
  headers:
    - Authorization
    - Cookie
    - Set-Cookie
  fields:
    - password
    - token
    - ssn
    - dateOfBirth
  patterns:
    - name: bearer-token
      regex: "Bearer [A-Za-z0-9._-]+"
      replacement: "Bearer <redacted>"
```

### Acceptance Criteria

* AC-52.1.1: Redaction must apply to logs.
* AC-52.1.2: Redaction must apply to evidence.
* AC-52.1.3: Redaction must apply to reports.
* AC-52.1.4: Redaction must apply before AI prompt construction.
* AC-52.1.5: Redaction rules must be testable through `cress doctor redaction` post-MVP.
* AC-52.1.6: Redaction must preserve enough structure for debugging when possible.

## 52.2 Secret Handling

### Acceptance Criteria

* AC-52.2.1: Secrets must be referenced, not embedded, in committed config.
* AC-52.2.2: Secret values must not be written to run history.
* AC-52.2.3: Secret values must not be printed by `config print` unless explicitly requested and allowed.
* AC-52.2.4: Secret values must be masked in diagnostics.
* AC-52.2.5: Plugins must receive secret references where possible instead of raw values.

## 53. Quality Gates

## 53.1 Validation Quality Gates

Quality gates are named validation policies used locally or in CI.

Example:

```yaml
qualityGates:
  release:
    require:
      - noValidationErrors
      - noUncoveredHighRiskAcceptanceCriteria
      - noDraftFlowsWithTag: release-gate
      - noBrittleLocatorsWithTag: release-gate
      - junitReportGenerated
      - evidenceLevelAtLeast: standard
```

### Acceptance Criteria

* AC-53.1.1: Quality gates must be configurable.
* AC-53.1.2: Quality gates must be executable from CLI.
* AC-53.1.3: Quality gate failures must produce clear diagnostics.
* AC-53.1.4: Quality gate results must be included in reports.
* AC-53.1.5: Quality gates must be usable in CI.

## 53.2 Example Gates

Required built-in gates:

* `noValidationErrors`
* `noMissingSteps`
* `noMissingFixtures`
* `noPolicyErrors`
* `allSelectedFlowsPassed`
* `requiredReportsGenerated`
* `requiredEvidenceCaptured`

Post-MVP gates:

* `noFlakyReleaseGateFlows`
* `noUncoveredHighRiskCriteria`
* `noBrittleLocators`
* `modelCoverageThreshold`
* `riskCoverageThreshold`

### Acceptance Criteria

* AC-53.2.1: Built-in gates must be documented.
* AC-53.2.2: Built-in gates must be available without plugins.
* AC-53.2.3: Custom gates may be provided by policy plugins post-MVP.

## 54. Example Complete Flow Package

## 54.1 Capability

```markdown
---
version: 1
id: prescription-refill
owner: PharmacyPlatform
risk: high
tags:
  - refill
  - pharmacy
---

# Capability: Patient requests prescription refill

A patient should be able to request a refill for an eligible medication without calling the pharmacy.

## Rules

- Inactive medications cannot be refilled.
- Controlled substances require manual review.
- Expired prescriptions show renewal instructions.
- Successful refill requests appear in the pharmacy review queue.

## Acceptance Criteria

### RX-1234-AC1

Given an existing patient has a refillable medication, when the patient requests a refill, then the refill request is submitted.

### RX-1234-AC2

Given a submitted refill request, then the pharmacy review queue contains the request.
```

## 54.2 Flow

```yaml
version: 1
id: patient-requests-standard-refill
name: Patient requests standard refill
status: ready
capability: prescription-refill
summary: Existing patient requests a standard non-controlled medication refill.

tags:
  - smoke
  - release-gate
  - patient
  - refill

traceability:
  requirement: RX-1234
  acceptanceCriteria:
    - RX-1234-AC1
    - RX-1234-AC2
  owner: PharmacyPlatform
  risk: high

personas:
  patient: existing-patient

fixtures:
  medication:
    use: medication.refillable
    for: patient

when:
  - step: app.open
  - step: patient.sign_in
    with:
      persona: patient
  - step: medication.request_refill
    with:
      medication: medication

then:
  - expect: patient.sees_refill_confirmation
  - expect: pharmacy.queue_contains_refill_request
  - expect: audit.event_recorded
    with:
      event: RefillRequested
      actor: patient
```

## 54.3 Step Manifest

```yaml
version: 1
steps:
  - name: app.open
    description: Open the application using the configured base URL.
    drivers:
      - playwright
    retrySafe: true
    implementation:
      plugin: steps.dotnet.app
      operation: OpenApp

  - name: patient.sign_in
    description: Sign in as a patient persona.
    inputs:
      persona:
        type: PersonaRef
        required: true
    effects:
      - browser.session.authenticated
    drivers:
      - playwright
    retrySafe: false
    implementation:
      plugin: steps.dotnet.identity
      operation: PatientSignIn

  - name: medication.request_refill
    description: Request a refill for a medication.
    inputs:
      medication:
        type: FixtureRef
        required: true
    effects:
      - refill.request.created
    drivers:
      - playwright
    retrySafe: false
    implementation:
      plugin: steps.dotnet.medication
      operation: RequestRefill
```

## 54.4 Fixture

```yaml
version: 1
fixtures:
  medication.refillable:
    type: domain.medication
    strategy: generated
    traits:
      - active
      - refillable
      - non-controlled
    cleanup: always
    provider:
      plugin: fixtures.dotnet.pharmacy
      operation: CreateMedication
```

## 54.5 Expected Plan

```json
{
  "flowId": "patient-requests-standard-refill",
  "actions": [
    {
      "kind": "setup",
      "name": "fixture.create",
      "fixture": "medication.refillable"
    },
    {
      "kind": "action",
      "name": "app.open",
      "step": "app.open"
    },
    {
      "kind": "action",
      "name": "patient.sign_in",
      "step": "patient.sign_in"
    },
    {
      "kind": "action",
      "name": "medication.request_refill",
      "step": "medication.request_refill"
    },
    {
      "kind": "expectation",
      "name": "patient.sees_refill_confirmation"
    },
    {
      "kind": "expectation",
      "name": "pharmacy.queue_contains_refill_request"
    },
    {
      "kind": "expectation",
      "name": "audit.event_recorded"
    },
    {
      "kind": "cleanup",
      "name": "fixture.cleanup",
      "fixture": "medication.refillable"
    }
  ]
}
```

## 55. Implementation Architecture Recommendation

## 55.1 Core Runtime Language

Recommended implementation path:

* Core CLI and runtime: .NET.
* Playwright driver host: Node-based plugin using Playwright directly.
* TypeScript SDK: Node package.
* .NET SDK: NuGet package.
* Plugin communication: JSON-RPC over stdio for MVP.

Rationale:

* .NET gives strong CLI, cross-platform binaries, source generation options, and enterprise adoption.
* Playwright's native ecosystem is strongest in Node, though .NET bindings are viable.
* JSON-RPC over stdio keeps plugin development simple.
* Later gRPC support can be added for long-running or remote plugins.

### Acceptance Criteria

* AC-55.1.1: Core runtime must not require Node unless a Node-based plugin is used.
* AC-55.1.2: Playwright driver may require Node for MVP if implemented as a Node plugin.
* AC-55.1.3: HTTP-only flows must run without Node.
* AC-55.1.4: .NET SDK steps must run without TypeScript SDK.
* AC-55.1.5: TypeScript SDK steps must run without .NET SDK.

## 55.2 Package Layout

NuGet packages:

* `Cress.Cli`
* `Cress.Core`
* `Cress.Sdk`
* `Cress.Driver.Http`
* `Cress.PluginHost`
* `Cress.Reporting`

NPM packages:

* `@cress/sdk`
* `@cress/driver-playwright`
* `@cress/plugin-host-node`

### Acceptance Criteria

* AC-55.2.1: CLI must be installable as a .NET global tool.
* AC-55.2.2: Node packages must be installable from npm-compatible registries.
* AC-55.2.3: Package version compatibility must be documented.
* AC-55.2.4: Plugin protocol version compatibility must be checked at runtime.

## 56. Testing Strategy for Cress Itself

## 56.1 Test Layers

Cress must test itself at multiple layers:

* Unit tests.
* Parser golden tests.
* Schema validation tests.
* Planner tests.
* Plugin protocol contract tests.
* Driver integration tests.
* CLI snapshot tests.
* Acceptance tests using Cress to run Cress examples.
* Cross-platform smoke tests.

### Acceptance Criteria

* AC-56.1.1: Parser tests must include valid and invalid specs.
* AC-56.1.2: Parser tests must verify source mapping.
* AC-56.1.3: Schema tests must verify all public schemas.
* AC-56.1.4: Planner tests must verify deterministic plan output.
* AC-56.1.5: Plugin tests must verify timeout handling.
* AC-56.1.6: Plugin tests must verify malformed response handling.
* AC-56.1.7: CLI tests must verify exit codes.
* AC-56.1.8: Acceptance tests must run example projects.

## 56.2 Golden File Tests

Golden file tests should be used for:

* YAML to CIR conversion.
* Markdown to capability model conversion.
* CIR to plan conversion.
* Plan to report summary conversion.
* Diagnostics output.

### Acceptance Criteria

* AC-56.2.1: Golden files must be easy to review.
* AC-56.2.2: Golden file updates must require explicit approval in test tooling.
* AC-56.2.3: Golden tests must normalize timestamps and machine-specific paths.

## 57. Documentation Requirements

## 57.1 Documentation Structure

Required documentation:

```text
docs/
  index.md
  getting-started.md
  concepts/
    capabilities.md
    flows.md
    steps.md
    fixtures.md
    drivers.md
    evidence.md
    reports.md
  authoring/
    yaml-flows.md
    markdown-capabilities.md
    gherkin.md
    models.md
  guides/
    greenfield-sdd.md
    brownfield-onboarding.md
    ci-cd.md
    writing-dotnet-steps.md
    writing-typescript-steps.md
    playwright-driver.md
    http-driver.md
    debugging-failures.md
    redaction-and-secrets.md
  reference/
    cli.md
    config.md
    schemas.md
    plugin-protocol.md
    policy.md
  examples/
    web-app.md
    api-and-ui.md
    desktop-app.md
```

### Acceptance Criteria

* AC-57.1.1: Documentation must include a getting started guide.
* AC-57.1.2: Documentation must include CLI reference.
* AC-57.1.3: Documentation must include schema reference.
* AC-57.1.4: Documentation must include plugin protocol reference.
* AC-57.1.5: Documentation must include at least one complete example.
* AC-57.1.6: Documentation must explain greenfield and brownfield workflows.
* AC-57.1.7: Documentation must include security and redaction guidance.

## 58. Backlog Epics

## 58.1 Epic: Core CLI and Project System

User story:

As a developer, I want to initialize and validate a Cress project so that I can adopt the framework in a source-controlled repository.

Acceptance summary:

* Project init works.
* Project discovery works.
* Config validation works.
* Profiles work.
* Diagnostics are actionable.

## 58.2 Epic: Specification Authoring

User story:

As a product-aligned tester, I want to author capabilities and flows in readable formats so that behavior specifications are understandable and executable.

Acceptance summary:

* YAML flows work.
* Markdown capabilities work.
* Specs compile to CIR.
* Source mapping works.

## 58.3 Epic: Planning

User story:

As an automation engineer, I want flows to compile into concrete execution plans so that I can inspect what will happen before running tests.

Acceptance summary:

* Step resolution works.
* Fixture resolution works.
* Missing bindings are reported.
* Plans are deterministic.

## 58.4 Epic: Execution Runtime

User story:

As a team, I want Cress to execute planned workflows reliably so that user behavior can be validated across environments.

Acceptance summary:

* Runtime sessions work.
* Drivers start and stop.
* Steps execute.
* Expectations evaluate.
* Cleanup runs.

## 58.5 Epic: Evidence and Reporting

User story:

As a developer debugging failures, I want comprehensive evidence and clear reports so that I can quickly understand what failed and why.

Acceptance summary:

* Evidence bundle created.
* HTML report generated.
* JSON report generated.
* JUnit generated.
* Markdown summary generated.

## 58.6 Epic: Plugin and SDK System

User story:

As a framework extender, I want to implement steps, drivers, and fixtures in my preferred language so that Cress can support arbitrary platforms.

Acceptance summary:

* Plugin manifest works.
* JSON-RPC host works.
* .NET SDK works.
* TypeScript SDK works.
* Plugin diagnostics work.

## 58.7 Epic: Brownfield Adoption

User story:

As a team with an existing app, I want to record and promote workflows so that I can incrementally adopt Cress without rewriting the app.

Acceptance summary:

* Recorder captures actions.
* Draft flows generated.
* Promotion creates step stubs.
* Review-required markers are created.

## 58.8 Epic: Model-Based Testing

User story:

As a quality engineer, I want to model application states and generate coverage-oriented flows so that I can validate more than manually authored happy paths.

Acceptance summary:

* Models validate.
* Flows generate from models.
* Coverage reports show state and transition coverage.

## 59. Risk Register

## 59.1 Technical Risks

| Risk                                | Impact | Mitigation                                             |
| ----------------------------------- | -----: | ------------------------------------------------------ |
| Scope becomes too broad             |   High | Keep MVP limited to YAML, Markdown, HTTP, Playwright   |
| Plugin protocol complexity          |   High | Start with JSON-RPC over stdio only                    |
| Browser driver brittleness          |   High | Prefer semantic locators and evidence-rich diagnostics |
| Fixture system becomes app-specific |   High | Keep fixture providers pluggable                       |
| AI features distract from core      | Medium | Make AI post-MVP and optional                          |
| Reports become too heavy            | Medium | Use evidence levels and relative artifact links        |
| Cross-platform path bugs            | Medium | Add path normalization tests                           |
| Parallel execution race conditions  | Medium | Make parallel opt-in and isolate fixtures              |

## 59.2 Product Risks

| Risk                                   | Impact | Mitigation                                                                  |
| -------------------------------------- | -----: | --------------------------------------------------------------------------- |
| Users see it as yet another BDD runner |   High | Lead with flow planning, evidence, and multi-boundary validation            |
| Brownfield onboarding is too hard      |   High | Prioritize recorder and promotion post-MVP                                  |
| Product users avoid specs              | Medium | Markdown capability format must be excellent                                |
| Developers bypass high-level flows     | Medium | Provide ergonomic code SDK and direct actions but nudge toward domain steps |

## 59.3 Operational Risks

| Risk                        | Impact | Mitigation                                              |
| --------------------------- | -----: | ------------------------------------------------------- |
| Evidence leaks secrets      |   High | Redaction by default, security tests                    |
| CI artifacts become massive | Medium | Configurable evidence levels and retention guidance     |
| Flaky tests undermine trust |   High | Failure classification, retry visibility, flake history |

## 60. Final Product Definition

Cress is a workflow-first E2E testing framework that converts human-readable product behavior into executable, observable, governed validation plans across arbitrary application platforms.

Its value is not that it clicks buttons.

Its value is that it proves user outcomes.

## 61. Immediate Next Spec Files To Split From This Document

This document is intentionally comprehensive. As implementation begins, it should be split into focused repository documents:

* `PRD.md`
* `SYSTEM-ARCHITECTURE.md`
* `DOMAIN-MODEL.md`
* `CLI-SPEC.md`
* `FLOW-AUTHORING-SPEC.md`
* `CAPABILITY-SPEC.md`
* `CIR-SPEC.md`
* `PLUGIN-PROTOCOL.md`
* `DRIVER-CONTRACT.md`
* `FIXTURE-SPEC.md`
* `EVIDENCE-SPEC.md`
* `REPORTING-SPEC.md`
* `POLICY-SPEC.md`
* `SECURITY-SPEC.md`
* `IMPLEMENTATION-PLAN.md`
* `ACCEPTANCE-TEST-PLAN.md`

Each split file should preserve relevant acceptance criteria and examples.
