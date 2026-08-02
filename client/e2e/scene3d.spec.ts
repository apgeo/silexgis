// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test } from '@playwright/test';
import { login } from './helpers.ts';

// The engine's own hosted services are the one thing a unit test cannot rule out: it runs against
// a stand-in for the library, so it can only show that this application configures the real one
// correctly. Here the real library runs, draws a real globe and fetches its real runtime files, so
// an outbound request to the vendor would actually happen if the configuration were wrong.
const VENDOR_HOST = /(^|\/\/|\.)cesium\.com/;

test('the 3D view opens on our own basemap and talks to nobody else', async ({ page }) => {
  const requested: string[] = [];
  page.on('request', (request) => requested.push(request.url()));

  await login(page);
  await page.getByRole('menuitem', { name: '3D view' }).click();
  await page.waitForURL(/\/map3d$/);

  // The engine owns the canvas inside our element; its presence is the scene starting.
  const canvas = page.getByTestId('scene3d-container').locator('canvas').first();
  await expect(canvas).toBeAttached({ timeout: 30_000 });
  await expect(page.getByTestId('scene3d-loading')).toHaveCount(0, { timeout: 30_000 });
  await expect(page.getByText('The 3D view could not be started')).toHaveCount(0);

  // Tile licences require the attribution to be shown, and it is the basemap's, not a vendor's.
  await expect(page.locator('.cesium-widget-credits')).toContainText('OpenStreetMap', {
    timeout: 30_000,
  });

  // The runtime files come from this installation, and the map tiles from the configured catalog.
  await expect
    .poll(() => requested.some((url) => /\/cesiumStatic\//.test(url)), { timeout: 30_000 })
    .toBe(true);

  expect(requested.filter((url) => VENDOR_HOST.test(url))).toEqual([]);
});

test('a browser without WebGL 2 is told why, rather than shown a dead canvas', async ({
  browser,
}) => {
  const context = await browser.newContext();
  // Removes the capability the scene needs, before any application code runs.
  await context.addInitScript(() => {
    Reflect.deleteProperty(window, 'WebGL2RenderingContext');
  });
  const page = await context.newPage();

  await login(page);
  await page.goto('/map3d');

  await expect(page.getByTestId('scene3d-unsupported')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('This browser cannot show the 3D view')).toBeVisible();
  await expect(page.getByTestId('scene3d-container')).toHaveCount(0);

  await context.close();
});
