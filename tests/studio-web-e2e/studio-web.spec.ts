import fs from 'node:fs/promises';
import { spawn, type ChildProcess } from 'node:child_process';
import path from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';

const captureDocsScreenshots = process.env.CRESS_CAPTURE_DOCS_SCREENSHOTS === '1';
const docsScreenshotRoot = path.join(process.cwd(), 'docs', 'images', 'studio');
const recentWorkspaceStorageKey = 'cress.recentWorkspaces';
const seededRecentWorkspaceStorageKey = 'cress.seededRecentWorkspaces';
const companionBaseURL = process.env.CRESS_COMPANION_URL ?? 'http://127.0.0.1:7421/';
const companionPort = new URL(companionBaseURL).port || '7421';
const companionExecutable = path.join(
  process.cwd(),
  'src',
  'Cress.Companion.Windows',
  'bin',
  'Release',
  'net10.0-windows',
  'Cress.Companion.Windows.exe'
);
const studioBaseURL = 'http://127.0.0.1:5088';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(({ recentKey, seededKey }) => {
    window.localStorage.clear();
    const seeded = window.sessionStorage.getItem(seededKey);
    if (seeded) {
      window.localStorage.setItem(recentKey, seeded);
    }
  }, { recentKey: recentWorkspaceStorageKey, seededKey: seededRecentWorkspaceStorageKey });
  await page.goto('/workspace');
  await expect(page.getByTestId('studio-shell')).toBeVisible();
});

test('navigates between workspace, designer, and results using stable shell selectors', async ({ page }) => {
  await seedRecentWorkspaces(page);
  await expect(page.getByTestId('recent-workspaces-list')).toBeVisible();
  await expect(page.getByTestId('workspace-section')).toBeVisible();
  await captureDocScreenshot(page, 'landing.png', page.getByTestId('studio-shell'));

  await page.getByTestId('nav-link-designer').click();
  await expect(page).toHaveURL(/\/designer$/);
  await expect(page.getByTestId('designer-section')).toBeVisible();
  await expect(page.getByTestId('designer-section')).toContainText('Author when the workspace is ready');

  await page.getByTestId('nav-link-results').click();
  await expect(page).toHaveURL(/\/results$/);
  await expect(page.getByTestId('results-panel')).toBeVisible();
  await expect(page.getByTestId('results-panel')).toContainText('Review only when there is signal');

  await page.getByTestId('nav-link-workspace').click();
  await expect(page).toHaveURL(/\/workspace$/);
  await expect(page.getByTestId('workspace-section')).toBeVisible();
});

test('loads the suggested workspace through the in-app picker and exposes explorer flows', async ({ page }) => {
  await expect(page.getByTestId('onboarding-panel')).toBeVisible();
  await expect(page.getByTestId('suggested-workspace-panel')).toBeVisible();

  await page.getByTestId('use-suggested-workspace').click();
  await expect(page.getByTestId('workspace-path-input')).not.toHaveValue('');

  await clickUntilVisible(page, page.getByTestId('open-workspace-picker'), page.getByTestId('workspace-picker-dialog'));
  await expect(page.getByTestId('workspace-picker-location')).not.toHaveValue('');
  await page.getByTestId('workspace-picker-filter').fill('spec');
  await captureDocScreenshot(page, 'workspace-picker.png', page.getByTestId('workspace-picker-dialog'));

  await page.getByTestId('workspace-picker-load-current').click();

  await expect(page.getByTestId('workspace-picker-dialog')).toBeHidden();
  await expect(page.getByTestId('status-bar-status-text')).toContainText('Loaded');
  await page.getByTestId('nav-link-designer').click();
  await expect(page).toHaveURL(/\/designer$/);
  await expect(page.getByTestId('explorer-panel')).toBeVisible();
  await expect(page.locator('[data-testid^="explorer-flow-"]').first()).toBeVisible();
});

test('keeps the global control center available across routes after a demo loads', async ({ page }) => {
  await loadBuiltInDemo(page);

  await page.getByTestId('global-controls-toggle').click();
  await expect(page.getByTestId('global-controls-drawer')).toBeVisible();
  await expect(page.getByTestId('global-controls-actions')).toContainText('Run all');
  await expect(page.getByTestId('global-controls-run-all')).toBeEnabled();
  await expect(page.getByTestId('global-controls-run-flow')).toBeEnabled();
  await expect(page.getByTestId('global-controls-open-results')).toBeEnabled();

  await page.getByTestId('global-controls-open-results').click();
  await expect(page).toHaveURL(/\/results$/);
  await expect(page.getByTestId('results-panel')).toBeVisible();
  await expect(page.getByTestId('global-controls-drawer')).toBeHidden();

  await page.getByTestId('global-controls-toggle').click();
  await expect(page.getByTestId('global-controls-drawer')).toBeVisible();
  await expect(page.getByTestId('global-controls-monitor')).toContainText('No run or recording session is active yet.');
});

test('validates the documented Studio authoring loop and captures the reused docs screenshots', async ({ page }) => {
  await loadBuiltInDemo(page);
  await expect(page.getByTestId('runner-node-select')).toHaveValue(/local-embedded/i);
  await expect(page.getByTestId('workspace-setup-summary')).toContainText('Screenshots: On failure');
  await expect(page.getByTestId('workspace-path-readiness')).toContainText('Cress workspace detected');
  await captureDocScreenshot(page, 'project-loaded.png', page.getByTestId('studio-shell'));

  await page.getByTestId('nav-link-designer').click();
  await expect(page).toHaveURL(/\/designer$/);

  const firstFlow = page.locator('[data-testid^="explorer-flow-"]').first();
  await firstFlow.click();

  await page.getByTestId('designer-tab-flow').click();
  await expect(page.getByTestId('flow-graph')).toBeVisible();
  await expect(page.getByTestId('flow-gherkin-preview')).toBeVisible();
  await captureDocScreenshot(page, 'flow-designer.png', page.getByTestId('designer-section'));

  await page.getByTestId('designer-tab-source').click();
  await expect(page.getByTestId('designer-source-editor')).toBeVisible();
  await captureDocScreenshot(page, 'source-tab.png', page.getByTestId('designer-section'));

  await page.getByTestId('designer-tab-metrics').click();
  await expect(page.getByTestId('metrics-panel')).toBeVisible();
  await captureDocScreenshot(page, 'metrics-tab.png', page.getByTestId('designer-section'));

  await page.getByTestId('global-controls-toggle').click();
  await expect(page.getByTestId('global-controls-drawer')).toBeVisible();
  await page.getByTestId('record-button-open').click();
  await expect(page.getByTestId('recording-target-picker')).toBeVisible();
  await expect(page.getByTestId('recording-picker-panel-desktop')).toBeVisible();
  await captureDocScreenshot(page, 'desktop-recording-picker.png', page.getByTestId('recording-target-picker'));

  await page.getByTestId('recording-picker-tab-web').click();
  await expect(page.getByTestId('recording-picker-panel-web')).toBeVisible();
  await captureDocScreenshot(page, 'web-recording-picker.png', page.getByTestId('recording-target-picker'));

  await page.getByTestId('recording-picker-cancel').click();
  await expect(page.getByTestId('recording-target-picker')).toBeHidden();

  await page.getByTestId('global-controls-open-results').click();
  await expect(page).toHaveURL(/\/results$/);
  await expect(page.getByTestId('results-panel')).toBeVisible();
  await expect(page.getByTestId('results-run-filter')).toBeVisible();
  await captureDocScreenshot(page, 'results-panel.png', page.getByTestId('results-panel'));
});

test('supports the guided use-path onboarding flow before loading a workspace', async ({ page }) => {
  await page.getByTestId('startup-mode-samples').click();
  await expect(page.getByTestId('demo-workspaces-list')).toBeVisible();

  const firstUsePathButton = page.locator('[data-testid^="use-demo-path-"]').first();
  await firstUsePathButton.click();

  await expect(page.getByTestId('workspace-path-input')).toHaveValue(/smoke/i);
  await expect(page.getByTestId('workspace-section')).toContainText('Later stages stay dimmed until the workspace loads.');

  await page.getByTestId('load-project').click();
  await expect(page.getByTestId('status-bar-status-text')).toContainText('Loaded');
  await page.getByTestId('nav-link-designer').click();
  await expect(page).toHaveURL(/\/designer$/);
  await expect(page.locator('[data-testid^="explorer-flow-"]').first()).toBeVisible();
});

test('filters and manages recent workspaces before loading a project', async ({ page }) => {
  await seedRecentWorkspaces(page);

  await page.getByTestId('recent-workspace-filter').fill('web');
  await expect(page.locator('[data-testid^="recent-workspace-card-"]')).toHaveCount(1);

  await page.locator('[data-testid^="use-recent-workspace-"]').first().click();
  await expect(page.getByTestId('workspace-path-input')).toHaveValue(/web-smoke/i);

  await page.locator('[data-testid^="remove-recent-workspace-"]').first().click();
  await expect(page.getByTestId('recent-workspace-filter-empty')).toBeVisible();

  await page.getByTestId('clear-recent-workspaces').click();
  await expect(page.getByText('No recent workspaces yet.')).toBeVisible();
});

test('filters explorer content and exposes flow actions after a demo is loaded', async ({ page }) => {
  await loadBuiltInDemo(page);
  await page.getByTestId('nav-link-designer').click();
  await expect(page).toHaveURL(/\/designer$/);
  await expect(page.locator('[data-testid^="explorer-flow-"]')).toHaveCount(2);

  await page.getByTestId('explorer-filter').fill('post');
  await expect(page.locator('[data-testid^="explorer-flow-"]')).toHaveCount(1);

  const filteredFlow = page.locator('[data-testid^="explorer-flow-"]').first();
  await filteredFlow.click();

  await expect(page.getByTestId('designer-tab-flow')).toBeVisible();
  await page.getByTestId('global-controls-toggle').click();
  await expect(page.getByTestId('global-controls-run-flow')).toBeEnabled();
  await expect(page.getByTestId('global-controls-open-results')).toBeEnabled();
});

test('pairs with the desktop companion end to end through the recording picker', async ({ page }) => {
  test.skip(process.platform !== 'win32', 'Desktop companion e2e coverage is Windows-only.');

  const companion = await launchCompanion();

  try {
    await loadBuiltInDemo(page);
    await page.getByTestId('global-controls-toggle').click();
    await page.getByTestId('global-controls-drawer').getByTestId('record-button-open').click();
    await page.getByTestId('recording-picker-tab-companion').click();

    await expect(page.getByTestId('recording-picker-companion-status')).toContainText(/Desktop companion is/i);
    await expect(page.getByTestId('recording-picker-companion-targets')).toBeVisible();

    const targetRow = page
      .getByTestId('recording-picker-companion-targets')
      .locator('tbody tr')
      .filter({ hasText: /Cress/i })
      .first();

    await expect(targetRow).toBeVisible();
    await targetRow.getByTestId('recording-picker-companion-start').click();

    await expect(page.getByTestId('recording-target-picker')).toBeHidden();
    await expect(page.getByTestId('global-controls-companion')).toContainText(/1 session\(s\)|Recording/i);

    await page.getByTestId('global-controls-drawer').getByTestId('record-button-open').click();
    await page.getByTestId('recording-picker-tab-companion').click();

    const sessionTable = page.getByTestId('recording-picker-companion-sessions');
    await expect(sessionTable).toBeVisible();
    await expect(sessionTable).toContainText(/Recording/i);

    await sessionTable.getByRole('button', { name: 'Pause' }).click();
    await expect(sessionTable).toContainText(/Paused/i);

    await sessionTable.getByRole('button', { name: 'Resume' }).click();
    await expect(sessionTable).toContainText(/Recording/i);

    await sessionTable.getByRole('button', { name: 'Stop' }).click();
    await expect(page.getByTestId('recording-picker-companion-status')).toContainText(/Desktop companion is/i);
  }
  finally {
    await stopCompanion(companion);
  }
});

async function loadBuiltInDemo(page: Page): Promise<void> {
  await page.getByTestId('load-suggested-workspace').click();
  await expect(page.getByTestId('workspace-path-input')).toHaveValue(/httpbin-smoke/i);
  await expect(page.getByTestId('status-bar-status-text')).toContainText('Loaded');
}

async function clickUntilVisible(page: Page, trigger: Locator, target: Locator): Promise<void> {
  for (let attempt = 0; attempt < 12; attempt += 1) {
    await trigger.click();
    try {
      await expect(target).toBeVisible({ timeout: 1000 });
      return;
    } catch {
      await page.waitForTimeout(250);
    }
  }

  await expect(target).toBeVisible();
}

async function captureDocScreenshot(page: Page, fileName: string, target: Locator): Promise<void> {
  if (!captureDocsScreenshots) {
    return;
  }

  await fs.mkdir(docsScreenshotRoot, { recursive: true });
  await expect(target).toBeVisible();
  await target.screenshot({
    path: path.join(docsScreenshotRoot, fileName)
  });
}

async function seedRecentWorkspaces(page: Page): Promise<void> {
  const recentWorkspaces = [
    path.win32.join(process.cwd(), 'specs', 'httpbin-smoke'),
    path.win32.join(process.cwd(), 'specs', 'web-smoke')
  ];

  await page.evaluate(({ recentKey, seededKey, paths }) => {
    const serialized = JSON.stringify(paths);
    window.sessionStorage.setItem(seededKey, serialized);
    window.localStorage.setItem(recentKey, serialized);
  }, { recentKey: recentWorkspaceStorageKey, seededKey: seededRecentWorkspaceStorageKey, paths: recentWorkspaces });

  await page.reload();
  await expect(page.getByTestId('studio-shell')).toBeVisible();
}

async function launchCompanion(): Promise<ChildProcess> {
  const companion = spawn(
    companionExecutable,
    [],
    {
      cwd: process.cwd(),
      env: {
        ...process.env,
        CRESS_COMPANION_PORT: companionPort,
        CRESS_STUDIO_URL: studioBaseURL
      },
      windowsHide: true,
      stdio: 'ignore'
    }
  );

  await waitForCompanionAvailability(true);
  return companion;
}

async function stopCompanion(companion: ChildProcess): Promise<void> {
  if (companion.exitCode === null) {
    companion.kill();
  }

  await waitForCompanionAvailability(false);
}

async function waitForCompanionAvailability(shouldBeAvailable: boolean): Promise<void> {
  const healthUrl = new URL('health', companionBaseURL).toString();

  for (let attempt = 0; attempt < 40; attempt += 1) {
    try {
      const response = await fetch(healthUrl, { cache: 'no-store' });
      if (shouldBeAvailable && response.ok) {
        return;
      }
    } catch {
      if (!shouldBeAvailable) {
        return;
      }
    }

    await new Promise(resolve => setTimeout(resolve, 250));
  }

  if (shouldBeAvailable) {
    throw new Error(`Desktop companion did not come online at ${healthUrl}.`);
  }
}
