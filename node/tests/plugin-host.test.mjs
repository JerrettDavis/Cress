import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const hostScript = path.join(repoRoot, "node", "cress-plugin-host-node", "host.js");
const samplePluginRoot = path.join(repoRoot, "node", "tests", "fixtures", "sample-plugin");

test("plugin host executes a step over JSON-RPC", async () => {
  const child = spawn(process.execPath, [hostScript, samplePluginRoot], {
    cwd: repoRoot,
    stdio: ["pipe", "pipe", "pipe"]
  });

  const readMessage = createReader(child.stdout);
  const readError = createReader(child.stderr);

  const initialize = await invoke(child, readMessage, {
    jsonrpc: "2.0",
    id: "1",
    method: "cress/initialize",
    params: {
      protocolVersion: 1,
      projectRoot: repoRoot,
      profile: "local",
      capabilities: {}
    }
  });

  assert.equal(initialize.result.ready, true);

  const execute = await invoke(child, readMessage, {
    jsonrpc: "2.0",
    id: "2",
    method: "steps/execute",
    params: {
      operation: "Execute",
      context: {
        flowId: "flow-1",
        stepName: "sample.step",
        artifactDirectory: path.join(repoRoot, "node", "tests", "artifacts"),
        inputs: { text: "hello" },
        variables: {},
        fixtures: {},
        drivers: {}
      }
    }
  });

  assert.equal(execute.result.status, "passed");
  assert.equal(execute.result.outputs.upper, "HELLO");

  await invoke(child, readMessage, {
    jsonrpc: "2.0",
    id: "3",
    method: "cress/shutdown",
    params: {}
  });

  child.stdin.end();
  await new Promise((resolve, reject) => {
    child.once("exit", code => code === 0 ? resolve() : reject(new Error(`Host exited with ${code}: ${readError.readAll()}`)));
    child.once("error", reject);
  });
});

function createReader(stream) {
  stream.setEncoding("utf8");
  let buffer = "";
  const waiters = [];

  stream.on("data", chunk => {
    buffer += chunk;
    let newlineIndex = buffer.indexOf("\n");
    while (newlineIndex >= 0) {
      const line = buffer.slice(0, newlineIndex).trim();
      buffer = buffer.slice(newlineIndex + 1);
      if (line.length > 0) {
        const waiter = waiters.shift();
        if (waiter) {
          waiter.resolve(JSON.parse(line));
        }
      }

      newlineIndex = buffer.indexOf("\n");
    }
  });

  stream.on("error", error => {
    const waiter = waiters.shift();
    if (waiter) {
      waiter.reject(error);
    }
  });

  return {
    next() {
      return new Promise((resolve, reject) => waiters.push({ resolve, reject }));
    },
    readAll() {
      return buffer;
    }
  };
}

async function invoke(child, reader, message) {
  child.stdin.write(`${JSON.stringify(message)}\n`);
  return reader.next();
}

