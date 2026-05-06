/**
 * web-recorder.test.mjs
 *
 * Tests for cress-web-recorder's RecordingSession class and recorder-page.js.
 * Run with: node --test node/tests/web-recorder.test.mjs
 *
 * Note on test strategy for DOM injection:
 *   Playwright's addInitScript runs in an isolated "utility world" and its
 *   event listeners don't receive events from the main world when using
 *   page.setContent(). For unit tests we inject the script via page.goto()
 *   (data: URL) which triggers a real navigation and runs addInitScript in
 *   the main world — matching production behaviour.
 */

import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";
import { readFile } from "node:fs/promises";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..", "..");
const recorderRoot = path.join(repoRoot, "node", "cress-web-recorder");

// ── Import helpers ────────────────────────────────────────────────────────────

async function importRecorder() {
  const mod = await import(pathToFileURL(path.join(recorderRoot, "index.js")).href);
  return mod;
}

async function loadPageScript() {
  return readFile(path.join(recorderRoot, "recorder-page.js"), "utf8");
}

// ── Test 1: RecordingSession class can be instantiated ────────────────────────

test("RecordingSession instantiates without error", async () => {
  const { RecordingSession } = await importRecorder();
  const session = new RecordingSession({ url: "http://localhost:9999" });
  assert.ok(session, "session should be truthy");
  assert.equal(typeof session.start, "function", "start should be a function");
  assert.equal(typeof session.stop, "function", "stop should be a function");
  assert.equal(typeof session.on, "function", "on should be a function");
  assert.deepEqual(session.events, [], "events should start empty");
});

// ── Test 2: RecordingSession emits 'event' via EventEmitter ──────────────────

test("RecordingSession emits events via on()", async () => {
  const { RecordingSession } = await importRecorder();
  const session = new RecordingSession({ url: "http://localhost:9999" });
  const received = [];
  session.on("event", evt => received.push(evt));

  session._handleEvent({ kind: "click", timestamp: "2026-01-01T00:00:00.000Z", element: null, value: null, key: null, url: null });
  session._handleEvent({ kind: "navigate", timestamp: "2026-01-01T00:00:01.000Z", element: null, value: null, key: null, url: "http://example.com" });

  assert.equal(received.length, 2);
  assert.equal(received[0].kind, "click");
  assert.equal(received[1].kind, "navigate");
  assert.equal(received[1].url, "http://example.com");
});

// ── Test 3: events getter returns immutable snapshots ─────────────────────────

test("RecordingSession.events returns a snapshot array", async () => {
  const { RecordingSession } = await importRecorder();
  const session = new RecordingSession({ url: "http://localhost:9999" });
  session._handleEvent({ kind: "click", timestamp: new Date().toISOString(), element: null, value: null, key: null, url: null });

  const snapshot1 = session.events;
  session._handleEvent({ kind: "fill", timestamp: new Date().toISOString(), element: null, value: "hello", key: null, url: null });
  const snapshot2 = session.events;

  assert.equal(snapshot1.length, 1, "first snapshot should have 1 event");
  assert.equal(snapshot2.length, 2, "second snapshot should have 2 events");
  assert.equal(snapshot1.length, 1, "snapshot1 should not be mutated");
});

// ── Test 4: recorder-page.js content checks ──────────────────────────────────

test("recorder-page.js has valid syntax and installs guard", async () => {
  const scriptContent = await loadPageScript();
  assert.ok(scriptContent.length > 100, "recorder-page.js should be non-trivial");
  assert.ok(scriptContent.includes("window.__cressRecorderInstalled"), "should guard against double-install");
  assert.ok(scriptContent.includes("window.__cressEmit"), "should call __cressEmit");
  assert.ok(scriptContent.includes(", true"), "should use capture phase");
  // Verify all event kinds are handled
  assert.ok(scriptContent.includes('"click"'), "should handle click");
  assert.ok(scriptContent.includes('"fill"'), "should handle fill");
  assert.ok(scriptContent.includes('"keypress"'), "should handle keypress");
  assert.ok(scriptContent.includes('"submit"'), "should handle submit");
});

// ── Tests 5–9: in-browser DOM injection via Playwright ────────────────────────
// Use page.goto() with data: URLs so addInitScript runs in the main world
// (matching real-recorder behaviour). page.setContent() doesn't trigger
// a navigation so addInitScript events would be in an isolated world.

let pw;
try {
  pw = await import("playwright");
} catch {
  pw = null;
}

const playwrightAvailable = pw !== null;

/**
 * Helper: inject script via addInitScript, navigate via data: URL, capture events.
 * @param {string} bodyHtml  - HTML body content
 * @param {(page: import('playwright').Page, captured: object[]) => Promise<void>} fn
 */
async function withPageCapture(bodyHtml, fn) {
  const scriptContent = await loadPageScript();
  const browser = await pw.chromium.launch({ headless: true });
  try {
    const context = await browser.newContext();
    const captured = [];
    // exposeFunction must come BEFORE addInitScript
    await context.exposeFunction("__cressEmit", (json) => {
      try { captured.push(JSON.parse(json)); } catch { /* ignore malformed */ }
    });
    await context.addInitScript(scriptContent);
    const page = await context.newPage();
    // Use goto with data: URL — triggers real navigation so addInitScript
    // runs in the main world and DOM events propagate correctly
    const encoded = encodeURIComponent(`<html><body>${bodyHtml}</body></html>`);
    await page.goto(`data:text/html,${encoded}`);
    await fn(page, captured);
  } finally {
    await browser.close();
  }
}

test("ElementInfo extracts testId from data-testid attribute", { skip: !playwrightAvailable }, async () => {
  await withPageCapture(
    `<button id="btn1" data-testid="submit-btn" aria-label="Submit form">Submit</button>`,
    async (page, captured) => {
      await page.click("#btn1");
      await page.waitForTimeout(150);

      const clickEvt = captured.find(e => e.kind === "click");
      assert.ok(clickEvt, `should have captured a click event, got: ${JSON.stringify(captured)}`);
      assert.equal(clickEvt.element?.testId, "submit-btn");
      assert.equal(clickEvt.element?.label, "Submit form");
      assert.equal(clickEvt.element?.tagName, "button");
      assert.equal(clickEvt.element?.role, "button");
    }
  );
});

test("ElementInfo extracts aria-label and implicit role from button", { skip: !playwrightAvailable }, async () => {
  await withPageCapture(
    `<button aria-label="Close dialog">X</button>`,
    async (page, captured) => {
      await page.click("button");
      await page.waitForTimeout(150);

      const clickEvt = captured.find(e => e.kind === "click");
      assert.ok(clickEvt, `should have captured a click event, got: ${JSON.stringify(captured)}`);
      assert.equal(clickEvt.element?.label, "Close dialog");
      assert.equal(clickEvt.element?.role, "button");
      assert.equal(clickEvt.element?.tagName, "button");
    }
  );
});

test("fill event emitted after debounce when typing into an input", { skip: !playwrightAvailable }, async () => {
  await withPageCapture(
    `<input id="email" type="email" data-testid="email-input" placeholder="Email address" />`,
    async (page, captured) => {
      await page.fill("#email", "test@example.com");
      // Wait for 250ms debounce + buffer
      await page.waitForTimeout(450);

      const fillEvts = captured.filter(e => e.kind === "fill");
      assert.ok(
        fillEvts.length >= 1,
        `should have at least one fill event, got: ${JSON.stringify(captured.map(e => e.kind))}`
      );
      const last = fillEvts[fillEvts.length - 1];
      assert.ok(last.value?.includes("test@"), `value should include 'test@', got: ${last.value}`);
      assert.equal(last.element?.testId, "email-input");
      assert.equal(last.element?.placeholder, "Email address");
    }
  );
});

test("keypress event emitted for Enter key", { skip: !playwrightAvailable }, async () => {
  await withPageCapture(
    `<input id="search" type="text" data-testid="search-input" />`,
    async (page, captured) => {
      await page.focus("#search");
      await page.keyboard.press("Enter");
      await page.waitForTimeout(150);

      const keypressEvt = captured.find(e => e.kind === "keypress" && e.key === "Enter");
      assert.ok(keypressEvt, `should have an Enter keypress event, got: ${JSON.stringify(captured)}`);
      assert.equal(keypressEvt.key, "Enter");
    }
  );
});

test("RecordedEvent shape has all required fields for a click", { skip: !playwrightAvailable }, async () => {
  await withPageCapture(
    `<button data-testid="ok-btn" aria-label="OK">OK</button>`,
    async (page, captured) => {
      await page.click("button");
      await page.waitForTimeout(150);

      const evt = captured.find(e => e.kind === "click");
      assert.ok(evt, `should have a click event, got: ${JSON.stringify(captured)}`);

      // Top-level shape
      for (const field of ["kind", "timestamp", "element", "value", "key", "url"]) {
        assert.ok(field in evt, `top-level field '${field}' required`);
      }

      // Element shape — mirrors C# Locator V1 fields
      const el = evt.element;
      for (const field of ["testId", "role", "label", "text", "placeholder", "cssSelector", "xpath", "tagName"]) {
        assert.ok(field in el, `element field '${field}' required`);
      }

      // testId / role values
      assert.equal(el.testId, "ok-btn");
      assert.equal(el.role, "button");
      assert.equal(el.label, "OK");

      // Timestamp is valid ISO 8601
      assert.ok(!isNaN(Date.parse(evt.timestamp)), "timestamp should be valid ISO 8601");
    }
  );
});
