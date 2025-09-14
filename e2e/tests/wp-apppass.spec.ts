import { test, expect } from '@playwright/test';

const wpUser   = process.env.WP_USERNAME || '';
const wpAppPwd = process.env.WP_APP_PASSWORD || '';

test.describe('WordPress REST API with App Password', () => {
  test.skip(!wpUser || !wpAppPwd,
    'WP_USERNAME and WP_APP_PASSWORD must be set in env');

  test('GET /users/me returns current user when authed with app password', async ({ request }) => {
    const token = Buffer.from(`${wpUser}:${wpAppPwd}`).toString('base64');

    const resp = await request.get('/wp-json/wp/v2/users/me', {
      headers: { 'Authorization': `Basic ${token}` },
      failOnStatusCode: false
    });

    expect(resp.status()).toBe(200);

    const body = await resp.json();
    expect(body).toHaveProperty('id');
    expect(body).toHaveProperty('name');
  });
});
