# Designing for automation

The best Cress experience comes from designing applications with stable, intentional automation contracts.

## Web applications

Preferred locator order:

1. `testId`
2. `role` + `label`
3. `role`
4. `label`
5. `text`
6. `cssSelector`
7. `xpath`

Recommendations:

- add `data-testid` attributes to important workflow surfaces
- keep labels and accessibility roles consistent
- make login, setup, and seed-data paths deterministic
- expose test accounts or reusable fixtures

## Desktop applications

Preferred locator order:

1. `automationId`
2. `name` + `controlType`
3. `label`
4. `role`

Recommendations:

- assign `AutomationId` values deliberately and keep them stable
- avoid unnecessary window-title variance
- provide launch arguments or test modes for repeatable startup
- expose observable states instead of timing-based assumptions

## Flow design

Healthy flows are:

- small enough to diagnose quickly
- traceable back to capabilities and acceptance criteria
- portable across local and CI profiles
- evidence-rich enough to explain failures

## Team onboarding checklist

Before onboarding a system to Cress, confirm:

1. the app exposes stable locators
2. the environment can be expressed through profiles
3. test data can be reset or recreated
4. the critical user journey can be expressed as a small set of flows
5. evidence artifacts are enough to triage failures asynchronously
