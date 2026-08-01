// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';

// Demo credentials/data: `dotnet run -- seed-demo` with the dev admin bootstrap.
// The full OIDC code+PKCE flow runs in the real browser.
export const adminEmail = 'admin@dev.local';
export const adminPassword = 'dev-admin-pass-1';

export async function login(page: Page) {
  await page.goto('/');
  // Unauthenticated → OIDC authorize → SPA login page with returnUrl.
  await page.waitForURL(/\/login\?returnUrl=/);
  await page.getByLabel('Email').fill(adminEmail);
  await page.getByLabel('Password').fill(adminPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();
  // Authorize completes, callback exchanges the code, workspace renders.
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 20_000 });
}

/** A named overlay row in the layer composer tree (left dock). */
export function overlayTreeNode(page: Page, name: string) {
  return page.locator('.layer-composer .ant-tree-treenode').filter({ hasText: name });
}

/** Removes a surface feature through the registry table — cleanup for flows that save one. */
export async function deleteFeature(page: Page, featureName: string) {
  await page.goto('/features');
  const row = page.getByRole('row', { name: new RegExp(featureName) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
}

/**
 * Puts the workspace map over the demo cave. Search cannot do this: its results carry no
 * coordinates by design, so picking one opens the record instead of moving the map. The
 * feature list's "show on map" fits the map to the row's geometry and goes there.
 */
export async function centreOnDemoCave(page: Page) {
  await page.goto('/features');
  const row = page.getByRole('row', { name: /Peștera Demo Mare/ });
  await expect(row).toBeVisible({ timeout: 15_000 });
  // Icon-only antd button: its accessible name is the icon's aria-label.
  await row.getByRole('button', { name: 'aim' }).click();
  await expect(page.locator('.map-canvas')).toBeVisible({ timeout: 15_000 });
}

/** Taps the map at a viewport-relative point, the way a finger places a vertex. */
export async function tapMap(page: Page, x: number, y: number) {
  const box = (await page.locator('.map-canvas').boundingBox())!;
  await page.touchscreen.tap(box.x + x, box.y + y);
}

/**
 * Presses and holds the map, the gesture that opens the context menu on a phone.
 *
 * Dispatched rather than performed with a real touch: a genuine hold makes Android
 * Chromium raise its own `contextmenu`, which would prove nothing about the timer the app
 * has to run for iOS Safari — where no such event ever arrives. This drives that timer.
 * The threshold and cancel rules themselves are unit-tested with a fake clock.
 */
export async function longPressMap(page: Page, x: number, y: number) {
  // The listener sits on OL's viewport; the canvas element below it would not bubble up.
  const viewport = page.locator('.ol-viewport');
  const box = (await viewport.boundingBox())!;
  const at = { pointerType: 'touch', isPrimary: true, clientX: box.x + x, clientY: box.y + y };
  await viewport.dispatchEvent('pointerdown', at);
  await page.waitForTimeout(700); // comfortably past the 550ms the app waits
  await viewport.dispatchEvent('pointerup', at);
}
