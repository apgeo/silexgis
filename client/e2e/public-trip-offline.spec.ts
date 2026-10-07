// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page, type Route } from '@playwright/test';
import { asPerson, tripBody, tryAsPerson } from './arrange.ts';
import { ownContext, test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A published trip's page while the server cannot be reached — a restart, a proxy with nothing
 * behind it, a phone that walked out of signal with the page open.
 *
 * <b>The reader is somebody waiting for a party to come out, and the page has two screens for a
 * reader who has nothing</b> — "nothing to show for this link" and "the trip could not be read" —
 * either of which, put in front of that person in place of the party they were looking at a
 * minute ago, says something untrue and alarming. What they are owed is the party as last read,
 * plus the news that it is no longer being refreshed, and for that news to go away by itself when
 * the server answers again.
 *
 * <b>The trip is real and so is the server.</b> Only the outage is arranged: every read a
 * published trip's page makes is cut off in the visitor's browser, in mid-visit, and later let
 * through again. While it is cut the coordinator reports the party somewhere else, so that what
 * the page shows during the outage is provably what it last read and what it shows afterwards is
 * provably a new read — a notice that merely hid itself would leave the old place standing.
 *
 * <b>The page's clock is the browser's own, moved on by hand.</b> The page re-reads a trip in
 * progress once a minute and gives a failed read several further attempts before calling it
 * failed; waited for in real time that is minutes of a test doing nothing. The clock is moved the
 * way a closed laptop lid moves it — every timer that has come due fires once — and each wait
 * ends on what the page shows, never on how far the clock was moved.
 */

const fixture = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'P8_Master.3d');

/** Two stations of the committed survey fixture: where the party is before and during the outage. */
const STATION_BEFORE = 'p8.p8.98';
const STATION_DURING = 'p8.bens_dig.217';

/**
 * How far the clock is moved at a time. Shorter than the page's shortest interval between reads,
 * so no step can carry it past a read and the next one unobserved; beyond that its size decides
 * nothing, because every wait below ends on the page and not on the clock.
 */
const STRETCH_MS = 30_000;

/** Every read a published trip's page makes of the trip: the party, and both lists behind it. */
const publishedTripReads = (url: URL) => url.pathname.startsWith('/api/v1/public/trips/');

/**
 * The outage. A request refused by the browser's own network stack, which is how a server that
 * is not there looks from inside a page.
 *
 * <b>Deliberately not the failure the suite's runner sets a test aside for.</b> The runner runs a
 * failed test again, and takes the second result, when the browser reported
 * net::ERR_NETWORK_CHANGED during it — the machine's addresses changing under the browser, which
 * no test caused. A request cut off here is reported as net::ERR_FAILED, so nothing this spec
 * does to itself can earn that second chance, and a real failure of it stands the first time.
 * The test asserts that below instead of trusting it.
 */
const cutOff = (route: Route) => route.abort('failed');

/** What was made, removed again when the test ends — passed or failed. */
const made: { trips: string[]; caves: string[] } = { trips: [], caves: [] };

test.afterEach(async ({ page }) => {
  for (const tripId of made.trips.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
  }
  for (const caveId of made.caves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

/** Points a trip's watch at a survey and starts it, honouring the version the read carried. */
async function startWatch(page: Page, tripId: string, surveyModelId: string) {
  const token = await bearerToken(page);
  const read = await page.request.get(`/api/v1/trip-logs/${tripId}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(read.ok(), `the watch of ${tripId} could not be read`).toBeTruthy();
  const etag = read.headers()['etag'];
  await asPerson(
    page,
    'PUT',
    `/api/v1/trip-logs/${tripId}/tracking`,
    { state: 'armed', surveyModelId, referenceStationName: null, depthFilter: [] },
    etag ? { 'If-Match': etag } : {},
  );
}

/**
 * Moves the page's clock on until the page shows what is waited for.
 *
 * A stretch at a time, looking in between, because what is waited for is several things the page
 * does one after another — a read coming due, that read failing, each further attempt — and each
 * is only scheduled once the one before it has come back from the network.
 */
async function letTimePassUntil(page: Page, reached: () => Promise<boolean>, what: string) {
  await expect(async () => {
    if (!(await reached())) {
      await page.clock.fastForward(STRETCH_MS);
    }
    expect(await reached(), what).toBe(true);
  }).toPass({ timeout: 120_000 });
}

test('a published trip stays on screen, and says it is not being refreshed, while the server cannot be reached', async ({
  page,
  browser,
  consoleErrors: guard,
}) => {
  test.setTimeout(300_000);
  guard.allow(
    /Failed to load resource: net::ERR_FAILED/,
    "the outage this test drives: each read of the published trip it cuts off is one line of the browser's own",
  );

  const stamp = Date.now();
  const tripTitle = `E2E while the server is away ${stamp}`;
  await login(page);

  // ---- A cave, its survey, and a party underground with a link published ----
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(`E2E Outage Cave ${stamp}`);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: `E2E Outage Cave ${stamp}` })).toBeVisible({
    timeout: 15_000,
  });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId, 'the cave page names the cave in its address').toBeTruthy();
  made.caves.push(caveId!);

  const uploaded = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${await bearerToken(page)}` },
    multipart: {
      file: { name: 'P8_Master.3d', mimeType: 'application/octet-stream', buffer: readFileSync(fixture) },
    },
  });
  expect(uploaded.status(), await uploaded.text()).toBe(201);
  const models = await asPerson<{ id: string }[]>(page, 'GET', `/api/v1/caves/${caveId}/survey-models`);

  const trip = await asPerson<{ id: string; participants: { caverId: string; name: string }[] }>(
    page,
    'POST',
    '/api/v1/trip-logs',
    tripBody(tripTitle, new Date().toISOString().slice(0, 10), {
      caveIds: [caveId],
      participants: [
        {
          caverId: null,
          newCaverName: 'E2E Carmen',
          roleId: null,
          entryTime: null,
          exitTime: null,
          note: null,
        },
      ],
    }),
  );
  made.trips.push(trip.id);
  const carmen = trip.participants[0].caverId;

  await startWatch(page, trip.id, models[0].id);
  const report = (kind: string, stationName: string | null) =>
    asPerson(page, 'POST', `/api/v1/trip-logs/${trip.id}/tracking/events`, {
      caverIds: [carmen],
      kind,
      stationName,
      depthM: null,
      teamId: null,
      note: null,
      recordedAt: null,
    });
  await report('entered', null);
  await report('atStation', STATION_BEFORE);
  const share = await asPerson<{ id: string; token: string }>(
    page,
    'POST',
    `/api/v1/trip-logs/${trip.id}/tracking/shares`,
  );
  expect(share.token).toBeTruthy();

  // ---- The visitor: a browser holding nothing but the link, with a clock that can be moved ----
  const anonymous = await ownContext(browser);
  try {
    const pub = await anonymous.newPage();
    // Before the page exists: a timer the page set on the real clock could not be moved on later.
    await pub.clock.install();

    /** How the browser reported each read of the published trip that did not come back. */
    const lost: string[] = [];
    pub.on('requestfailed', (request) => {
      if (publishedTripReads(new URL(request.url()))) {
        lost.push(request.failure()?.errorText ?? 'no reason given');
      }
    });
    /** Reads of the published trip the server answered. */
    let answered = 0;
    pub.on('response', (response) => {
      if (publishedTripReads(new URL(response.url())) && response.ok()) {
        answered += 1;
      }
    });

    const party = pub.getByTestId('public-trip-party');
    const stale = pub.getByTestId('public-trip-stale');
    const staleShown = () => stale.isVisible();
    const staleGone = async () => (await stale.count()) === 0;

    // ======== The page ========
    await pub.goto(`/shared/trips/${share.token}`);
    await expect(pub.getByTestId('public-trip-title')).toHaveText(tripTitle, { timeout: 30_000 });
    await expect(pub.getByTestId('public-trip-state-armed')).toBeVisible();
    await expect(party).toContainText('E2E Carmen');
    await expect(party).toContainText(STATION_BEFORE);
    await expect(pub.getByTestId('public-trip-count-underground')).toHaveText('1');
    // The twin of everything asserted absent or present below: a page being refreshed says
    // nothing about not being refreshed.
    await expect(stale).toHaveCount(0);
    expect(lost).toEqual([]);

    // ---- The server goes away, and meanwhile the party moves on ----
    await pub.route(publishedTripReads, cutOff);
    await report('atStation', STATION_DURING);
    await letTimePassUntil(pub, staleShown, 'the page never said it had stopped refreshing');

    await expect(stale).toContainText('This page has stopped refreshing');
    // The party is still there, as last read — the place reported during the outage is the proof
    // that this is the earlier read and not a later one.
    await expect(pub.getByTestId('public-trip-title')).toHaveText(tripTitle);
    await expect(pub.getByTestId('public-trip-state-armed')).toBeVisible();
    await expect(party).toContainText('E2E Carmen');
    await expect(party).toContainText(STATION_BEFORE);
    await expect(party).not.toContainText(STATION_DURING);
    await expect(pub.getByTestId('public-trip-count-underground')).toHaveText('1');
    // Neither of the screens for a reader who has nothing, and not the link's end either: the
    // server said nothing at all, which is not the same as saying no.
    await expect(pub.getByTestId('public-trip-not-found')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-unreachable')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-ended')).toHaveCount(0);

    // What this spec cut off is reported as a plain failure, never as the network changing —
    // the one report the suite's runner answers by running a failed test again.
    expect(lost.length, 'no read of the published trip was cut off').toBeGreaterThan(0);
    expect([...new Set(lost)]).toEqual(['net::ERR_FAILED']);

    // ---- The server comes back, and nobody touches the page ----
    const answeredBefore = answered;
    await pub.unroute(publishedTripReads, cutOff);
    await letTimePassUntil(pub, staleGone, 'the notice stayed up after the server came back');

    expect(answered).toBeGreaterThan(answeredBefore);
    await expect(party).toContainText(STATION_DURING);
    await expect(party).not.toContainText(STATION_BEFORE);
    await expect(pub.getByTestId('public-trip-not-found')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-unreachable')).toHaveCount(0);

    // ======== The frame a club pastes into an article ========
    //
    // A drawing with markers and no list beside it: nothing on it has a time, so it reads as now
    // for as long as nothing says otherwise.
    await pub.goto(`/shared/trips/${share.token}/embed`);
    const drawn = pub.getByTestId('caveview-tracking');
    // The list of the party is drawn only once the survey has loaded, so its presence is the load.
    await expect(drawn).toContainText('E2E Carmen', { timeout: 90_000 });
    await expect(pub.getByTestId('public-trip-embed')).toBeVisible();
    await expect(stale).toHaveCount(0);

    const lostBefore = lost.length;
    await pub.route(publishedTripReads, cutOff);
    await letTimePassUntil(pub, staleShown, 'the frame never said it had stopped refreshing');

    await expect(stale).toContainText('Not refreshing');
    // Still the drawing and still the party on it: not the frame's "could not be read", not its
    // "nothing to show", and not the end of the link.
    await expect(pub.getByTestId('public-trip-embed')).toBeVisible();
    await expect(drawn).toContainText('E2E Carmen');
    await expect(pub.getByTestId('public-trip-embed-unreachable')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-embed-failure')).toHaveCount(0);
    await expect(pub.getByTestId('public-trip-ended')).toHaveCount(0);
    expect(lost.length).toBeGreaterThan(lostBefore);
    expect([...new Set(lost)]).toEqual(['net::ERR_FAILED']);

    const answeredInFrame = answered;
    await pub.unroute(publishedTripReads, cutOff);
    await letTimePassUntil(pub, staleGone, 'the frame kept its notice after the server came back');

    expect(answered).toBeGreaterThan(answeredInFrame);
    await expect(pub.getByTestId('public-trip-embed')).toBeVisible();
    await expect(drawn).toContainText('E2E Carmen');
  } finally {
    await anonymous.close();
  }
});
