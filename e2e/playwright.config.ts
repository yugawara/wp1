// playwright.config.ts
import { defineConfig, devices } from '@playwright/test';

const isCI = !!process.env.CI;

// --- Require mandatory env vars ---
if (!process.env.WP_USERNAME || !process.env.WP_APP_PASSWORD) {
  throw new Error(
    '❌ Missing WordPress credentials: WP_USERNAME and WP_APP_PASSWORD must be set in environment.'
  );
}

// Centralize defaults (env can still override)
const DEFAULT_BASE_URL = 'http://localhost:8080'; // fallback for local runs

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: isCI,
  workers: isCI ? 1 : undefined,
  retries: isCI ? 2 : 1,
  reporter: [['html'], ['list']],

  use: {
    baseURL: process.env.WP_BASE_URL ?? DEFAULT_BASE_URL,
    trace: 'on-first-retry',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ignoreHTTPSErrors: isCI,

    // Only keep these two as you requested
    wpUser: process.env.WP_USERNAME,
    wpAppPwd: process.env.WP_APP_PASSWORD,
  } as any,

  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        channel: isCI ? undefined : 'chrome',
      },
    },
  ],
});
