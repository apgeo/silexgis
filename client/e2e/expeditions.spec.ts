// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * A camp's own page, driven the way somebody arrives at one: by address.
 *
 * The tab is in the address on purpose — so a section of a camp is a place somebody can link to
 * and one that survives a reload — and the map is the tab that has to be told it became visible,
 * because a map built against a pane nobody is looking at measures nothing and draws a blank tile
 * grid that never repairs itself. Both are checked here rather than argued about in a comment.
 */

/**
 * A camp out of the seeded data, with the working area drawn on it.
 *
 * There is no list page for camps yet, so the id is asked for over the API rather than clicked
 * to — with the token the running application is holding, captured off a request it made itself.
 * The session lives in memory, so there is no cookie or stored token to borrow instead.
 */
async function seededCamp(page: Page): Promise<{ id: string; name: string }> {
  let authorization: string | undefined;
  page.on('request', (request) => {
    const header = request.headers()['authorization'];
    if (header && request.url().includes('/api/v1/')) {
      authorization = header;
    }
  });

  await login(page);
  await expect.poll(() => authorization, { timeout: 30_000 }).toBeTruthy();

  const response = await page.request.get('/api/v1/expeditions?pageSize=50', {
    headers: { authorization: authorization! },
  });
  expect(response.ok()).toBe(true);
  const body = (await response.json()) as { items: { id: string; name: string; geom: unknown }[] };
  // The one with a working area: it is what the map tab has to draw for this to prove anything.
  const camp = body.items.find((x) => x.geom !== null) ?? body.items[0];
  expect(camp, 'the seeded data holds no camp to open').toBeTruthy();
  return camp;
}

test('a camp opens at the tab its address names, and keeps it across a reload', async ({ page }) => {
  const camp = await seededCamp(page);

  await gotoRoute(page, `/expeditions/${camp.id}`);
  await expect(page.getByTestId('expedition-name')).toHaveText(camp.name);
  // No tab in the address is the page's own first tab, and the address stays clean.
  await expect(page.getByTestId('expedition-trips-tab')).toBeVisible({ timeout: 15_000 });

  await page.goto(`/expeditions/${camp.id}?tab=roster`);
  await expect(page.getByTestId('expedition-roster-tab')).toBeVisible({ timeout: 15_000 });

  // A reload is the whole point of the tab living in the address rather than in memory.
  await page.reload();
  await expect(page.getByTestId('expedition-roster-tab')).toBeVisible({ timeout: 15_000 });
  expect(new URL(page.url()).searchParams.get('tab')).toBe('roster');
});

test("a camp's map draws once its tab is the one on screen", async ({ page }) => {
  const camp = await seededCamp(page);

  await page.goto(`/expeditions/${camp.id}?tab=map`);
  await expect(page.getByTestId('expedition-map-tab')).toBeVisible({ timeout: 15_000 });

  // A map that measured nothing renders no canvas of its own and no attribution; a drawn one
  // has both. This is the assertion that would have caught a map built in a hidden pane.
  const canvas = page.getByTestId('expedition-map').locator('canvas');
  await expect(canvas).toBeVisible({ timeout: 30_000 });
  const box = await canvas.boundingBox();
  expect(box!.width).toBeGreaterThan(100);
  expect(box!.height).toBeGreaterThan(100);

  // Said on the screen, not only in the response: the same camp draws differently for two
  // readers, and a map without that sentence gets reported as missing data.
  await expect(page.getByText(/Drawn over the trips you may read/)).toBeVisible();
});
