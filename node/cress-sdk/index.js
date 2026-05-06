export const PROTOCOL_VERSION = 1;

export function defineStep(operation, execute) {
  return { operation, execute };
}

export function defineFixture(operation, execute) {
  return { operation, execute };
}

export function createPluginModule({ steps = [], fixtures = [] } = {}) {
  return { steps, fixtures };
}

export function createStepResult({
  success = true,
  message = undefined,
  failureClassification = undefined,
  outputs = {},
  artifacts = []
} = {}) {
  return {
    success,
    message,
    failureClassification,
    outputs,
    artifacts
  };
}

export function createFixtureResult({
  success = true,
  message = undefined,
  outputs = {},
  artifacts = []
} = {}) {
  return {
    success,
    message,
    outputs,
    artifacts
  };
}

export function getDriverMetadata(context, driverName) {
  return context?.drivers?.[driverName] ?? null;
}

