// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, localDay, tripBody, tryAsPerson, versionOf } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A camp's head count: who is underground on its trips, on one screen, and nowhere on it a place.
 *
 * A camp runs several parties on one day, and the person at the surface asks one thing of all of
 * them at once. Each trip's own tracking answers it for one party; the camp's section answers it
 * for the camp. Which trips it lists, how it counts them and that its answer carries no place are
 * decided on the server and tested there. What only a browser can show is the screen: that a
 * tracked trip is drawn with its count and its people, that a trip nobody ever tracked is not
 * drawn as an empty party, that a watch closed while the section is open is still there and says
 * so, and that the page holds no station and no depth.
 *
 * <b>Why the reader here is the administrator.</b> It is the hard case for "no place". The
 * administrator may be told everything about this cave, and the same reports open on the trip's own
 * tracking show the station and the depth — that is asserted first, so the absence below is an
 * absence of something that was there to show. A screen that kept places only from people who may
 * not have them would pass with a restricted reader and fail here.
 *
 * Everything is stood up by this run and taken down after it. The survey is the committed fixture;
 * the people are invented.
 */

/** The station the depths of this watch are measured from. */
const DATUM_STATION = 'p8.p8.98';
/** The station one of the party is reported at. */
const REPORTED_STATION = 'p8.p8.97';
/** What every station of the fixture survey begins with: no name of that shape may be drawn. */
const ANY_STATION = 'p8.p8.';
/**
 * The depth another is reported at. One decimal, which is what the log keeps, and a figure the text
 * of a page cannot hold by accident: a moment is written with a colon before its minutes and the
 * run's stamps hold no point.
 */
const REPORTED_DEPTH = 137.5;

/**
 * Every answer to the camp's head count this browser receives from here on, as it was sent.
 *
 * The screen is drawn from this one read, so what it carried is the other half of "the page holds
 * no place": a station that travelled and was merely not drawn would still have reached the reader.
 */
function headCountsReceived(page: Page, campId: string) {
  const reading: Promise<void>[] = [];
  const bodies: string[] = [];
  page.on('response', (response) => {
    const { pathname } = new URL(response.url());
    if (pathname !== `/api/v1/expeditions/${campId}/surface-log` || response.status() !== 200) {
      return;
    }
    reading.push(
      response.text().then(
        (body) => {
          bodies.push(body);
        },
        () => {
          // A body the browser dropped on its way elsewhere; the reads that arrived are searched.
        },
      ),
    );
  });
  return async () => {
    await Promise.all(reading);
    return bodies;
  };
}

test('a camp counts who is underground on a tracked trip, keeps it listed once closed, and shows no place', async ({
  page,
}) => {
  // A survey the server has to read before a watch can be started on it, and a wait for the
  // section to ask again by itself.
  test.slow();
  const stamp = Date.now();
  const ana = `E2E Camp Watch Ana ${stamp}`;
  const bujor = `E2E Camp Watch Bujor ${stamp}`;
  const corina = `E2E Camp Watch Corina ${stamp}`;
  const dan = `E2E Camp Watch Dan ${stamp}`;
  const emil = `E2E Camp Watch Emil ${stamp}`;
  const trackedTitle = `E2E Camp Watch Tracked ${stamp}`;
  const untrackedTitle = `E2E Camp Watch Untracked ${stamp}`;

  await login(page);
  let caveId: string | undefined;
  let modelId: string | undefined;
  let campId: string | undefined;
  const tripIds: string[] = [];

  try {
    // ---- The scene: a cave with a survey, a camp, two of its trips ----
    const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
    const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
      name: `E2E Camp Watch Cave ${stamp}`,
      caveTypeId: caveTypes[0].id,
      visibility: 'authenticated',
      locationProtected: false,
      explorationStatus: 'unknown',
      isShowCave: false,
    });
    caveId = cave.id;

    const upload = await page.request.post(`/api/v1/caves/${cave.id}/survey-models`, {
      headers: { Authorization: `Bearer ${await bearerToken(page)}` },
      multipart: {
        file: {
          name: 'P8_Master.3d',
          mimeType: 'application/octet-stream',
          buffer: readFileSync('e2e/fixtures/P8_Master.3d'),
        },
      },
    });
    expect(upload.ok(), `the survey upload answered ${upload.status()}`).toBeTruthy();
    const model = ((await upload.json()) as { id: string }).id;
    modelId = model;
    // The stations a report names are stored when the server has read the file through.
    await expect
      .poll(
        async () =>
          (await asPerson<{ status: string }>(page, 'GET', `/api/v1/survey-models/${model}`)).status,
        { timeout: 90_000, message: 'the uploaded survey was never read' },
      )
      .toBe('ready');

    const camp = await asPerson<{ id: string }>(page, 'POST', '/api/v1/expeditions', {
      name: `E2E Camp Watch ${stamp}`,
      startDate: localDay(-1),
      endDate: localDay(5),
      visibility: 'authenticated',
    });
    campId = camp.id;

    const person = (name: string) => ({
      caverId: null, newCaverName: name, roleId: null, entryTime: null, exitTime: null, note: null,
    });
    type MadeTrip = { id: string; participants: { caverId: string; name: string }[] };
    const tracked = await asPerson<MadeTrip>(
      page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(trackedTitle, localDay(0), {
        caveIds: [cave.id],
        participants: [person(ana), person(bujor), person(corina), person(dan)],
      }),
    );
    tripIds.push(tracked.id);
    const untracked = await asPerson<MadeTrip>(
      page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(untrackedTitle, localDay(0), { caveIds: [cave.id], participants: [person(emil)] }),
    );
    tripIds.push(untracked.id);
    for (const trip of [tracked, untracked]) {
      await asPerson(page, 'POST', `/api/v1/expeditions/${camp.id}/trips`, { tripLogId: trip.id });
    }

    // One trip is tracked and reported on; the other's tracking is never started.
    const watchPath = `/api/v1/trip-logs/${tracked.id}/tracking`;
    const idOf = (name: string) => tracked.participants.find((p) => p.name === name)!.caverId;
    await asPerson(
      page,
      'PUT',
      watchPath,
      { state: 'armed', surveyModelId: model, referenceStationName: DATUM_STATION, depthFilter: [] },
      { 'If-Match': await versionOf(page, watchPath) },
    );
    const report = (name: string, kind: string, stationName: string | null, depthM: number | null) =>
      asPerson(page, 'POST', `${watchPath}/events`, {
        caverIds: [idOf(name)], kind, stationName, depthM, teamId: null, note: null, recordedAt: null,
      });
    // Two underground, each last heard at a place; one in and out again; one never heard from.
    await report(ana, 'entered', null, null);
    await report(bujor, 'entered', null, null);
    await report(corina, 'entered', null, null);
    await report(ana, 'atStation', REPORTED_STATION, null);
    await report(bujor, 'atDepth', null, REPORTED_DEPTH);
    await report(corina, 'exited', null, null);
    // A depth is recorded at the station the survey holds nearest to it below the datum, which is
    // the server's to work out: read back, since it is one more name that must not be drawn.
    const depthStation = (
      await asPerson<{ items: { kind: string; stationName: string | null }[] }>(
        page,
        'GET',
        `${watchPath}/events?page=1&pageSize=200`,
      )
    ).items.find((row) => row.kind === 'atDepth')?.stationName;
    expect(depthStation, 'the station the reported depth was recorded at').toBeTruthy();
    const stations = [ANY_STATION, DATUM_STATION, REPORTED_STATION, depthStation!];

    const placesIn = async () => {
      // The station names are looked for in everything the page holds, hover texts included. The
      // depth only in what it reads as: a figure of that shape is an ordinary coordinate in the
      // drawing of an icon.
      const markup = await page.content();
      const text = await page.locator('body').innerText();
      return {
        station: stations.some((name) => markup.includes(name)),
        depth: text.includes(String(REPORTED_DEPTH)),
      };
    };

    // ---- The positive case: this reader is told the places, on the trip's own tracking ----
    // Without this, "no place on the camp's screen" would pass on reports that carried none, or
    // for a reader who is told none anywhere.
    await page.goto(`/trip-logs/${tracked.id}?tab=tracking`);
    await page.waitForURL((url) => url.pathname === `/trip-logs/${tracked.id}`, { timeout: 60_000 });
    const told = page.getByTestId('trip-tracking-participants');
    await expect(told.getByRole('row', { name: new RegExp(ana) })).toContainText(REPORTED_STATION, {
      timeout: 60_000,
    });
    await expect(told.getByRole('row', { name: new RegExp(bujor) })).toContainText(
      String(REPORTED_DEPTH),
    );
    expect(await placesIn(), 'the trip’s own tracking, for this reader').toEqual({
      station: true,
      depth: true,
    });

    // ---- The camp: both trips are its trips ----
    await page.goto(`/expeditions/${camp.id}?tab=trips`);
    await page.waitForURL((url) => url.pathname === `/expeditions/${camp.id}`, { timeout: 60_000 });
    const campTrips = page.getByTestId('expedition-trips-tab');
    await expect(campTrips).toContainText(trackedTitle, { timeout: 60_000 });
    // The untracked trip is one of the camp's, which is what makes its absence below a statement
    // about tracking rather than about a trip that never joined.
    await expect(campTrips).toContainText(untrackedTitle);

    // ---- The head count, reached the way a person reaches it: by its tab ----
    const received = headCountsReceived(page, camp.id);
    await page.getByRole('tab', { name: 'Who is underground' }).click();
    await expect(page).toHaveURL(/[?&]tab=watch\b/);
    const section = page.getByTestId('expedition-watch-tab');
    const card = section.getByTestId(`expedition-watch-trip-${tracked.id}`);
    await expect(card).toBeVisible({ timeout: 60_000 });
    await expect(card.getByTestId('expedition-watch-trip-link')).toHaveText(trackedTitle);
    await expect(card.getByTestId('expedition-watch-state-armed')).toHaveText('Tracking');
    await expect(card.getByTestId('expedition-watch-count-underground')).toHaveText('2');
    await expect(card.getByTestId('expedition-watch-count-out')).toHaveText('1');
    await expect(card.getByTestId('expedition-watch-count-unheard')).toHaveText('1');
    // Each person by name, with the standing the count put them in.
    const standingOf = (name: string) =>
      card.getByTestId(`expedition-watch-person-${idOf(name)}`);
    for (const [name, standing, words] of [
      [ana, 'underground', 'Underground'],
      [bujor, 'underground', 'Underground'],
      [corina, 'out', 'Out'],
      [dan, 'unheard', 'Not heard from'],
    ] as const) {
      await expect(standingOf(name)).toContainText(name);
      await expect(standingOf(name).getByTestId(`expedition-watch-standing-${standing}`)).toHaveText(
        words,
      );
    }
    // The party has been heard from, and the card says so rather than "no word yet".
    await expect(card.getByTestId('expedition-watch-last-heard')).not.toContainText('no word yet');
    await expect(card.getByTestId('expedition-watch-closed-at')).toHaveCount(0);

    // The trip nobody tracked has no card: not an empty party, not a row of zeros.
    await expect(section.getByTestId(/^expedition-watch-trip-[0-9a-f-]{36}$/)).toHaveCount(1);
    await expect(section.getByTestId(`expedition-watch-trip-${untracked.id}`)).toHaveCount(0);
    await expect(section).not.toContainText(untrackedTitle);
    await expect(section).not.toContainText(emil);
    await expect(section.getByTestId('expedition-watch-empty')).toHaveCount(0);

    // No place — on a page whose reader was shown both a moment ago.
    expect(await placesIn(), 'the camp’s head count, for the same reader').toEqual({
      station: false,
      depth: false,
    });

    // ---- The watch is closed while the section is open ----
    await asPerson(page, 'PUT', watchPath, { state: 'closed' }, {
      'If-Match': await versionOf(page, watchPath),
    });
    // Nothing is pressed and the page is not opened again: the section asks by itself for as
    // long as it is on screen, and that is how it learns this watch has stopped.
    await expect(card.getByTestId('expedition-watch-state-closed')).toHaveText('Tracking closed', {
      timeout: 120_000,
    });
    await expect(card.getByTestId('expedition-watch-state-armed')).toHaveCount(0);
    await expect(card.getByTestId('expedition-watch-closed-at')).toBeVisible();
    // Closing a watch reports nothing about anybody: the count stands as it was left.
    await expect(card.getByTestId('expedition-watch-count-underground')).toHaveText('2');
    await expect(card.getByTestId('expedition-watch-count-out')).toHaveText('1');
    await expect(card.getByTestId('expedition-watch-count-unheard')).toHaveText('1');
    await expect(section.getByTestId(`expedition-watch-trip-${untracked.id}`)).toHaveCount(0);
    expect(await placesIn(), 'the camp’s head count once the watch is closed').toEqual({
      station: false,
      depth: false,
    });

    // ---- What that screen was drawn from carried no place either ----
    const bodies = await received();
    // At least the first read and the one that brought the closing.
    expect(bodies.length, 'reads of the head count this browser received').toBeGreaterThan(1);
    const carrying = (needle: string) => bodies.filter((body) => body.includes(needle)).length;
    // The search finds what is there: every read names the party.
    expect(carrying(ana), 'reads naming somebody on the tracked trip').toBe(bodies.length);
    for (const name of stations) {
      expect(carrying(name), `reads carrying the station ${name}`).toBe(0);
    }
    expect(carrying(String(REPORTED_DEPTH)), 'reads carrying the reported depth').toBe(0);
    expect(carrying(model), 'reads naming the survey').toBe(0);
    expect(carrying(cave.id), 'reads naming the cave').toBe(0);
    expect(carrying(emil), 'reads naming somebody on the trip nobody tracked').toBe(0);

    // ---- Where each person is stays one link away, on the trip's own tracking ----
    await card.getByTestId('expedition-watch-trip-link').click();
    await page.waitForURL(
      (url) => url.pathname === `/trip-logs/${tracked.id}` && url.searchParams.get('tab') === 'tracking',
      { timeout: 60_000 },
    );
    await expect(
      page.getByTestId('trip-tracking-participants').getByRole('row', { name: new RegExp(ana) }),
    ).toBeVisible({ timeout: 60_000 });
  } finally {
    // Off the camp and the trip before their rows go, so no page of this run reads a deleted
    // record and files the refusal with the console guard.
    try {
      await gotoRoute(page, '/expeditions');
    } catch {
      // The failure that brought the flow here is the one to report.
    }
    if (campId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/expeditions/${campId}`);
    }
    for (const tripId of tripIds) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    if (modelId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/survey-models/${modelId}`);
    }
    if (caveId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
    }
  }
});
