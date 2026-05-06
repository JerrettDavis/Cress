import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { PROTOCOL_VERSION } from "@cress/sdk";

const pluginRoot = path.resolve(process.argv[2] ?? ".");
const plugin = await loadPlugin(pluginRoot);

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
      if (params.protocolVersion !== PROTOCOL_VERSION) {
        throw new Error(`Unsupported protocol version '${params.protocolVersion}'.`);
      }

      return {
        protocolVersion: PROTOCOL_VERSION,
        pluginId: path.basename(pluginRoot),
        capabilities: [
          plugin.steps.length > 0 ? "steps" : null,
          plugin.fixtures.length > 0 ? "fixtures" : null
        ].filter(Boolean),
        ready: true
      };
    case "cress/health":
      return { ready: true };
    case "cress/capabilities":
      return {
        steps: plugin.steps.map(step => step.operation),
        fixtures: plugin.fixtures.map(fixture => fixture.operation)
      };
    case "steps/list":
      return plugin.steps.map(step => ({ operation: step.operation }));
    case "steps/execute":
      return executeStep(params.operation, params.context ?? {});
    case "fixtures/list":
      return plugin.fixtures.map(fixture => ({ operation: fixture.operation }));
    case "fixtures/create":
    case "fixtures/cleanup":
    case "fixtures/claim":
      return executeFixture(params.operation, params.context ?? {});
    case "cress/shutdown":
      process.exitCode = 0;
      return { acknowledged: true };
    default:
      throw new Error(`Method '${method}' is not supported.`);
  }
}

async function executeStep(operation, context) {
  const handler = plugin.steps.find(step => equalsIgnoreCase(step.operation, operation));
  if (!handler) {
    throw new Error(`Step operation '${operation}' was not found.`);
  }

  const logs = [];
  const enriched = {
    ...context,
    inputs: context.inputs ?? {},
    variables: context.variables ?? {},
    fixtures: context.fixtures ?? {},
    drivers: context.drivers ?? {},
    logger: createLogger(logs)
  };

  const result = (await handler.execute(enriched)) ?? {};
  return normalizeExecutionResult(result, logs);
}

async function executeFixture(operation, context) {
  const handler = plugin.fixtures.find(fixture => equalsIgnoreCase(fixture.operation, operation));
  if (!handler) {
    throw new Error(`Fixture operation '${operation}' was not found.`);
  }

  const logs = [];
  const enriched = {
    ...context,
    bindings: context.bindings ?? {},
    variables: context.variables ?? {},
    logger: createLogger(logs)
  };

  const result = (await handler.execute(enriched)) ?? {};
  return normalizeExecutionResult(result, logs);
}

function normalizeExecutionResult(result, logs) {
  return {
    status: result.success === false ? "failed" : "passed",
    message: result.message ?? "",
    failureClassification: result.failureClassification,
    outputs: result.outputs ?? {},
    artifacts: Array.isArray(result.artifacts) ? result.artifacts : [],
    logs
  };
}

function createLogger(logs) {
  const write = (level, message, data) => {
    logs.push({
      level,
      message,
      data: data ?? {}
    });
  };

  return {
    info: (message, data) => write("information", message, data),
    warning: (message, data) => write("warning", message, data),
    error: (message, data) => write("error", message, data)
  };
}

async function loadPlugin(rootPath) {
  const packageJsonPath = path.join(rootPath, "package.json");
  const packageJson = JSON.parse(await fs.readFile(packageJsonPath, "utf8"));
  const mainFile = packageJson.main ?? "dist/index.js";
  const entryPath = path.resolve(rootPath, mainFile);
  const imported = await import(pathToFileURL(entryPath).href);
  const candidate = imported.default ?? imported.plugin ?? imported;
  return {
    steps: Array.isArray(candidate.steps) ? candidate.steps : [],
    fixtures: Array.isArray(candidate.fixtures) ? candidate.fixtures : []
  };
}

function equalsIgnoreCase(left, right) {
  return String(left).localeCompare(String(right), undefined, { sensitivity: "accent" }) === 0;
}

function writeResponse(message) {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

