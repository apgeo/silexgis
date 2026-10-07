// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, tryAsPerson } from './arrange.ts';
import { gotoRoute, login, mapAddress } from './helpers.ts';

/**
 * Deleting a cave and taking it back, as the person who deleted it sees both.
 *
 * A delete marks the cave and what it contains and removes nothing, so the flow is the round
 * trip: a cave with an entrance is deleted from its own page, is gone from the list and from the
 * map, is found on the list of deleted caves and features as one row that says an entrance went
 * with it, and is restored from there with that entrance. The entrance is what the flow carries
 * across, because "it came back" proves little about a cave that held nothing.
 *
 * The caves are arranged through the API — the cave form has a spec of its own — and everything
 * from the delete onwards is driven, since the confirmation, the list and the restore are the
 * subject here.
 */

/**
 * Narrows the cave list to this run's caves and waits for the list to have answered about them.
 * By the run's stamp, which both caves carry and which is one unbroken word: what is waited for
 * is the request that names it, and a word has one spelling in an address where a name with
 * spaces in it has several.
 */
async function searchCaves(page: Page, stamp: string) {
  const answered = page.waitForResponse(
    (r) => r.url().includes('/api/v1/caves?') && r.url().includes(stamp) && r.ok(),
    { timeout: 30_000 },
  );
  await page.getByPlaceholder('Search name or toponym…').fill(stamp);
  await answered;
}

/**
 * The caves whose entrances the map was handed to draw, read off the answers to the map's own
 * requests from here on.
 *
 * The map draws on a canvas and keeps its objects to itself, so what it shows cannot be asked of
 * the page; what it was given to show can. A set that only grows, so a flow asks "has the map been
 * given this cave yet" and, of a cave that must be absent, asks it of an answer that did carry a
 * neighbour standing a few metres away.
 */
function entrancesServedToTheMap(page: Page) {
  const answers: string[] = [];
  page.on('response', (response) => {
    if (!response.url().includes('/api/v1/map/cave-entrances') || !response.ok()) {
      return;
    }
    void response
      .text()
      .then((body) => answers.push(body))
      .catch(() => {
        // An answer that cannot be read says nothing about what the map was given.
      });
  });
  return {
    forget: () => {
      answers.length = 0;
    },
    /** The answers that carried this cave. */
    naming: (caveId: string) => answers.filter((body) => body.includes(caveId)),
  };
}

test('a deleted cave leaves the list and the map with its entrance, and is restored with it', async ({
  page,
}) => {
  test.slow();

  const stamp = `${Date.now()}`;
  const name = `E2E Restore Cave ${stamp}`;
  const neighbourName = `E2E Neighbour Cave ${stamp}`;
  const entranceName = `E2E Gura ${stamp}`;
  // A spot of its own for this run, away from the demonstration caves, so that at this zoom the
  // two entrances are served one by one rather than counted into a cell with others.
  const lat = 46.9 + (Number(stamp.slice(-4)) % 1000) / 10_000;
  const lon = 23.1 + (Number(stamp.slice(-7, -4)) % 1000) / 10_000;
  const camera = { lat, lon, zoom: 17 };

  await login(page);
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const entranceTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/entrance-types');
  const newCave = (caveName: string) =>
    asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
      name: caveName,
      caveTypeId: caveTypes[0].id,
      visibility: 'authenticated',
      locationProtected: false,
      explorationStatus: 'unknown',
      isShowCave: false,
    });
  const newEntrance = (caveId: string, entrance: string, eastwards: number) =>
    asPerson(page, 'POST', `/api/v1/caves/${caveId}/entrances`, {
      name: entrance,
      entranceTypeId: entranceTypes[0].id,
      isMain: true,
      geom: { type: 'Point', coordinates: [lon + eastwards, lat] },
      altitude: null,
      description: null,
      positionQuality: 'gps',
      surveyedAt: null,
    });

  const cave = await newCave(name);
  // A second cave a few metres off that is never deleted: what the map's answers are checked
  // against when the first one has to be absent from them.
  const neighbour = await newCave(neighbourName);

  try {
    await newEntrance(cave.id, entranceName, 0);
    await newEntrance(neighbour.id, `E2E Vecina ${stamp}`, 0.0004);
    const map = entrancesServedToTheMap(page);

    // On the map, both of them.
    await gotoRoute(page, mapAddress(camera));
    await expect.poll(() => map.naming(cave.id).length, { timeout: 60_000 }).toBeGreaterThan(0);
    await expect.poll(() => map.naming(neighbour.id).length, { timeout: 60_000 }).toBeGreaterThan(0);

    // On the list, by the door a deleted cave will later be reached through.
    await gotoRoute(page, '/caves');
    await expect(page.getByTestId('cave-list-deleted')).toBeVisible({ timeout: 30_000 });
    await searchCaves(page, stamp);
    const listed = page.getByRole('row', { name: new RegExp(name) });
    await expect(listed).toBeVisible({ timeout: 15_000 });

    // Its own page, with the entrance on it.
    await listed.click();
    await expect(page).toHaveURL(new RegExp(`/caves/${cave.id}`));
    await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(entranceName).first()).toBeVisible({ timeout: 30_000 });

    // The confirmation says what is about to happen: the entrances go too, nothing is removed,
    // and where it can be put back from.
    await page.getByTestId('cave-delete').click();
    const confirmation = page.getByRole('tooltip');
    await expect(confirmation).toContainText('Delete this cave?');
    await expect(confirmation).toContainText('it can be restored, with them, from Deleted caves and features');
    await confirmation.getByRole('button', { name: 'OK' }).click();

    // Gone from the list…
    await expect(page).toHaveURL(/\/caves(\?.*)?$/, { timeout: 30_000 });
    await searchCaves(page, stamp);
    await expect(page.getByRole('row', { name: new RegExp(name) })).toHaveCount(0);

    // …and from the map: an answer that carries the neighbour, standing beside where the
    // entrance was, does not carry the cave.
    map.forget();
    await gotoRoute(page, mapAddress(camera));
    await expect.poll(() => map.naming(neighbour.id).length, { timeout: 60_000 }).toBeGreaterThan(0);
    expect(map.naming(cave.id)).toHaveLength(0);

    // The deleted list holds it as one row — the cave, with its entrance counted on it and not
    // listed beside it.
    await gotoRoute(page, '/caves');
    await page.getByTestId('cave-list-deleted').click();
    await expect(page).toHaveURL(/\/features\/deleted$/);
    const deleted = page.getByTestId('deleted-features').getByRole('row', { name: new RegExp(name) });
    await expect(deleted).toBeVisible({ timeout: 30_000 });
    await expect(deleted.getByTestId('deleted-feature-took')).toHaveText('1 entrance');
    await expect(
      page.getByTestId('deleted-features').getByRole('row', { name: new RegExp(entranceName) }),
    ).toHaveCount(0);

    // Restored, after a confirmation, and opened.
    await deleted.getByTestId('deleted-feature-restore').click();
    const restoring = page.getByRole('tooltip');
    await expect(restoring).toContainText('Restore this?');
    await restoring.getByRole('button', { name: 'Restore' }).click();

    await expect(page).toHaveURL(new RegExp(`/caves/${cave.id}$`), { timeout: 30_000 });
    await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 30_000 });
    // The same entrance, because nothing of the cave was taken down while it was away.
    await expect(page.getByText(entranceName).first()).toBeVisible({ timeout: 30_000 });

    // Back on the map…
    map.forget();
    await gotoRoute(page, mapAddress(camera));
    await expect.poll(() => map.naming(cave.id).length, { timeout: 60_000 }).toBeGreaterThan(0);

    // …on the list…
    await gotoRoute(page, '/caves');
    await searchCaves(page, stamp);
    await expect(page.getByRole('row', { name: new RegExp(name) })).toBeVisible({ timeout: 15_000 });

    // …and no longer among the deleted ones.
    await gotoRoute(page, '/features/deleted');
    await expect(page.getByTestId('deleted-features')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('row', { name: new RegExp(name) })).toHaveCount(0);
  } finally {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${cave.id}`);
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${neighbour.id}`);
  }
});
