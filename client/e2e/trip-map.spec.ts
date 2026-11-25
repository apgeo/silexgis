// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, overlayTreeNode } from './helpers.ts';
import { asPerson, tripBody, tryAsPerson } from './arrange.ts';

/**
 * The trip overlay, reached the way it is meant to be reached: from the trip listing's own button.
 *
 * Nothing here asserts a number that depends on what the demo instance holds. The counts it does
 * assert are of two trips this run writes into a day window in 1901, where nothing else lives: one
 * with a position, which has to be drawn, and one without, which has to be reported rather than
 * silently missing. Around them: the list's window travels to the map, the layer is on when the
 * reader arrives, and its filters are on screen only while it is.
 */

/** The overlay's request for a window starting on `from`, whichever control asked it. */
function windowAsked(page: Page, from: string) {
  return page.waitForRequest((request) => {
    const url = new URL(request.url());
    return url.pathname === '/api/v1/map/trip-logs' && url.searchParams.get('from') === from;
  }, { timeout: 30_000 });
}

test('the trip list hands its filter to the map, which turns the overlay on and explains itself', async ({
  page,
}) => {
  await login(page);
  // The list narrowed to a window, so there is a filter to hand over.
  await gotoRoute(page, '/trip-logs?from=1901-01-01&to=1901-12-31');

  // The other end of a button that until now led to a map that did nothing.
  const firstAsk = windowAsked(page, '1901-01-01');
  await page.getByTestId('trip-list-show-on-map').click();
  await page.waitForURL(/\/map/, { timeout: 60_000 });

  // Arriving with the overlay already on is the whole point of the handover: carrying a filter to
  // a map showing no trips would be a button that appears to do nothing.
  const tripsNode = overlayTreeNode(page, 'Trip logs');
  await expect(tripsNode).toBeVisible({ timeout: 30_000 });
  await expect(tripsNode.locator('.ant-tree-checkbox-checked')).toBeVisible();

  // The instruction is consumed rather than left in the address: the camera syncs into the URL as
  // the reader pans, so a re-applied one-shot would keep re-answering a question already answered.
  expect(new URL(page.url()).searchParams.get('trips')).toBeNull();

  // The filters are a block in the layer panel and not a dock of their own, so they are on screen
  // exactly while the layer is — and they show the window the list was narrowed to.
  const filters = page.getByTestId('map-trip-filters');
  await expect(filters).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('map-trips-from')).toHaveValue('1901-01-01');
  await expect(page.getByTestId('map-trips-to')).toHaveValue('1901-12-31');

  // The layer asked with that window, and the box it asked over is where the reader is looking,
  // so this run's located trip is put inside it.
  const [west, south, east, north] = new URL((await firstAsk).url())
    .searchParams.get('bbox')!
    .split(',')
    .map(Number);

  // What the overlay says about itself. The count is a claim only once an answer has arrived, so
  // this waits for it to stop saying it is loading rather than reading it straight away.
  const count = page.getByTestId('map-trip-count');
  await expect(count).toBeVisible();
  await expect(count).not.toHaveText(/Loading/, { timeout: 30_000 });
  await expect(count).toHaveText(/^\d+ trip\(s\) drawn$/);

  const stamp = Date.now();
  const made: string[] = [];
  try {
    const located = await asPerson<{ id: string }>(page, 'POST', '/api/v1/trip-logs', tripBody(
      `E2E Map Located ${stamp}`,
      '1901-06-15',
      { geom: { type: 'Point', coordinates: [(west + east) / 2, (south + north) / 2] } },
    ));
    made.push(located.id);
    const unlocated = await asPerson<{ id: string }>(page, 'POST', '/api/v1/trip-logs', tripBody(
      `E2E Map Unlocated ${stamp}`,
      '1901-06-16',
    ));
    made.push(unlocated.id);

    // Asked again over a window that holds exactly this run's two trips: one is drawn and the
    // other is reported. Anchored, so "11 trip(s) drawn" is not read as one.
    const secondAsk = windowAsked(page, '1901-01-02');
    await page.getByTestId('map-trips-from').fill('1901-01-02');
    await secondAsk;
    await expect(count).toHaveText(/^1 trip\(s\) drawn$/, { timeout: 30_000 });
    await expect(page.getByTestId('map-trips-unlocated')).toContainText(/^1 trip\(s\) in this window/);

    // A window no trip can fall in is a legitimate question with an honest answer, not an error,
    // and nothing in range means nothing unlocated either — the unlocated line is about the window.
    await page.getByTestId('map-trips-from').fill('1900-01-01');
    await page.getByTestId('map-trips-to').fill('1900-12-31');
    await expect(count).toHaveText(/^0 trip\(s\) drawn$/, { timeout: 30_000 });
    await expect(page.getByTestId('map-trips-unlocated')).toBeHidden();
  } finally {
    for (const id of made) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${id}`);
    }
  }

  // Switching the layer off takes its filters with it; nothing is left explaining a layer that
  // is no longer drawn.
  await tripsNode.locator('.ant-tree-checkbox').click();
  await expect(filters).toBeHidden({ timeout: 30_000 });
});
