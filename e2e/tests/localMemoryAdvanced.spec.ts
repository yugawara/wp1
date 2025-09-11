import { test, expect } from '@playwright/test';

test.describe('ILocalStore /test-memory advanced scenarios', () => {
  // Real overlap using the same input. We assert the storage invariant: two items get added.
  test('should handle two Adds fired concurrently (race) without overwriting', async ({ page }) => {
    await page.goto('test-memory');

    // Helper to parse "Listed N items"
    async function listCount(): Promise<number> {
      await page.getByTestId('btn-list').click();
      const text = await page.getByRole('status').textContent();
      const m = text?.match(/Listed\s+(\d+)\s+items/);
      return m ? parseInt(m[1], 10) : 0;
    }

    const before = await listCount();

    // True race: second fill can overwrite the shared input before the first click handler runs.
    await Promise.all([
      (async () => {
        await page.getByTestId('title-input').fill('Quick Alpha');
        await page.getByTestId('btn-add').click();
        await expect(page.getByRole('status')).toHaveText(/Added draft:/);
      })(),
      (async () => {
        await page.getByTestId('title-input').fill('Quick Beta');
        await page.getByTestId('btn-add').click();
        await expect(page.getByRole('status')).toHaveText(/Added draft:/);
      })(),
    ]);

    const after = await listCount();

    // Storage invariant: at least two new items were added (IDs are unique).
    expect(after).toBeGreaterThanOrEqual(before + 2);
  });

  test('status text should update consistently after Put and Delete', async ({ page }) => {
    await page.goto('test-memory');

    // Use Put (fixed key 'draft:1') so Delete actually removes what we added.
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
