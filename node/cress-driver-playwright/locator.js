/**
 * Resolve a Playwright locator from a Cress step's input fields.
 *
 * Locator priority order (V2 — highest to lowest):
 *   1. testId        — page.getByTestId()  (data-testid attribute; most stable)
 *   2. role          — page.getByRole()    (ARIA role; combined with `name` when both present)
 *   3. label         — page.getByLabel()   (aria-label / associated <label> element)
 *   4. placeholder   — page.getByPlaceholder()
 *   5. text          — page.getByText()    (visible text content)
 *   6. cssSelector   — page.locator()      (raw CSS selector)
 *   7. xpath         — page.locator('xpath=...') (raw XPath expression)
 *   8. selector      — page.locator()      (raw selector string; legacy/generic fallback)
 *   9. automationId  — page.locator('[id="..."]') (desktop concept; web fallback via id attribute)
 *
 * When multiple fields are present the highest-priority one wins. Others are ignored
 * (no AND-chaining — simpler and avoids Playwright strict-mode failures on multi-match).
 *
 * Web-only note: cssSelector and xpath are fully supported here. Flawright blocks
 * them when no desktop-native locator is also present (see FlawrightRuntimeDriver).
 *
 * @param {import('playwright').Page} page
 * @param {Record<string, string>} inputs
 * @returns {import('playwright').Locator}
 */
export function locate(page, inputs) {
  // Priority 1: testId — most stable; data-testid attribute
  if (inputs.testId) {
    return page.getByTestId(inputs.testId);
  }

  // Priority 2: role — ARIA role; combined with name when both present
  if (inputs.role) {
    return page.getByRole(inputs.role, inputs.name ? { name: inputs.name } : {});
  }

  // Priority 3: label — aria-label or associated <label> text
  if (inputs.label) {
    return page.getByLabel(inputs.label);
  }

  // Priority 4: placeholder — input placeholder text
  if (inputs.placeholder) {
    return page.getByPlaceholder(inputs.placeholder);
  }

  // Priority 5: text — visible text content
  if (inputs.text) {
    return page.getByText(inputs.text);
  }

  // Priority 6: cssSelector — raw CSS selector (web-only)
  if (inputs.cssSelector) {
    return page.locator(inputs.cssSelector);
  }

  // Priority 7: xpath — raw XPath expression (web-only)
  if (inputs.xpath) {
    return page.locator(`xpath=${inputs.xpath}`);
  }

  // Priority 8: selector — legacy raw selector string
  if (inputs.selector) {
    return page.locator(inputs.selector);
  }

  // Priority 9: automationId — desktop concept; web fallback attempts id attribute match
  if (inputs.automationId) {
    return page.locator(`[id="${inputs.automationId}"]`);
  }

  throw new Error("No locator was provided. Supply testId, role, label, placeholder, text, cssSelector, xpath, selector, or automationId.");
}
