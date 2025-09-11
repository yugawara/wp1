import { test, expect } from '@playwright/test';

test.skip('has title', async ({ page }) => {
  await page.goto('/../');

  // Expect a title "to contain" a substring.
  await expect(page).toHaveTitle(/wptest/);
});
test('has title2', async ({ page }) => {
  await page.goto('./');

  // Expect a title "to contain" a substring.
  await expect(page).toHaveTitle(/Home/);
});

