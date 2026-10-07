// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, localDay, tripBody, tryAsPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * Deleting a trip and taking it back, as the person who deleted it sees both.
 *
 * A delete marks the trip and removes nothing, so the flow is the round trip: a trip with people
 * on it is deleted from its own page, is gone from the list and from its own address, is found
 * on the list of deleted trips, and is restored from there with the same people on it. The
 * roster is what the flow carries across, because "it came back" proves little about a trip
 * that held nothing; a trip that returns with its party is one whose rows were never taken down.
 *
 * The trip is arranged through the API — the trip form has a spec of its own — and everything
 * from the delete onwards is driven, since the confirmation, the list and the restore are the
 * subject here.
 */

/** Narrows the trip list to one word of a title and waits for the list to have answered. */
async function searchTrips(page: Page, word: string) {
  await page.getByPlaceholder('Search by title…').fill(word);
  // The narrowing lives in the address, and it arrives there only once the typing has settled:
  // until then the rows on screen are still the unfiltered page.
  await expect(page).toHaveURL(new RegExp(`[?&]q=${word}`), { timeout: 15_000 });
}

test('a deleted trip leaves the list and its own address, and is restored with its roster', async ({
  page,
  consoleErrors,
}) => {
  test.slow();
  // The page asks for the deleted trip by its address, is answered 404, and says so — and the
  // browser writes a console error for every request that answered 404, however well the page
  // handled it.
  consoleErrors.allow(
    /status of 404/,
    'this flow opens the address of a trip it has just deleted, which the server answers as not found',
  );

  const stamp = `${Date.now()}`;
  const title = `E2E Restore ${stamp}`;
  const first = `E2E Ana ${stamp}`;
  const second = `E2E Bogdan ${stamp}`;

  await login(page);
  const trip = await asPerson<{ id: string }>(
    page,
    'POST',
    '/api/v1/trip-logs',
    tripBody(title, localDay(-3), {
      participants: [
        { caverId: null, newCaverName: first },
        { caverId: null, newCaverName: second },
      ],
    }),
  );

  try {
    // On the list, by the door a deleted trip will later be reached through.
    await gotoRoute(page, '/trip-logs');
    await expect(page.getByTestId('trip-list-deleted')).toBeVisible({ timeout: 30_000 });
    await searchTrips(page, stamp);
    const listed = page.getByRole('row', { name: new RegExp(title) });
    await expect(listed).toBeVisible({ timeout: 15_000 });

    // Its own page, with the party on it.
    await listed.click();
    await expect(page).toHaveURL(new RegExp(`/trip-logs/${trip.id}`));
    await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(first).first()).toBeVisible();
    await expect(page.getByText(second).first()).toBeVisible();

    // The confirmation says what is about to happen, in the installation's own number: nothing
    // is removed, and the trip can be put back for a stated while.
    await page.getByTestId('trip-delete').click();
    const confirmation = page.getByRole('tooltip');
    await expect(confirmation).toContainText('Delete this trip log?');
    await expect(confirmation).toContainText(/It can be restored from Deleted trips for \d+ days\./);
    await confirmation.getByRole('button', { name: 'OK' }).click();

    // Gone from the list…
    await expect(page).toHaveURL(/\/trip-logs(\?.*)?$/, { timeout: 30_000 });
    await searchTrips(page, stamp);
    await expect(page.getByTestId('trip-list-empty')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('row', { name: new RegExp(title) })).toHaveCount(0);

    // …and from its own address, which says what it says of a trip that never existed, and
    // points whoever could have deleted one at where a deleted trip would be.
    await gotoRoute(page, `/trip-logs/${trip.id}`);
    await expect(page.getByText('No such trip')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(title)).toHaveCount(0);
    await page.getByTestId('trip-not-found-deleted').click();
    await expect(page).toHaveURL(/\/trip-logs\/deleted$/);

    // The deleted list holds it, with how long it has left.
    const deleted = page.getByTestId('deleted-trips').getByRole('row', { name: new RegExp(title) });
    await expect(deleted).toBeVisible({ timeout: 30_000 });
    await expect(deleted).toContainText(/in \d+ days/);

    // Restored, after a confirmation, and opened.
    await deleted.getByTestId('deleted-trip-restore').click();
    const restoring = page.getByRole('tooltip');
    await expect(restoring).toContainText('Restore this trip?');
    await restoring.getByRole('button', { name: 'Restore' }).click();

    await expect(page).toHaveURL(new RegExp(`/trip-logs/${trip.id}$`), { timeout: 30_000 });
    await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 30_000 });
    // The same people, because nothing of the trip was taken down while it was away.
    await expect(page.getByText(first).first()).toBeVisible();
    await expect(page.getByText(second).first()).toBeVisible();

    // And back on the list, where it is no longer among the deleted ones.
    await gotoRoute(page, '/trip-logs/deleted');
    await expect(page.getByTestId('deleted-trips')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('row', { name: new RegExp(title) })).toHaveCount(0);
  } finally {
    await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
  }
});
