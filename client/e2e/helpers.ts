// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';

// Demo credentials/data: `dotnet run -- seed-demo` with the dev admin bootstrap.
// The full OIDC code+PKCE flow runs in the real browser.
export const adminEmail = 'admin@dev.local';
export const adminPassword = 'dev-admin-pass-1';

/**
 * Signs in, as the demo administrator unless another account is named. A flow that has to
 * show one person's content to a different person needs a second account, and the sign-in
 * itself is identical for both — only the credentials differ.
 */
export async function login(page: Page, email = adminEmail, password = adminPassword) {
  await page.goto('/');
  // Unauthenticated → OIDC authorize → SPA login page with returnUrl.
  await page.waitForURL(/\/login\?returnUrl=/);
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  // Authorize completes, callback exchanges the code, workspace renders. The wait is generous
  // because signing in is a redirect chain through the server and then a first paint of the
  // map, and every worker in the run starts with one: on a machine running the whole suite in
  // parallel this is the slowest moment of any test, and a tighter bound fails tests that have
  // nothing wrong with them.
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
}

/**
 * Goes to a route by address and waits until the application is really on it.
 *
 * Reaching a route this way is a full sign-in round trip: the session is held in memory, so a
 * hard navigation drops it and the application fetches a new one through the authorization
 * server before it renders anything. Looking for a row before that round trip has finished
 * looks for it on the redirect pages, where it will never be — and because a click waits for
 * its target, the test does not fail at the mistake. It hangs until its whole time is gone and
 * then blames the row.
 */
export async function gotoRoute(page: Page, path: string) {
  await page.goto(path);
  await page.waitForURL((url) => url.pathname === path, { timeout: 60_000 });
}

/** A named overlay row in the layer composer tree (left dock). */
export function overlayTreeNode(page: Page, name: string) {
  return page.locator('.layer-composer .ant-tree-treenode').filter({ hasText: name });
}

/**
 * Removes a surface feature through the registry table — cleanup for flows that save one.
 *
 * The registry shows a page at a time, so the row is narrowed to by name first: whether it
 * happens to be on the first page is a fact about how many other features sort ahead of it,
 * not about the one being removed.
 */
export async function deleteFeature(page: Page, featureName: string) {
  await page.goto('/features');
  await page.getByPlaceholder('Search by name').fill(featureName);
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
  await page.getByPlaceholder('Search by name').fill('Peștera Demo Mare');
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
