import { test, expect } from '@playwright/test';

// Use Node env only on the test side (not in page scripts)
const wpUser   = process.env.WP_USERNAME || '';
const wpAppPwd = process.env.WP_APP_PASSWORD || '';

test.describe('Blazor Razor page /wp-me via WPDI (AppPass mode)', () => {
  test.skip(!wpUser || !wpAppPwd,
    'WP_USERNAME, WP_APP_PASSWORD must be set');

  test('renders current user on /wp-me', async ({ page, baseURL }) => {
    // 1) Prime localStorage BEFORE the app loads
    await page.addInitScript(({ user, pass }) => {
      localStorage.setItem('app_user',   user);
      localStorage.setItem('app_pass',   pass);
    }, { user: wpUser, pass: wpAppPwd });

    // 2) Navigate to the Razor page with auth=apppass and wpurl
    // const target = new URL(`wp-me?auth=apppass&wpurl=${encodeURIComponent(wpBase)}`, baseURL );
    // await page.goto(target.toString());
    // 2) Navigate to the Razor page with auth=apppass (robust URL join)
    await page.goto('wp-me?auth=apppass');

    // 3) Expect the page to show the current user
    const ok = page.getByTestId('wp-me-ok');
    await expect(ok).toBeVisible({ timeout: 15000 });
    await expect(ok).toContainText('id:');
    await expect(ok).toContainText('name:');
  });
});
