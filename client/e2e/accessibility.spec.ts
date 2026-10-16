// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, mapAddress } from './helpers.ts';

/** The camera the map's address says it is at: `#zoom/lat/lon`. */
async function cameraLongitude(page: Page): Promise<number> {
  const hash = await page.evaluate(() => window.location.hash);
  const parts = hash.slice(1).split('/');
  return Number(parts[2]);
}

async function focusIsOn(page: Page, selector: string): Promise<boolean> {
  return page.evaluate((sel) => document.activeElement?.matches(sel) ?? false, selector);
}

/**
 * The workspace used from the keyboard alone, as somebody who cannot use a pointer uses it: past
 * the shell by the skip link, onto the map by Tab, the map moved with the arrows, and the list of
 * shortcuts opened with `?` and closed with Escape — handing focus back to where it was.
 */
test('the map workspace can be reached and driven from the keyboard', async ({ page }) => {
  await login(page);
  const camera = { lat: 45.7, lon: 25.3, zoom: 10 };
  await gotoRoute(page, mapAddress(camera));
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });

  // The shell's landmarks, in reading order: the skip link first, then the header, the rail's
  // navigation and the page.
  await expect(page.locator('body')).toMatchAriaSnapshot(`
    - link "Skip to main content"
    - banner
    - complementary:
      - navigation "Main navigation"
    - main
  `);

  // The first Tab lands on the skip link, which is then on screen; Enter puts focus in the page.
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: 'Skip to main content' });
  await expect(skip).toBeFocused();
  expect((await skip.boundingBox())!.y).toBeGreaterThanOrEqual(0);
  await page.keyboard.press('Enter');
  expect(await focusIsOn(page, 'main')).toBe(true);

  // From the page, Tab reaches the map — bounded, so a map that is not in the tab order fails
  // here rather than after the test's whole time.
  let reached = false;
  for (let step = 0; step < 200 && !reached; step++) {
    await page.keyboard.press('Tab');
    reached = await focusIsOn(page, '.map-canvas');
  }
  expect(reached, 'the map is in the tab order').toBe(true);
  const map = page.getByRole('application', { name: /^Map\./ });
  await expect(map).toBeFocused();
  // A keyboard focus shows: the ring is drawn for :focus-visible, which a Tab sets.
  expect(await focusIsOn(page, '.map-canvas:focus-visible')).toBe(true);

  // The arrows move the map. Read from the address, which the map writes once it comes to rest.
  const before = await cameraLongitude(page);
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowRight');
  await expect.poll(() => cameraLongitude(page), { timeout: 15_000 }).toBeGreaterThan(before);

  // `?` opens the list of shortcuts, read from what the workspace has bound.
  await page.keyboard.press('?');
  const help = page.getByRole('dialog', { name: 'Keyboard shortcuts' });
  await expect(help).toBeVisible();
  await expect(help).toMatchAriaSnapshot(`
    - dialog "Keyboard shortcuts":
      - table "On this screen":
        - rowgroup:
          - row /Show this list of shortcuts/
          - row /Clear the selection/
          - row /Show or hide the left panel/
          - row /Show or hide the right panel/
          - row /Previous selection/
          - row /Next selection/
      - table "With the map focused":
        - rowgroup:
          - row /Move the map/
          - row /Zoom in/
          - row /Zoom out/
  `);

  // Escape closes it — and only it: the map's own Escape (clear the selection) stays out of it —
  // and focus goes back to the map it was opened from.
  await page.keyboard.press('Escape');
  await expect(help).toBeHidden();
  await expect(map).toBeFocused();
});

test('the shortcut list also opens from a visible control on the map', async ({ page }) => {
  await login(page);
  const opener = page.getByRole('button', { name: 'Keyboard shortcuts' });
  await opener.focus();
  await page.keyboard.press('Enter');
  const help = page.getByRole('dialog', { name: 'Keyboard shortcuts' });
  await expect(help).toBeVisible();
  await help.getByRole('button', { name: 'Close', exact: true }).last().click();
  await expect(help).toBeHidden();
  await expect(opener).toBeFocused();
});
