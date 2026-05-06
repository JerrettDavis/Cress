# Designing for Cress

The easiest way to make Cress successful is to design your systems so the automation can use stable, intentional contracts instead of brittle heuristics.

## Web apps

Design for these locator priorities:

1. `testId`
2. `role` + `label`
3. `role`
4. `label`
5. `text`
6. `cssSelector`
7. `xpath`

### Recommendations

- Add `data-testid` attributes to important workflow surfaces.
- Use accessible roles and labels consistently.
- Keep auth, navigation, and data setup deterministic.
- Expose environments with seed data or reusable test accounts.
- Avoid CSS-only identity for critical elements when a semantic identifier is possible.

## Desktop apps

Design for these desktop priorities:

1. `automationId`
2. `name` + `controlType`
3. `label`
4. `role`

### Recommendations

- Assign `AutomationId` values intentionally and keep them stable.
- Avoid dynamic window titles unless they are part of the behavior under test.
- Provide startup arguments, predictable launch paths, or test modes for local/CI use.
- Keep asynchronous loading visible through states that automation can assert reliably.

## Flow design

Good Cress flows are:

- **small** enough to diagnose quickly
- **traceable** back to capabilities and acceptance criteria
- **profile-driven** instead of hardcoding environment details
- **evidence-rich** enough to explain failures without rerunning immediately

## Team design checklist

Before onboarding a system to Cress, confirm:

1. the app exposes stable locators
2. environment configuration can live in profiles
3. test data can be created or reset repeatably
4. the core user journey can be expressed as a small set of flows
5. failures can be diagnosed from screenshots, traces, or report output

## What “good” looks like

The best Cress-enabled systems let authors:

- find the right element without fragile selectors
- run the same flow locally and in CI with only profile changes
- understand failures from evidence artifacts alone
- evolve flows in source control without rerecording everything
