import { defineConfig, devices } from '@playwright/test';

const port = 5088;
const baseURL = `http://127.0.0.1:${port}`;
const companionBaseURL = process.env.CRESS_COMPANION_URL ?? 'http://127.0.0.1:7421/';
const dotnet = '"C:\\Program Files\\dotnet\\dotnet.exe"';
const captureDocsScreenshots = process.env.CRESS_CAPTURE_DOCS_SCREENSHOTS === '1';

export default defineConfig({
  testDir: 'tests/studio-web-e2e',
  outputDir: 'artifacts/playwright',
  fullyParallel: false,
  workers: 1,
  timeout: 60000,
  expect: {
    timeout: 10000
  },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL,
    browserName: 'chromium',
    headless: true,
    screenshot: captureDocsScreenshots ? 'on' : 'only-on-failure',
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    testIdAttribute: 'data-testid'
  },
  webServer: {
    command: `${dotnet} run --project src\\Cress.Studio.Web\\Cress.Studio.Web.csproj --configuration Debug --no-launch-profile --urls ${baseURL}`,
    env: {
      ...process.env,
      CRESS_COMPANION_URL: companionBaseURL
    },
    url: baseURL,
    reuseExistingServer: false,
    timeout: 120000
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        viewport: captureDocsScreenshots
          ? { width: 1600, height: 1200 }
          : devices['Desktop Chrome'].viewport
      }
    }
  ]
});
