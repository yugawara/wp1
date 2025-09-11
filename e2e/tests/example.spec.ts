import { test, expect } from '@playwright/test';

test.skip('has title', async ({ page }) => {
  await page.goto('/../');

  // Expect a title "to contain" a substring.
  await expect(page).toHaveTitle(/wptest/);
});
test('has title2', async ({ page }, testInfo) => {
  console.log('Configured baseURL:', testInfo.project.use.baseURL);

  await page.goto('');

  console.log('Navigated to:', page.url());

  await expect(page).toHaveTitle(/Home/);
});

