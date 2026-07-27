// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test } from '@playwright/test';
import { login, longPressMap } from './helpers.ts';

// Runs in the `mobile-ios-smoke` project only (iPhone 14, WebKit, 390x664).
//
// A smoke project on purpose: the Android one is the editing gate, and duplicating it here
// would buy coverage of the same code twice. What this exists for is the one thing chromium
// cannot answer — WebKit raises no `contextmenu` over a canvas, so the long-press menu is
// either the app's own pointer timer or it does not exist on an iPhone at all. It also
// catches the WebKit-only ways the workspace can simply fail to render.
//
// Emulated WebKit is not an iPhone: it does not reproduce Safari's touch callout, its
// scroll/zoom heuristics, or its synthetic-click timing. This proves the code path is
// reachable in the engine, not that the gesture feels right on the device.

test('the workspace renders, its docks open as drawers, and long-press opens the map menu', async ({ page }) => {
  await login(page); // asserts the OL viewport renders — WebKit's own first hurdle

  // The phone layout is chosen on width, so it applies here exactly as on Android.
  await expect(page.locator('.map-workspace-panel')).toHaveCount(0);
  await page.getByTestId('map-dock-toggle-left').click();
  await expect(page.getByText('Base layers')).toBeVisible();

  // Close it before touching the map: the drawer's mask covers the canvas. The 320px panel
  // leaves a strip of mask exposed at this width — aim there, not at the centre.
  await page.locator('.ant-drawer-mask').click({ position: { x: 360, y: 400 } });
  await expect(page.getByText('Base layers')).toBeHidden();

  await page.getByTestId('map-dock-toggle-right').click();
  await expect(page.locator('.ant-drawer-right')).toBeVisible();
  await page.locator('.ant-drawer-mask').click({ position: { x: 30, y: 400 } });
  await expect(page.locator('.ant-drawer-right')).toBeHidden();

  // The point of this project: Safari sends no contextmenu, so this menu can only come from
  // the app timing the press itself.
  await longPressMap(page, 150, 300);
  await expect(page.getByText('Copy coordinates')).toBeVisible();
  await expect(page.getByText('Add feature')).toBeVisible();
});

test('the app is installable to the home screen', async ({ page }) => {
  await login(page);

  // iOS ignores the manifest's icons and takes this one instead, so its absence is silent:
  // the home-screen icon becomes a screenshot of the page.
  await expect(page.locator('link[rel="apple-touch-icon"]')).toHaveAttribute('href', /\.png$/);
  const icon = await page.request.get('/icons/apple-touch-icon.png');
  expect(icon.status()).toBe(200);
  // Status alone is not enough: an SPA-fallback server answers 200 with index.html for any
  // missing path, so a deleted icon would still pass a status-only check.
  expect(icon.headers()['content-type']).toContain('image/png');
});
