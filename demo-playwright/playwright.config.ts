import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 90_000,
  fullyParallel: false,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://localhost:5050',
    trace: 'on',
    screenshot: 'on',
    video: 'on',
    viewport: { width: 1366, height: 850 },
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: 'dotnet run --project ../src/DataLifecycleEngine.Cli -- ui --port 5050 -c ../configs/retention-policy.example.yaml',
    cwd: __dirname,
    url: 'http://localhost:5050',
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
