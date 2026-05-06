/**
 * cress-web-recorder — public API entry point.
 *
 * Exports:
 *   RecordingSession  — captures user interactions in a real browser via Playwright CDP.
 *   RecordedEvent     — JSDoc typedef for the event shape (mirrors C# RecordedEvent).
 *   ElementInfo       — JSDoc typedef for element metadata (mirrors C# Locator V1 fields).
 */

import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { EventEmitter } from "node:events";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PAGE_SCRIPT_PATH = path.join(__dirname, "recorder-page.js");

// ── JSDoc typedefs ────────────────────────────────────────────────────────────

/**
 * @typedef {'click'|'fill'|'keypress'|'navigate'|'submit'|'change'} EventKind
 */

/**
 * @typedef {Object} ElementInfo
 * @property {string|null} testId       - data-testid attribute value
 * @property {string|null} role         - ARIA role (explicit or implicit)
 * @property {string|null} label        - aria-label / associated label text
 * @property {string|null} text         - visible innerText (truncated to 80 chars)
 * @property {string|null} placeholder  - input placeholder text
 * @property {string|null} cssSelector  - synthesised CSS path (fallback locator)
 * @property {string|null} xpath        - DOM XPath expression
 * @property {string|null} tagName      - lower-case HTML tag name
 */

/**
 * @typedef {Object} RecordedEvent
 * @property {EventKind}      kind       - type of interaction
 * @property {string}         timestamp  - ISO 8601 timestamp
 * @property {ElementInfo|null} element  - element metadata (null for navigate)
 * @property {string|null}    value      - new value for fill/change; null otherwise
 * @property {string|null}    key        - key name for keypress; null otherwise
 * @property {string|null}    url        - URL for navigate/submit; null otherwise
 */

// ── RecordingSession ──────────────────────────────────────────────────────────

/**
 * A recording session that captures user interactions in a browser using Playwright.
 *
 * Usage:
 * ```js
 * const session = new RecordingSession({ url: 'http://localhost:3000' });
 * session.on('event', evt => console.log(evt));
 * await session.start();
 * // user interacts...
 * const events = await session.stop();
 * ```
 *
 * The session injects a small script into every page frame that listens for DOM
 * events (click, input, change, keydown, submit) at the capture phase and forwards
 * them via an exposed Playwright binding. Navigation events are captured directly
 * from the Playwright page object (no DOM injection needed).
 */
export class RecordingSession extends EventEmitter {
  /**
   * @param {Object} options
   * @param {string} options.url           - Initial URL to navigate to
   * @param {'chromium'|'firefox'|'webkit'} [options.browserType='chromium']
   * @param {boolean} [options.headless=false]
   */
  constructor({ url, browserType = "chromium", headless = false }) {
    super();
    this._url = url;
    this._browserType = browserType;
    this._headless = headless;

    /** @type {RecordedEvent[]} */
    this._events = [];

    this._browser = null;
    this._context = null;
    this._page = null;
    this._pageScriptContent = null;
  }

  /**
   * Launches the browser, injects the recorder script, navigates to the start URL,
   * and begins capturing events.
   */
  async start() {
    // Load the page script content once
    this._pageScriptContent = await readFile(PAGE_SCRIPT_PATH, "utf8");

    // Dynamic import of playwright — allows the package to be used as a peer dep
    const pw = await _importPlaywright();
    const browserType = pw[this._browserType];
    if (!browserType) {
      throw new Error(`Unknown browserType '${this._browserType}'. Use 'chromium', 'firefox', or 'webkit'.`);
    }

    this._browser = await browserType.launch({ headless: this._headless });
    this._context = await this._browser.newContext();
    this._page = await this._context.newPage();

    // Inject the recorder script into every frame before page scripts run
    await this._context.addInitScript(this._pageScriptContent);

    // Expose binding: window.__cressEmit(jsonString) → handleEvent()
    await this._context.exposeBinding("__cressEmit", (_source, jsonString) => {
      try {
        const event = JSON.parse(jsonString);
        this._handleEvent(event);
      } catch {
        // Malformed JSON from page — ignore silently
      }
    });

    // Navigate events via Playwright's framenavigated
    this._page.on("framenavigated", frame => {
      // Only record top-level (main frame) navigations
      if (frame === this._page.mainFrame()) {
        /** @type {RecordedEvent} */
        const evt = {
          kind: "navigate",
          timestamp: new Date().toISOString(),
          element: null,
          value: null,
          key: null,
          url: frame.url(),
        };
        this._handleEvent(evt);
      }
    });

    // Navigate to the start URL
    await this._page.goto(this._url, { waitUntil: "domcontentloaded" });
  }

  /**
   * Stops the recording session, closes the browser, and returns all captured events.
   * @returns {Promise<RecordedEvent[]>}
   */
  async stop() {
    if (this._browser) {
      await this._browser.close().catch(() => {});
      this._browser = null;
      this._context = null;
      this._page = null;
    }
    return [...this._events];
  }

  /**
   * Live snapshot of events captured so far.
   * @returns {RecordedEvent[]}
   */
  get events() {
    return [...this._events];
  }

  // ── Internal ───────────────────────────────────────────────────────────────

  /**
   * @param {RecordedEvent} event
   */
  _handleEvent(event) {
    this._events.push(event);
    this.emit("event", event);
  }
}

// ── Playwright dynamic import helper ─────────────────────────────────────────

/**
 * Imports playwright dynamically (peer dependency).
 * Supports both the package being installed as a direct dep or a peer dep.
 */
async function _importPlaywright() {
  try {
    // Standard ESM import
    return await import("playwright");
  } catch {
    // Fallback: try playwright-core
    try {
      return await import("playwright-core");
    } catch {
      throw new Error(
        "playwright is not installed. Run: npm install playwright\n" +
        "or install it as a peer dependency in your project."
      );
    }
  }
}
