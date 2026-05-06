import fs from "node:fs/promises";
import path from "node:path";
import { chromium, firefox, webkit, errors } from "playwright";
import { locate } from "./locator.js";

const sessions = new Map();
let sessionSequence = 0;

process.stdin.setEncoding("utf8");
let buffer = "";
process.stdin.on("data", chunk => {
  buffer += chunk;
  let newlineIndex = buffer.indexOf("\n");
  while (newlineIndex >= 0) {
    const line = buffer.slice(0, newlineIndex).trim();
    buffer = buffer.slice(newlineIndex + 1);
    if (line.length > 0) {
      void handleMessage(line);
    }

    newlineIndex = buffer.indexOf("\n");
  }
});

async function handleMessage(line) {
  let message;
  try {
    message = JSON.parse(line);
  } catch (error) {
    writeResponse({ jsonrpc: "2.0", id: null, error: { code: -32700, message: `Invalid JSON: ${error.message}` } });
    return;
  }

  try {
    const result = await dispatch(message.method, message.params ?? {});
    writeResponse({ jsonrpc: "2.0", id: message.id ?? null, result });
  } catch (error) {
    writeResponse({
      jsonrpc: "2.0",
      id: message.id ?? null,
      error: {
        code: -32000,
        message: error instanceof Error ? error.message : String(error)
      }
    });
  }
}

async function dispatch(method, params) {
  switch (method) {
    case "cress/initialize":
      if (params.protocolVersion !== 1) {
        throw new Error(`Unsupported protocol version '${params.protocolVersion}'.`);
      }

      return {
        protocolVersion: 1,
        pluginId: "@cress/driver-playwright",
        capabilities: ["driver"],
        ready: true
      };
    case "cress/health":
      return { ready: true };
    case "driver/capabilities":
      return {
        browserTypes: ["chromium", "firefox", "webkit"],
        actions: [
          "open",
          "goto",
          "navigate",
          "fill",
          "click",
          "select",
          "check",
          "uncheck",
          "press",
          "wait_for_url",
          "wait_for_text",
          "screenshot",
          "assert-url",
          "assert-text",
          "expect_visible",
          "expect_hidden",
          "expect_title",
          "expect_enabled",
          "expect_disabled"
        ]
      };
    case "driver/startSession":
      return startSession(params);
    case "driver/perform":
      return performAction(params);
    case "driver/captureEvidence":
      return captureEvidence(params.sessionId);
    case "driver/stopSession":
      return stopSession(params.sessionId);
    case "cress/shutdown":
      for (const sessionId of [...sessions.keys()]) {
        await stopSession(sessionId);
      }

      return { acknowledged: true };
    default:
      throw new Error(`Method '${method}' is not supported.`);
  }
}

async function startSession(params) {
  const profile = params.profile ?? {};
  const browserName = String(profile.playwright?.browser ?? "chromium").toLowerCase();
  const browserType = selectBrowser(browserName);
  const headless = profile.playwright?.headless !== false;
  const evidenceMode = String(profile.evidence?.mode ?? "standard").toLowerCase();
  const screenshotPolicy = String(profile.evidence?.screenshotPolicy ?? "on-failure").toLowerCase();
  const sessionId = `playwright-${++sessionSequence}`;
  const tracePath = path.join(params.artifactRoot, "traces", `${sanitizeFileName(params.flowId)}-trace.zip`);
  const consolePath = path.join(params.artifactRoot, "logs", `${sanitizeFileName(params.flowId)}-browser-console.json`);
  const networkPath = path.join(params.artifactRoot, "network", `${sanitizeFileName(params.flowId)}-network.json`);

  await fs.mkdir(path.dirname(tracePath), { recursive: true });
  await fs.mkdir(path.dirname(consolePath), { recursive: true });
  await fs.mkdir(path.dirname(networkPath), { recursive: true });
  await fs.mkdir(path.join(params.artifactRoot, "videos"), { recursive: true });

  const browser = await browserType.launch({ headless });
  const context = await browser.newContext({
    baseURL: profile.baseUrl ?? undefined,
    recordVideo: evidenceMode === "full" ? { dir: path.join(params.artifactRoot, "videos") } : undefined
  });

  if (profile.timeouts?.driver) {
    context.setDefaultTimeout(Number(profile.timeouts.driver));
  }

  await context.tracing.start({ screenshots: true, snapshots: true });
  const page = await context.newPage();
  const consoleEntries = [];
  const networkEntries = [];
  page.on("console", message => {
    consoleEntries.push({
      type: message.type(),
      text: message.text(),
      location: message.location()
    });
  });
  page.on("response", response => {
    networkEntries.push({
      url: response.url(),
      status: response.status(),
      ok: response.ok()
    });
  });

  sessions.set(sessionId, {
    flowId: params.flowId,
    artifactRoot: params.artifactRoot,
    baseUrl: profile.baseUrl ?? undefined,
    evidenceMode,
    screenshotPolicy,
    browser,
    context,
    page,
    tracePath,
    consolePath,
    networkPath,
    consoleEntries,
    networkEntries,
    finalized: false,
    sequence: 0
  });

  return {
    sessionId,
    metadata: {
      kind: "node-playwright",
      browser: browserName,
      headless: String(headless),
      baseUrl: profile.baseUrl ?? ""
    }
  };
}

async function performAction(params) {
  const session = getSession(params.sessionId);
  const action = params.action ?? {};
  const operation = normalizeOperation(action.operation ?? action.name);
  const inputs = action.inputs ?? {};

  try {
    switch (operation) {
      case "open":
      case "goto":
      case "navigate":
      case "browser.goto":
        await session.page.goto(resolveTargetUrl(session, inputs), { waitUntil: "load" });
        return await passed(session, action, `Opened ${session.page.url()}.`);
      case "fill":
      case "browser.fill":
        await locate(session.page, inputs).fill(requireInput(inputs, "value"));
        return await passed(session, action, "Filled field.");
      case "click":
      case "browser.click":
        await locate(session.page, inputs).click();
        return await passed(session, action, "Clicked target.");
      case "select":
      case "browser.select":
        await locate(session.page, inputs).selectOption(requireInput(inputs, "value"));
        return await passed(session, action, "Selected option.");
      case "check":
      case "browser.check":
        await locate(session.page, inputs).check();
        return await passed(session, action, "Checked target.");
      case "uncheck":
      case "browser.uncheck":
        await locate(session.page, inputs).uncheck();
        return await passed(session, action, "Unchecked target.");
      case "press":
      case "browser.press":
        await locate(session.page, inputs).press(requireInput(inputs, "key"));
        return await passed(session, action, "Pressed key.");
      case "wait_for_url":
      case "browser.wait_for_url":
        await session.page.waitForURL(requireInput(inputs, "url"));
        return await passed(session, action, `Observed URL ${session.page.url()}.`);
      case "wait_for_text":
      case "browser.wait_for_text":
        await session.page.getByText(requireInput(inputs, "text")).waitFor();
        return await passed(session, action, `Observed text '${inputs.text}'.`);
      case "screenshot":
      case "browser.screenshot":
        return {
          status: "passed",
          message: "Captured screenshot.",
          artifacts: [await captureScreenshot(session, action.name ?? "browser.screenshot", "step")]
        };
      case "assert-url":
      case "browser.expect_url":
      case "expect_url":
        return session.page.url() === requireAnyInput(inputs, ["equals", "url"])
          ? await passed(session, action, `Current URL matched '${requireAnyInput(inputs, ["equals", "url"])}'.`)
          : await assertionFailed(session, action, `Expected URL '${requireAnyInput(inputs, ["equals", "url"])}', but found '${session.page.url()}'.`);
      case "assert-text":
      case "browser.expect_text":
      case "expect_text": {
        const expected = requireInput(inputs, "text");
        const actualText = await session.page.locator("body").innerText();
        return actualText.includes(expected)
          ? await passed(session, action, `Observed text '${expected}'.`)
          : await assertionFailed(session, action, `Expected text '${expected}' was not present in the page.`);
      }
      case "expect_visible":
      case "browser.expect_visible":
        return await locate(session.page, inputs).isVisible()
          ? await passed(session, action, "Element is visible.")
          : await assertionFailed(session, action, "Expected element to be visible.");
      case "expect_hidden":
      case "browser.expect_hidden":
        return await locate(session.page, inputs).isHidden()
          ? await passed(session, action, "Element is hidden.")
          : await assertionFailed(session, action, "Expected element to be hidden.");
      case "expect_title":
      case "browser.expect_title": {
        const expected = requireInput(inputs, "title");
        const actual = await session.page.title();
        return actual === expected
          ? await passed(session, action, `Page title matched '${expected}'.`)
          : await assertionFailed(session, action, `Expected page title '${expected}', but found '${actual}'.`);
      }
      case "expect_enabled":
      case "browser.expect_enabled":
        return await locate(session.page, inputs).isEnabled()
          ? await passed(session, action, "Element is enabled.")
          : await assertionFailed(session, action, "Expected element to be enabled.");
      case "expect_disabled":
      case "browser.expect_disabled":
        return await locate(session.page, inputs).isDisabled()
          ? await passed(session, action, "Element is disabled.")
          : await assertionFailed(session, action, "Expected element to be disabled.");
      default:
        return failed(`Playwright operation '${action.operation}' is not supported.`, "unsupported-playwright-operation");
    }
  } catch (error) {
    const failureClassification = classifyError(error);
    const screenshot = shouldCaptureFailureScreenshot(session, failureClassification)
      ? await captureScreenshot(session, action.name ?? "playwright-action", "failure")
      : null;
    return {
      status: "failed",
      message: error instanceof Error ? error.message : String(error),
      failureClassification,
      artifacts: screenshot ? [screenshot] : []
    };
  }
}

async function captureEvidence(sessionId) {
  const session = getSession(sessionId);
  if (session.finalized) {
    return { artifacts: [] };
  }

  session.finalized = true;
  const artifacts = [];
  await session.context.tracing.stop({ path: session.tracePath });
  if (session.evidenceMode === "full") {
    artifacts.push(toArtifact(session.tracePath, "traces", "Playwright trace"));
  }

  await fs.writeFile(session.consolePath, JSON.stringify(session.consoleEntries, null, 2));
  await fs.writeFile(session.networkPath, JSON.stringify(session.networkEntries, null, 2));
  artifacts.push(toArtifact(session.consolePath, "logs", "Browser console log"));
  artifacts.push(toArtifact(session.networkPath, "network", "Network log"));

  const video = session.page.video();
  await session.page.close();
  await session.context.close();
  if (session.evidenceMode === "full" && video) {
    const videoPath = await video.path();
    artifacts.push(toArtifact(videoPath, "videos", "Playwright video"));
  }

  return { artifacts };
}

async function stopSession(sessionId) {
  const session = sessions.get(sessionId);
  if (!session) {
    return { acknowledged: true };
  }

  sessions.delete(sessionId);
  if (!session.finalized) {
    await session.context.tracing.stop({ path: session.tracePath });
    await session.page.close().catch(() => {});
    await session.context.close().catch(() => {});
  }

  await session.browser.close().catch(() => {});
  return { acknowledged: true };
}

function selectBrowser(browserName) {
  switch (browserName) {
    case "firefox":
      return firefox;
    case "webkit":
      return webkit;
    case "chromium":
    default:
      return chromium;
  }
}

function resolveTargetUrl(session, inputs) {
  if (inputs.url) {
    return inputs.url;
  }

  if (!inputs.path) {
    return session.page.url() || session.baseUrl || "http://localhost";
  }

  try {
    return new URL(inputs.path, session.baseUrl || session.page.url() || "http://localhost").toString();
  } catch {
    return inputs.path;
  }
}

async function captureScreenshot(session, actionName, suffix) {
  const relativePath = path.join("screenshots", `${String(++session.sequence).padStart(3, "0")}-${sanitizeFileName(actionName)}-${suffix}.png`);
  const absolutePath = path.join(session.artifactRoot, relativePath);
  await fs.mkdir(path.dirname(absolutePath), { recursive: true });
  await session.page.screenshot({ path: absolutePath, fullPage: true });
  return {
    category: "screenshots",
    relativePath,
    description: "Playwright screenshot"
  };
}

function classifyError(error) {
  if (error instanceof errors.TimeoutError) {
    return "timeout";
  }

  const message = error instanceof Error ? error.message : String(error);
  if (/locator|strict mode/i.test(message)) {
    return "locator";
  }

  if (/navigation|net::|ERR_/i.test(message)) {
    return "navigation";
  }

  if (/expect|assert|Expected/i.test(message)) {
    return "assertion-failed";
  }

  return "playwright-error";
}

async function passed(session, action, message) {
  const artifacts = shouldCaptureEveryStepScreenshot(session) && !/screenshot/i.test(String(action?.name ?? action?.operation ?? ""))
    ? [await captureScreenshot(session, action?.name ?? action?.operation ?? "playwright-action", "step")]
    : [];
  return { status: "passed", message, artifacts };
}

function failed(message, failureClassification) {
  return { status: "failed", message, failureClassification, artifacts: [] };
}

// assertionFailed: like failed() but also captures a screenshot when the policy is on-assertion-failure.
async function assertionFailed(session, action, message) {
  const screenshot = shouldCaptureFailureScreenshot(session, "assertion-failed")
    ? await captureScreenshot(session, action.name ?? "assertion-failure", "failure").catch(() => null)
    : null;
  return {
    status: "failed",
    message,
    failureClassification: "assertion-failed",
    artifacts: screenshot ? [screenshot] : []
  };
}

function shouldCaptureEveryStepScreenshot(session) {
  return session.screenshotPolicy === "every-step";
}

function shouldCaptureFailureScreenshot(session, failureClassification) {
  if (session.screenshotPolicy === "off" || session.screenshotPolicy === "never") {
    return false;
  }

  if (session.screenshotPolicy === "on-assertion-failure") {
    return failureClassification === "assertion-failed";
  }

  // on-failure (default) and every-step both capture on failure
  return true;
}

function requireInput(inputs, name) {
  if (!inputs[name]) {
    throw new Error(`Required input '${name}' was not supplied.`);
  }

  return inputs[name];
}

function requireAnyInput(inputs, names) {
  for (const name of names) {
    if (inputs[name]) {
      return inputs[name];
    }
  }

  throw new Error(`One of [${names.join(", ")}] must be supplied.`);
}

function getSession(sessionId) {
  const session = sessions.get(sessionId);
  if (!session) {
    throw new Error(`Session '${sessionId}' was not found.`);
  }

  return session;
}

function sanitizeFileName(value) {
  return String(value).replace(/[^A-Za-z0-9._-]+/g, "-");
}

function normalizeOperation(operation) {
  return String(operation).trim().toLowerCase().replace(/_/g, "_");
}

function toArtifact(absolutePath, category, description) {
  return {
    category,
    relativePath: path.relative(path.dirname(path.dirname(absolutePath)), absolutePath).replace(/\\/g, "/"),
    description
  };
}

function writeResponse(message) {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}
