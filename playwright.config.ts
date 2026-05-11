import { defineConfig, devices } from '@playwright/test';

const port = 5076;
const baseURL = `http://127.0.0.1:${port}`;
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
    command: `${dotnet} run --project src\\Cress.Studio.Web\\Cress.Studio.Web.csproj --configuration Release --no-launch-profile --urls ${baseURL}`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120000
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome']
      }
    }
  ]
});
