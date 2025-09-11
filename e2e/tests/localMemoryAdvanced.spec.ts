import { test, expect } from '@playwright/test';

test.describe('ILocalStore /test-memory advanced scenarios', () => {
  // Deterministic concurrency via a single Razor button that triggers two concurrent writes.
  test('should add two items concurrently from a single race button', async ({ page }) => {
    await page.goto('test-memory');

    // Trigger internal race
    await page.getByTestId('btn-add-race').click();

    // List and assert both payloads landed
    await page.getByTestId('btn-list').click();
    await expect(page.getByTestId('draft-list')).toContainText('Quick Alpha');
    await expect(page.getByTestId('draft-list')).toContainText('Quick Beta');
  });

  test('status text should update consistently after Put and Delete', async ({ page }) => {
    await page.goto('test-memory');

    // Use Put so Delete removes the same fixed id (draft:1)
    await page.getByTestId('title-input').fill('CountMe');
    await page.getByTestId('btn-put').click();
    await expect(page.getByRole('status')).toHaveText('Put/Upserted!');

    await page.getByTestId('btn-list').click();
    await expect(page.getByRole('status')).toHaveText(/Listed 1 items/);

    await page.getByTestId('btn-delete').click();
    await expect(page.getByRole('status')).toHaveText('Deleted!');

    await page.getByTestId('btn-list').click();
    await expect(page.getByRole('status')).toHaveText(/Listed 0 items/);
  });

  test('a11y sanity: controls and status are discoverable by role', async ({ page }) => {
    await page.goto('test-memory');

    // Make status non-empty first so it's visible.
    await page.getByTestId('btn-list').click();
    await expect(page.getByRole('status')).toHaveText(/Listed \d+ items/);

    await expect(page.getByRole('status')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Put' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'GetById' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'List' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Delete' })).toBeVisible();

    // New race button also present
    await expect(page.getByRole('button', { name: 'Add Race' })).toBeVisible();
  });

  test('flake guard: List status should change after Add', async ({ page }) => {
    await page.goto('test-memory');

    await page.getByTestId('btn-list').click();
    const before = await page.getByRole('status').textContent();

    await page.getByTestId('title-input').fill('Flaky Item');
    await page.getByTestId('btn-add').click();
    await page.getByTestId('btn-list').click();

    const after = await page.getByRole('status').textContent();
    expect(after).not.toBe(before);
  });
});
