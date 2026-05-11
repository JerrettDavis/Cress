import fs from 'node:fs/promises';
import path from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';

const captureDocsScreenshots = process.env.CRESS_CAPTURE_DOCS_SCREENSHOTS === '1';
const docsScreenshotRoot = path.join(process.cwd(), 'docs', 'images', 'studio');
const recentWorkspaceStorageKey = 'cress.recentWorkspaces';
const seededRecentWorkspaceStorageKey = 'cress.seededRecentWorkspaces';

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
  await page.getByTestId('demo-filter').fill('browser');
  await page.getByTestId('runner-node-filter').fill('local');
  await captureDocScreenshot(page, 'landing.png', page.getByTestId('studio-shell'));

  await page.getByTestId('nav-link-designer').click();
  await expect(page).toHaveURL(/\/designer$/);
  await expect(page.getByTestId('designer-section')).toBeVisible();
  await expect(page.getByTestId('designer-tab-overview')).toBeVisible();

  await page.getByTestId('nav-link-results').click();
  await expect(page).toHaveURL(/\/results$/);
  await expect(page.getByTestId('results-panel')).toBeVisible();
  await expect(page.getByTestId('results-live-headline')).toContainText('No run in progress');

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
  await expect(page.getByTestId('explorer-panel')).toBeVisible();
  await expect(page.locator('[data-testid^="explorer-flow-"]').first()).toBeVisible();
});

test('validates the documented Studio authoring loop and captures the reused docs screenshots', async ({ page }) => {
  await loadBuiltInDemo(page);
  await expect(page.getByTestId('runner-node-select')).toHaveValue(/local-embedded/i);
  await expect(page.getByTestId('workspace-setup-summary')).toContainText('Screenshots: On failure');
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

  await page.getByTestId('record-button-open').click();
  await expect(page.getByTestId('recording-target-picker')).toBeVisible();
  await expect(page.getByTestId('recording-picker-panel-desktop')).toBeVisible();
  await captureDocScreenshot(page, 'desktop-recording-picker.png', page.getByTestId('recording-target-picker'));

  await page.getByTestId('recording-picker-tab-web').click();
  await expect(page.getByTestId('recording-picker-panel-web')).toBeVisible();
  await captureDocScreenshot(page, 'web-recording-picker.png', page.getByTestId('recording-target-picker'));

  await page.getByTestId('recording-picker-cancel').click();
  await expect(page.getByTestId('recording-target-picker')).toBeHidden();

  await page.getByTestId('nav-link-results').click();
  await expect(page).toHaveURL(/\/results$/);
  await expect(page.getByTestId('results-panel')).toBeVisible();
  await expect(page.getByTestId('results-run-filter')).toBeVisible();
  await captureDocScreenshot(page, 'results-panel.png', page.getByTestId('results-panel'));
});

test('supports the guided use-path onboarding flow before loading a workspace', async ({ page }) => {
  await expect(page.getByTestId('demo-workspaces-list')).toBeVisible();

  const firstUsePathButton = page.locator('[data-testid^="use-demo-path-"]').first();
  await firstUsePathButton.click();

  await expect(page.getByTestId('workspace-path-input')).toHaveValue(/smoke/i);
  await expect(page.getByTestId('explorer-empty')).toBeVisible();

  await page.getByTestId('load-project').click();
  await expect(page.getByTestId('status-bar-status-text')).toContainText('Loaded');
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
  await expect(page.locator('[data-testid^="explorer-flow-"]')).toHaveCount(2);

  await page.getByTestId('explorer-filter').fill('post');
  await expect(page.locator('[data-testid^="explorer-flow-"]')).toHaveCount(1);

  const filteredFlow = page.locator('[data-testid^="explorer-flow-"]').first();
  await filteredFlow.click();

  await expect(page.getByTestId('designer-tab-flow')).toBeVisible();
  await expect(page.getByTestId('run-flow')).toBeEnabled();
  await expect(page.getByTestId('open-selected-file')).toBeEnabled();

  await page.getByTestId('more-actions').click();
  await expect(page.getByTestId('new-flow')).toBeEnabled();
});

async function loadBuiltInDemo(page: Page): Promise<void> {
  await clickUntilVisible(page, page.getByTestId('quick-load-first-demo'), page.locator('[data-testid^="explorer-flow-"]').first());
  await expect(page.getByTestId('workspace-path-input')).toHaveValue(/httpbin-smoke/i);
  await expect(page.locator('[data-testid^="explorer-flow-"]').first()).toBeVisible();
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
