/**
 * Tests for the Playwright driver's locate() function.
 *
 * The locate() function is pure — it maps input fields to Playwright page calls.
 * We stub the page object with a minimal mock that records which method was called
 * and what arguments it received, then assert the expected strategy was chosen.
 *
 * Priority order under test (V2 spec):
 *   1. testId
 *   2. role  (+ name when present)
 *   3. label
 *   4. placeholder
 *   5. text
 *   6. cssSelector
 *   7. xpath
 *   8. selector
 *   9. automationId
 */

import assert from "node:assert/strict";
import test from "node:test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { locate } from "../cress-driver-playwright/locator.js";

// ---------------------------------------------------------------------------
// Minimal page stub that records the last call made to it.
// ---------------------------------------------------------------------------
function makePageStub() {
  const sentinel = {};
  const stub = {
    _lastMethod: null,
    _lastArgs: null,
    _result: sentinel,

    getByTestId(value) { stub._lastMethod = "getByTestId"; stub._lastArgs = [value]; return sentinel; },
    getByRole(role, opts) { stub._lastMethod = "getByRole"; stub._lastArgs = [role, opts]; return sentinel; },
    getByLabel(value) { stub._lastMethod = "getByLabel"; stub._lastArgs = [value]; return sentinel; },
    getByPlaceholder(value) { stub._lastMethod = "getByPlaceholder"; stub._lastArgs = [value]; return sentinel; },
    getByText(value) { stub._lastMethod = "getByText"; stub._lastArgs = [value]; return sentinel; },
    locator(selector) { stub._lastMethod = "locator"; stub._lastArgs = [selector]; return sentinel; },
  };
  return { stub, sentinel };
}

// ---------------------------------------------------------------------------
// PL-L-1: testId → getByTestId (priority 1)
// ---------------------------------------------------------------------------
test("locate: testId → getByTestId", () => {
  const { stub, sentinel } = makePageStub();
  const result = locate(stub, { testId: "submit-btn" });

  assert.equal(stub._lastMethod, "getByTestId");
  assert.deepEqual(stub._lastArgs, ["submit-btn"]);
  assert.equal(result, sentinel);
});

// ---------------------------------------------------------------------------
// PL-L-2: role only → getByRole without name option
// ---------------------------------------------------------------------------
test("locate: role only → getByRole(role, {})", () => {
  const { stub } = makePageStub();
  locate(stub, { role: "button" });

  assert.equal(stub._lastMethod, "getByRole");
  assert.equal(stub._lastArgs[0], "button");
  assert.deepEqual(stub._lastArgs[1], {});
});

// ---------------------------------------------------------------------------
// PL-L-3: role + name → getByRole with name option
// ---------------------------------------------------------------------------
test("locate: role + name → getByRole(role, { name })", () => {
  const { stub } = makePageStub();
  locate(stub, { role: "button", name: "Submit" });

  assert.equal(stub._lastMethod, "getByRole");
  assert.equal(stub._lastArgs[0], "button");
  assert.deepEqual(stub._lastArgs[1], { name: "Submit" });
});

// ---------------------------------------------------------------------------
// PL-L-4: label → getByLabel (priority 3)
// ---------------------------------------------------------------------------
test("locate: label → getByLabel", () => {
  const { stub } = makePageStub();
  locate(stub, { label: "Email address" });

  assert.equal(stub._lastMethod, "getByLabel");
  assert.deepEqual(stub._lastArgs, ["Email address"]);
});

// ---------------------------------------------------------------------------
// PL-L-5: cssSelector → locator (priority 6)
// ---------------------------------------------------------------------------
test("locate: cssSelector → locator(cssSelector)", () => {
  const { stub } = makePageStub();
  locate(stub, { cssSelector: ".btn-primary" });

  assert.equal(stub._lastMethod, "locator");
  assert.deepEqual(stub._lastArgs, [".btn-primary"]);
});

// ---------------------------------------------------------------------------
// PL-L-6: xpath → locator('xpath=...') (priority 7)
// ---------------------------------------------------------------------------
test("locate: xpath → locator('xpath=...')", () => {
  const { stub } = makePageStub();
  locate(stub, { xpath: "//button[@id='ok']" });

  assert.equal(stub._lastMethod, "locator");
  assert.deepEqual(stub._lastArgs, ["xpath=//button[@id='ok']"]);
});

// ---------------------------------------------------------------------------
// PL-L-7: automationId → locator('[id="..."]') (priority 9, web fallback)
// ---------------------------------------------------------------------------
test("locate: automationId → locator('[id=\"...\"]')", () => {
  const { stub } = makePageStub();
  locate(stub, { automationId: "myButton" });

  assert.equal(stub._lastMethod, "locator");
  assert.deepEqual(stub._lastArgs, ['[id="myButton"]']);
});

// ---------------------------------------------------------------------------
// PL-L-8: testId beats role when both present (priority 1 wins over 2)
// ---------------------------------------------------------------------------
test("locate: testId + role → testId wins (priority 1 over 2)", () => {
  const { stub } = makePageStub();
  locate(stub, { testId: "submit-btn", role: "button" });

  assert.equal(stub._lastMethod, "getByTestId");
  assert.deepEqual(stub._lastArgs, ["submit-btn"]);
});

// ---------------------------------------------------------------------------
// PL-L-9: role beats label when both present (priority 2 over 3)
// ---------------------------------------------------------------------------
test("locate: role + label → role wins (priority 2 over 3)", () => {
  const { stub } = makePageStub();
  locate(stub, { role: "textbox", label: "Email" });

  assert.equal(stub._lastMethod, "getByRole");
  assert.equal(stub._lastArgs[0], "textbox");
});

// ---------------------------------------------------------------------------
// PL-L-10: cssSelector beats xpath when both present (priority 6 over 7)
// ---------------------------------------------------------------------------
test("locate: cssSelector + xpath → cssSelector wins (priority 6 over 7)", () => {
  const { stub } = makePageStub();
  locate(stub, { cssSelector: ".btn", xpath: "//button" });

  assert.equal(stub._lastMethod, "locator");
  assert.deepEqual(stub._lastArgs, [".btn"]);
});

// ---------------------------------------------------------------------------
// PL-L-11: text beats cssSelector when both present (priority 5 over 6)
// ---------------------------------------------------------------------------
test("locate: text + cssSelector → text wins (priority 5 over 6)", () => {
  const { stub } = makePageStub();
  locate(stub, { text: "Click me", cssSelector: ".btn" });

  assert.equal(stub._lastMethod, "getByText");
  assert.deepEqual(stub._lastArgs, ["Click me"]);
});

// ---------------------------------------------------------------------------
// PL-L-12: no locator provided → throws descriptive error
// ---------------------------------------------------------------------------
test("locate: no locator → throws with helpful message", () => {
  const { stub } = makePageStub();

  assert.throws(
    () => locate(stub, {}),
    (err) => {
      assert.ok(err instanceof Error);
      assert.ok(err.message.includes("testId"), `Missing 'testId' in: ${err.message}`);
      assert.ok(err.message.includes("cssSelector"), `Missing 'cssSelector' in: ${err.message}`);
      assert.ok(err.message.includes("automationId"), `Missing 'automationId' in: ${err.message}`);
      return true;
    }
  );
});
