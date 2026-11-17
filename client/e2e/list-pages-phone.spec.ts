// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The list pages at the width of a phone.
 *
 * Each list page has a title on the left and a row of buttons on the right — the trip list up to
 * seven of them. A row that may not wrap is as wide as all its buttons together, and at a phone's
 * width that pushes the page past the right edge: the reader scrolls the whole page sideways to
 * reach the buttons, and the table under them scrolls with it. Each page is reached at a desktop
 * width (the address round trip waits for the desktop frame), then narrowed to a phone's width and
 * measured. The administrator is signed in so that every button a page can offer is drawn.
 */
const pages = [
  { path: '/trip-logs', heading: 'Trip logs' },
  { path: '/caves', heading: 'Caves' },
  { path: '/features', heading: 'Features' },
  { path: '/saved-filters', heading: 'Saved filters' },
];

test('the list pages fit a phone screen without a sideways scroll', async ({ page }) => {
  test.setTimeout(240_000);
  await login(page);
  for (const { path, heading } of pages) {
    await page.setViewportSize({ width: 1280, height: 800 });
    await gotoRoute(page, path);
    await page.setViewportSize({ width: 412, height: 915 });
    await expect(page.getByRole('heading', { name: heading, exact: true }), path).toBeVisible({ timeout: 30_000 });
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow, `${path} fits a phone screen`).toBeLessThanOrEqual(0);
    // The page's own content pane scrolls by itself, so the window can stay narrow while the row
    // runs off inside it. Every control in the title row has to end inside the screen.
    const header = page.getByRole('heading', { name: heading, exact: true }).locator('xpath=..');
    const furthest = await header.evaluate((row) =>
      Math.max(...Array.from(row.querySelectorAll('button, a')).map((control) => control.getBoundingClientRect().right)),
    );
    expect(furthest, `${path} keeps its title row's controls on screen`).toBeLessThanOrEqual(412);
  }
});
