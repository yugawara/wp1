// import { test, expect } from '@playwright/test';

// // Node-side env (NOT used inside page scripts)
// const wpBase    = (process.env.WP_BASE_URL || '').replace(/\/+$/, ''); // e.g. https://wp.lan
// const wpUser    = process.env.WP_USERNAME || '';
// const wpPass    = process.env.WP_APP_PASSWORD || ''; // WordPress *form* password (not app password)
// const blazorURL = process.env.BLAZOR_BASE_URL || '';

// test.describe('Login to WP (nonce) → open Blazor /wp-me and read current user', () => {
//   test.skip(!wpBase || !wpUser || !wpPass || !blazorURL,
//     'Set WP_BASE_URL, WP_USERNAME, WP_PASSWORD, BLAZOR_BASE_URL');

//   test('nonce mode end-to-end via WpMe.razor', async ({ page }) => {
//     // 1) Login to WordPress (cookie session)
//     const loginUrl = new URL('wp-login.php', wpBase + '/').toString();
//     await page.goto(loginUrl);
//     await page.fill('#user_login', wpUser);
//     await page.fill('#user_pass', wpPass);
//     await page.click('#wp-submit');

//     // landed at dashboard or redirected with admin bar present; be lenient about URL
//     await page.waitForURL(new RegExp(`${wpBase.replace(/[-/\\^$*+?.()|[\]{}]/g, '\\$&')}/wp-(admin|login\\.php\\?)`), { timeout: 15000 });

//     // 2) Prime Blazor app storage BEFORE it loads (so WPDI knows the endpoint)
//     await page.addInitScript(({ base }) => {
//       // runs at document start for next navigations; localStorage is origin-scoped
//       localStorage.setItem('wpEndpoint', base);   // your app reads this
//       // auth mode will come from query param: ?auth=nonce
//     }, { base: wpBase });

//     // 3) Go to Blazor page using nonce mode
//     const target = new URL('wp-me?auth=nonce', (blazorURL || '').replace(/\/+$/, '') + '/');
//     await page.goto(target.toString());

//     // 4) Expect the Razor page to render current user via WPDI nonce flow
//     const ok = page.getByTestId('wp-me-ok');
//     await expect(ok).toBeVisible({ timeout: 20000 });
//     await expect(ok).toContainText('id:');
//     await expect(ok).toContainText('name:');
//   });
// });
