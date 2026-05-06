---
version: 1
id: web-recording
owner: Platform
risk: medium
tags:
  - web
  - browser
  - recording
---

# Capability: Web Browser Recording

The Cress web recorder captures user interactions in a real browser (via Playwright CDP)
and converts them into deterministic, replay-ready flow steps.

## Locator Strategy

Web flows use a priority-ordered locator strategy (most stable first):

1. `testId` — `data-testid` attribute (explicit developer intent, most stable)
2. `role` + `label` — ARIA role combined with accessible name (semantic, resilient to DOM restructuring)
3. `role` — ARIA role alone
4. `label` — `aria-label` or associated label text
5. `text` — visible innerText (for elements with no labels)
6. `cssSelector` — synthesised CSS path (fragile, last-resort structural fallback)
7. `xpath` — DOM XPath expression (fragile)

## Step Operations

| Operation | Step Name | Description |
|-----------|-----------|-------------|
| Navigate browser | `browser.navigate` | Navigate to an absolute or relative URL |
| Click element | `ui.invoke` | Click any interactive element |
| Fill input | `ui.fill` | Type text into an input field |
| Assert text | `ui.assert-text` | Verify visible text of an element |
| Press key | `ui.press-key` | Simulate a keyboard key (Enter, Tab, Escape, etc.) |

## Acceptance Criteria

### WEB-AC1

Given a recorded web flow with `testId` locators, when replayed via the Playwright driver,
then each step resolves to the correct DOM element and completes without locator errors.

### WEB-AC2

Given a recorded web flow with `browser.navigate` steps, when replayed, then the browser
navigates to the expected URL and the subsequent steps execute against the correct page.

### WEB-AC3

Given a user types text into a form field during recording, when the inference engine
processes the keystroke stream, then only a single `ui.fill` step with the final value
is produced (keystroke-level events are debounced and collapsed).
