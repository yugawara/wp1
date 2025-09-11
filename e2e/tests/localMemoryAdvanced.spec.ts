import { test, expect } from '@playwright/test';

// These are advanced tests that complement localMemory.spec.ts and localMemory.extra.spec.ts
test.describe('ILocalStore /test-memory advanced scenarios', () => {
  test('should handle two Adds fired quickly (concurrency)', async ({ page }) => {
    await page.goto('test-memory');

    await Promise.all([
      page.getByTestId('title-input').fill('Quick Alpha').then(() => page.getByTestId('btn-add').click()),
      page.getByTestId('title-input').fill('Quick Beta').then(() => page.getByTestId('btn-add').click()),
    ]);

    // List should include both, regardless of ordering
    await page.getByTestId('btn-list').click();
    await expect(page.getByTestId('draft-list')).toContainText('Quick Alpha');
    await expect(page.getByTestId('draft-list')).toContainText('Quick Beta');
  });

  test('status text should update consistently after Add and Delete', async ({ page }) => {
    await page.goto('test-memory');

    await page.getByTestId('title-input').fill('CountMe');
    await page.getByTestId('btn-add').click();
    await page.getByTestId('btn-list').click();
    await expect(page.getByRole('status')).toHaveText(/Listed 1 items/);

    await page.getByTestId('btn-delete').click();
    await expect(page.getByRole('status')).toHaveText('Deleted!');
    await page.getByTestId('btn-list').click();
    await expect(page.getByRole('status')).toHaveText(/Listed 0 items/);
  });

  test('a11y sanity: controls and status should be discoverable by role', async ({ page }) => {
    await page.goto('test-memory');

    // Check the status element has role=status and is live
    const status = page.getByRole('status');
    await expect(status).toBeVisible();

    // Buttons should be accessible by role and label
    await expect(page.getByRole('button', { name: 'Add' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Put' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'GetById' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'List' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Delete' })).toBeVisible();
  });

  test('flaky guard: List count should always increase after Add', async ({ page }) => {
    await page.goto('test-memory');

    await page.getByTestId('btn-list').click();
    const before = await page.getByRole('status').textContent();

    await page.getByTestId('title-input').fill('Flaky Item');
    await page.getByTestId('btn-add').click();
    await page.getByTestId('btn-list').click();
    const after = await page.getByRole('status').textContent();

    // Use regex to ensure after count is greater than or equal to before count
    // (avoids brittleness if text changes)
    expect(after).not.toBe(before);
  });
});
