import { test, expect } from '@playwright/test';

test('has title', async ({ page }) => {
  await page.goto('/');

  // Expect a title "to contain" a substring.
  await expect(page).toHaveTitle(/wptest/);
});
test.skip('has title2', async ({ page }) => {
  await page.goto('/blazorapp');

  // Expect a title "to contain" a substring.
  await expect(page).toHaveTitle(/Home/);
});

