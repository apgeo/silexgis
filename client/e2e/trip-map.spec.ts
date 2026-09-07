// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, overlayTreeNode } from './helpers.ts';

/**
 * The trip overlay, reached the way it is meant to be reached: from the trip listing's own button.
 *
 * Nothing here asserts a number that depends on what the demo instance holds. What the overlay
 * has to get right is not how many dots it draws — that is a fact about the seed — but that the
 * button leads somewhere that works, that the layer is on when the reader arrives, that its
 * filters are on screen only while it is, and that a trip with no location is reported rather
 * than silently missing. Those hold whatever has been seeded.
 */

test('the trip list hands its filter to the map, which turns the overlay on and explains itself', async ({
  page,
}) => {
  await login(page);
  await gotoRoute(page, '/trip-logs');

  // The other end of a button that until now led to a map that did nothing.
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
  // exactly while the layer is.
  const filters = page.getByTestId('map-trip-filters');
  await expect(filters).toBeVisible({ timeout: 30_000 });

  // What the overlay says about itself. The count is a claim only once an answer has arrived, so
  // this waits for it to stop saying it is loading rather than reading it straight away.
  const count = page.getByTestId('map-trip-count');
  await expect(count).toBeVisible();
  await expect(count).not.toHaveText(/Loading/, { timeout: 30_000 });
  await expect(count).toHaveText(/trip\(s\) drawn/);

  // The day window narrows what is asked for, and doing so must not break the overlay: a window
  // no trip can fall in is a legitimate question with an honest answer, not an error.
  await page.getByTestId('map-trips-from').fill('1900-01-01');
  await page.getByTestId('map-trips-to').fill('1900-12-31');
  await expect(count).toHaveText(/0 trip\(s\) drawn/, { timeout: 30_000 });
  // Nothing in range means nothing unlocated either — the unlocated line is about the window.
  await expect(page.getByTestId('map-trips-unlocated')).toBeHidden();

  // Switching the layer off takes its filters with it; nothing is left explaining a layer that
  // is no longer drawn.
  await tripsNode.locator('.ant-tree-checkbox').click();
  await expect(filters).toBeHidden({ timeout: 30_000 });
});
