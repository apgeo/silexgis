// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, mapAddress, pixelOnMap } from './helpers.ts';

/**
 * Draw a line on the map, get the ground under it.
 *
 * What only a browser shows: that the button arms a draw on the real map, that two clicks and a
 * double-click make a line, and that the line is sent to the profile route and its answer opens
 * in the panel. Whether the profile has readings depends on whether the instance has built an
 * elevation model, so the spec asserts the request, that the route answered it successfully, and
 * that the panel shows the profile or says why there is none — never the failure notice, which is
 * what a route that broke would put there.
 */
const camera = { lat: 45.525, lon: 25.44, zoom: 13 };

test('a line drawn on the map is sent for its terrain profile and the answer opens beside the button', async ({
  page,
}) => {
  await login(page);
  await gotoRoute(page, mapAddress(camera));

  await page.getByTestId('terrain-profile-button').click();
  await expect(page.getByTestId('terrain-profile-tool')).toContainText(/double-click to finish/i);

  const a = await pixelOnMap(page, camera, camera.lat, camera.lon - 0.004);
  const b = await pixelOnMap(page, camera, camera.lat + 0.002, camera.lon);
  const c = await pixelOnMap(page, camera, camera.lat, camera.lon + 0.004);

  const asked = page.waitForRequest(
    (request) => request.url().includes('/api/v1/terrain/profile') && request.method() === 'POST',
  );
  const answered = page.waitForResponse(
    (response) =>
      response.url().includes('/api/v1/terrain/profile') && response.request().method() === 'POST',
  );
  await page.mouse.click(a.x, a.y);
  await page.mouse.click(b.x, b.y);
  await page.mouse.dblclick(c.x, c.y);
  const body = (await asked).postDataJSON() as { points: unknown[] };
  expect(body.points.length).toBeGreaterThanOrEqual(2);

  expect((await answered).status()).toBe(200);

  // Either the profile is drawn or the panel says honestly why there is none (no model built, or
  // none reaching this line); it never stays empty, and it never shows the failure notice.
  const tool = page.getByTestId('terrain-profile-tool');
  await expect(tool.locator('[data-testid="chart-terrain-profile"], .ant-empty')).toBeVisible({
    timeout: 30_000,
  });
  await expect(tool.locator('.ant-alert')).toHaveCount(0);

  await page.getByTestId('terrain-profile-close').click();
  await expect(page.getByTestId('terrain-profile-close')).toHaveCount(0);
});
