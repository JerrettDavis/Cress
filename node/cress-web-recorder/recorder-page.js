/**
 * recorder-page.js
 *
 * Injected into every page frame via Playwright's context.addInitScript().
 * Runs in the browser context — NO Node.js APIs available.
 * Captures DOM interactions and forwards them to the host via window.__cressEmit().
 *
 * Design notes:
 * - All listeners are attached at the capture phase (useCapture=true) so they
 *   fire before the page's own handlers and are not suppressible by stopPropagation.
 * - input/change events are debounced: a 250ms quiet period after the last keystroke
 *   coalesces rapid typing into one fill event per element.
 * - ElementInfo extraction is dependency-free and mirrors the C# Locator shape.
 */

(function cressRecorder() {
  "use strict";

  if (window.__cressRecorderInstalled) return;
  window.__cressRecorderInstalled = true;

  // ── ElementInfo extraction ────────────────────────────────────────────────

  /**
   * Extract an ElementInfo object from a DOM element.
   * @param {Element} el
   * @returns {Object}
   */
  function extractElementInfo(el) {
    if (!el || typeof el.tagName !== "string") return null;

    const tagName = el.tagName.toLowerCase();

    // testId: data-testid attribute (most stable)
    const testId = el.getAttribute("data-testid") || el.getAttribute("data-test-id") || null;

    // role: explicit aria role, or implicit role from tag
    const role =
      el.getAttribute("role") ||
      implicitRole(el) ||
      null;

    // label: aria-label, then aria-labelledby, then associated <label>
    let label = el.getAttribute("aria-label") || null;
    if (!label) {
      const labelledById = el.getAttribute("aria-labelledby");
      if (labelledById) {
        const labelEl = document.getElementById(labelledById);
        if (labelEl) label = (labelEl.textContent || "").trim() || null;
      }
    }
    if (!label && el.id) {
      const associatedLabel = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
      if (associatedLabel) label = (associatedLabel.textContent || "").trim() || null;
    }

    // placeholder: for input/textarea
    const placeholder = el.getAttribute("placeholder") || null;

    // text: visible innerText, truncated to 80 chars
    const rawText = (el.innerText || el.textContent || "").trim();
    const text = rawText.length > 0 ? rawText.slice(0, 80) : null;

    // cssSelector: synthesised path (id > testId-attr > tag+nth)
    const cssSelector = buildCssSelector(el);

    // xpath
    const xpath = buildXPath(el);

    return { testId, role, label, text, placeholder, cssSelector, xpath, tagName };
  }

  function implicitRole(el) {
    const tag = el.tagName.toLowerCase();
    const type = (el.getAttribute("type") || "").toLowerCase();
    switch (tag) {
      case "button": return "button";
      case "a": return "link";
      case "input":
        if (type === "checkbox") return "checkbox";
        if (type === "radio") return "radio";
        if (type === "submit" || type === "button") return "button";
        return "textbox";
      case "select": return "combobox";
      case "textarea": return "textbox";
      case "nav": return "navigation";
      case "main": return "main";
      case "header": return "banner";
      case "footer": return "contentinfo";
      case "form": return "form";
      default: return null;
    }
  }

  function buildCssSelector(el) {
    if (el.id) return "#" + CSS.escape(el.id);

    const testId = el.getAttribute("data-testid");
    if (testId) return `[data-testid="${testId}"]`;

    // Walk up DOM building tag[nth-child] path — max 4 segments
    const parts = [];
    let current = el;
    for (let i = 0; i < 4 && current && current !== document.body && current !== document.documentElement; i++) {
      const tag = current.tagName.toLowerCase();
      const parent = current.parentElement;
      if (!parent) break;
      const siblings = Array.from(parent.children).filter(c => c.tagName === current.tagName);
      const idx = siblings.indexOf(current);
      parts.unshift(idx > 0 ? `${tag}:nth-of-type(${idx + 1})` : tag);
      current = parent;
    }
    return parts.join(" > ") || el.tagName.toLowerCase();
  }

  function buildXPath(el) {
    if (el.id) return `//*[@id="${el.id}"]`;
    const parts = [];
    let current = el;
    while (current && current !== document.documentElement) {
      const parent = current.parentElement;
      if (!parent) break;
      const tag = current.tagName.toLowerCase();
      const siblings = Array.from(parent.children).filter(c => c.tagName === current.tagName);
      const idx = siblings.indexOf(current) + 1;
      parts.unshift(siblings.length > 1 ? `${tag}[${idx}]` : tag);
      current = parent;
    }
    return "/" + parts.join("/");
  }

  // ── Debounce state for input/change coalescing ────────────────────────────

  /** Map from element → { timer, lastValue } */
  const fillTimers = new WeakMap();
  const FILL_DEBOUNCE_MS = 250;

  function scheduleFill(el, value) {
    const existing = fillTimers.get(el);
    if (existing) clearTimeout(existing.timer);
    const timer = setTimeout(() => {
      fillTimers.delete(el);
      emit({
        kind: "fill",
        timestamp: new Date().toISOString(),
        element: extractElementInfo(el),
        value,
        key: null,
        url: null,
      });
    }, FILL_DEBOUNCE_MS);
    fillTimers.set(el, { timer, value });
  }

  // ── Emit helper ──────────────────────────────────────────────────────────

  function emit(event) {
    if (typeof window.__cressEmit === "function") {
      window.__cressEmit(JSON.stringify(event));
    }
  }

  // ── DOM event listeners ──────────────────────────────────────────────────

  document.addEventListener("click", function (e) {
    const el = e.target;
    if (!el || !el.tagName) return;
    emit({
      kind: "click",
      timestamp: new Date().toISOString(),
      element: extractElementInfo(el),
      value: null,
      key: null,
      url: null,
    });
  }, true /* capture */);

  document.addEventListener("input", function (e) {
    const el = e.target;
    if (!el || !el.tagName) return;
    const value = el.value !== undefined ? el.value : (el.textContent || "");
    scheduleFill(el, value);
  }, true);

  document.addEventListener("change", function (e) {
    const el = e.target;
    if (!el || !el.tagName) return;
    // Flush immediately on change (blur-like semantics for select, checkbox, radio)
    const existing = fillTimers.get(el);
    if (existing) {
      clearTimeout(existing.timer);
      fillTimers.delete(el);
    }
    const value = el.value !== undefined ? el.value : (el.textContent || "");
    emit({
      kind: "change",
      timestamp: new Date().toISOString(),
      element: extractElementInfo(el),
      value,
      key: null,
      url: null,
    });
  }, true);

  document.addEventListener("keydown", function (e) {
    // Only emit non-character keys (Enter, Tab, Escape, arrows, F-keys, etc.)
    // Character keys are captured as part of the fill/change debounce
    const isActionKey =
      e.key === "Enter" ||
      e.key === "Tab" ||
      e.key === "Escape" ||
      e.key.startsWith("Arrow") ||
      e.key.startsWith("F") && e.key.length >= 2 ||
      e.key === "Backspace" ||
      e.key === "Delete" ||
      e.key === "Home" ||
      e.key === "End" ||
      e.key === "PageUp" ||
      e.key === "PageDown";
    if (!isActionKey) return;
    emit({
      kind: "keypress",
      timestamp: new Date().toISOString(),
      element: extractElementInfo(e.target),
      value: null,
      key: e.key,
      url: null,
    });
  }, true);

  document.addEventListener("submit", function (e) {
    const el = e.target;
    emit({
      kind: "submit",
      timestamp: new Date().toISOString(),
      element: extractElementInfo(el),
      value: null,
      key: null,
      url: window.location.href,
    });
  }, true);
})();
