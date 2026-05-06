export interface EvidenceArtifact {
  category: string;
  relativePath: string;
  description?: string;
}

export interface LogSink {
  info(message: string, data?: Record<string, string>): void;
  warning(message: string, data?: Record<string, string>): void;
  error(message: string, data?: Record<string, string>): void;
}

export interface StepExecutionContext {
  flowId: string;
  stepName: string;
  artifactDirectory: string;
  baseUrl?: string;
  inputs: Record<string, string>;
  variables: Record<string, string>;
  fixtures: Record<string, string>;
  drivers: Record<string, Record<string, string>>;
  logger: LogSink;
}

export interface FixtureExecutionContext {
  flowId: string;
  fixtureAlias: string;
  fixtureName: string;
  artifactDirectory: string;
  bindings: Record<string, string>;
  variables: Record<string, string>;
  logger: LogSink;
}

export interface StepExecutionResult {
  success: boolean;
  message?: string;
  failureClassification?: string;
  outputs?: Record<string, string>;
  artifacts?: EvidenceArtifact[];
}

export interface FixtureExecutionResult {
  success: boolean;
  message?: string;
  outputs?: Record<string, string>;
  artifacts?: EvidenceArtifact[];
}

export interface StepDefinition {
  operation: string;
  execute(context: StepExecutionContext): Promise<StepExecutionResult> | StepExecutionResult;
}

export interface FixtureDefinition {
  operation: string;
  execute(context: FixtureExecutionContext): Promise<FixtureExecutionResult> | FixtureExecutionResult;
}

export interface PluginModule {
  steps?: StepDefinition[];
  fixtures?: FixtureDefinition[];
}

export const PROTOCOL_VERSION: 1;
export function defineStep(operation: string, execute: StepDefinition["execute"]): StepDefinition;
export function defineFixture(operation: string, execute: FixtureDefinition["execute"]): FixtureDefinition;
export function createPluginModule(module: PluginModule): PluginModule;
export function createStepResult(result?: StepExecutionResult): StepExecutionResult;
export function createFixtureResult(result?: FixtureExecutionResult): FixtureExecutionResult;
export function getDriverMetadata(context: StepExecutionContext, driverName: string): Record<string, string> | null;

