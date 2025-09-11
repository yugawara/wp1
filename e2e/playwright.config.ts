import { defineConfig, devices } from '@playwright/test';

const isCI = !!process.env.CI;

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: isCI,
  retries: isCI ? 2 : 0,
  workers: isCI ? 1 : undefined,
  reporter: [['html'], ['list']],
  use: {
    baseURL: process.env.BLAZOR_BASE_URL,
    trace: 'on-first-retry',
    video: 'retain-on-failure', // ✅ add this
    screenshot: 'only-on-failure', // optional, often useful
  },
  retries: 1, // ✅ required if you want the "on-first-retry" behavior

  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        // If not on CI, use the system Chrome instead of Playwright’s built-in Chromium
        channel: isCI ? undefined : 'chrome',
      },
    },


    /* Test against mobile viewports. */
    // {
    //   name: 'Mobile Chrome',
    //   use: { ...devices['Pixel 5'] },
    // },
    // {
    //   name: 'Mobile Safari',
    //   use: { ...devices['iPhone 12'] },
    // },

    /* Test against branded browsers. */
    // {
    //   name: 'Microsoft Edge',
    //   use: { ...devices['Desktop Edge'], channel: 'msedge' },
    // },
    // {
    //   name: 'Google Chrome',
    //   use: { ...devices['Desktop Chrome'], channel: 'chrome' },
    // },
  ],

  /* Run your local dev server before starting the tests */
  // webServer: {
  //   command: 'npm run start',
  //   url: 'http://localhost:3000',
  //   reuseExistingServer: !process.env.CI,
  // },
});
