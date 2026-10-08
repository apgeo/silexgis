// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page, type Request, type Route } from '@playwright/test';
import { asPerson, tripBody, tryAsPerson } from './arrange.ts';
import type { ConsoleErrorGuard } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A report recorded where the server cannot be reached: kept in the browser, still there after the
 * page is loaded again, sent by itself when the connection returns — and on the log once.
 *
 * <b>Why a real browser.</b> Everything this rests on is the browser's own and is stubbed in every
 * component suite: storage that outlives the page, the browser's notice that the connection is
 * back, a request that leaves and is never answered. And the promise being checked is about a
 * second system — that the server, sent the same report twice, writes it once — which no client
 * test can see at all. Here the server is real and so is the log it writes; only the outage is
 * arranged.
 *
 * <b>Two outages, because "no answer" hides two different truths.</b> In the first the request
 * never left the phone, so the report is not on the log and the send that follows must write it.
 * In the second the request arrived, the server wrote the report, and the answer was lost on the
 * way back: the browser knows exactly as little as in the first case, holds the report just the
 * same, and the send that follows must write nothing. The person cannot tell these apart and
 * neither can the page, so the two walks end on the same sentence and the same log of one row.
 *
 * Everything is stood up by the run and removed after it: a cave with the committed survey fixture,
 * a trip with one invented person on it, a watch started on that survey.
 */

const fixture = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'P8_Master.3d');

/**
 * What every kept report is stored under in the browser. Spelled out here rather than read from
 * the application: the walk counts what is in storage, and a count taken through the application's
 * own name for it would agree with the application however that name came to change.
 */
const KEPT_UNDER = 'silexgis.trackingOutbox.';

interface Report {
  id: string;
  caverId: string;
  kind: string;
  note: string | null;
  recordedAt: string;
}

interface Scene {
  tripId: string;
  /** The one address a new report is sent to. */
  reportsPath: string;
  logOf: () => Promise<Report[]>;
}

/** What was made, removed again when a test ends — passed or failed. */
const made: { trips: string[]; models: string[]; caves: string[] } = {
  trips: [],
  models: [],
  caves: [],
};

/** Takes down whatever the walks stood up. For a spec's `afterEach`. */
export async function removeWhatWasMade(page: Page) {
  for (const tripId of made.trips.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
  }
  for (const modelId of made.models.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/survey-models/${modelId}`);
  }
  for (const caveId of made.caves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
}

/** A cave, its survey, a trip with one person on it and a running watch — with the tab open. */
async function aRunningWatch(page: Page, what: string): Promise<Scene> {
  const stamp = Date.now();
  await login(page);

  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E Held Reports Cave ${what} ${stamp}`,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
  const caveId = cave.id;
  made.caves.push(caveId);

  const uploaded = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${await bearerToken(page)}` },
    multipart: {
      file: {
        name: 'P8_Master.3d',
        mimeType: 'application/octet-stream',
        buffer: readFileSync(fixture),
      },
    },
  });
  expect(uploaded.ok(), `the survey upload answered ${uploaded.status()}`).toBeTruthy();
  const models = await asPerson<{ id: string }[]>(
    page,
    'GET',
    `/api/v1/caves/${caveId}/survey-models`,
  );
  made.models.push(models[0].id);

  const trip = await asPerson<{ id: string }>(
    page,
    'POST',
    '/api/v1/trip-logs',
    tripBody(`E2E held reports ${what} ${stamp}`, new Date().toISOString().slice(0, 10), {
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

  const token = await bearerToken(page);
  const watch = await page.request.get(`/api/v1/trip-logs/${trip.id}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(watch.ok(), 'the watch could not be read').toBeTruthy();
  const etag = watch.headers()['etag'];
  await asPerson(
    page,
    'PUT',
    `/api/v1/trip-logs/${trip.id}/tracking`,
    { state: 'armed', surveyModelId: models[0].id, referenceStationName: null, depthFilter: [] },
    etag ? { 'If-Match': etag } : {},
  );

  await page.goto(`/trip-logs/${trip.id}?tab=tracking`);
  await page.waitForURL((url) => url.pathname === `/trip-logs/${trip.id}`, { timeout: 60_000 });
  await expect(page.getByTestId('trip-tracking-record')).toBeVisible({ timeout: 60_000 });

  return {
    tripId: trip.id,
    reportsPath: `/api/v1/trip-logs/${trip.id}/tracking/events`,
    // Asked with the session as it is at that moment: the page is loaded again part-way through,
    // and the token it held before is not the one it holds after.
    logOf: async () =>
      (
        await asPerson<{ items: Report[] }>(
          page,
          'GET',
          `/api/v1/trip-logs/${trip.id}/tracking/events?page=1&pageSize=200`,
        )
      ).items,
  };
}

/** Ticks the one person on the trip, writes the note and presses Record. */
async function reportThatSheWentIn(page: Page, note: string) {
  await page
    .getByTestId('trip-tracking-participants')
    .getByRole('row', { name: /E2E Carmen/ })
    .getByRole('checkbox')
    .check();
  await page.getByTestId('trip-tracking-note').fill(note);
  await page.getByTestId('trip-tracking-record').click();
}

/** How many reports this browser is keeping. A count: what they say is never read out. */
const keptInStorage = (page: Page) =>
  page.evaluate(
    (prefix) => Object.keys(window.localStorage).filter((key) => key.startsWith(prefix)).length,
    KEPT_UNDER,
  );

/** The page says the report is held, in the three places it says so. */
async function expectHeld(page: Page) {
  await expect(page.getByText(/No answer from the server — not sent yet/)).toBeVisible({
    timeout: 15_000,
  });
  await expect(page.getByTestId('trip-tracking-outbox-count')).toHaveText('Held reports: 1');
  // In words, where the text is: a note about a person is sitting on this phone.
  await expect(page.getByTestId('trip-tracking-outbox-kept')).toContainText('kept in this browser');
  await expect(page.getByTestId('held-reports')).toBeVisible();
}

/** The page has nothing held any more, and says what became of the one it had. */
async function expectSentAndGone(page: Page, timeout = 30_000) {
  await expect(page.getByText('Held reports sent: 1.')).toBeVisible({ timeout });
  await expect(page.getByTestId('trip-tracking-outbox')).toHaveCount(0);
  await expect(page.getByTestId('held-reports')).toHaveCount(0);
  expect(await keptInStorage(page)).toBe(0);
}

/** Leaves the tab before its trip is removed, so its own polling never reads a trip that is gone. */
async function leave(page: Page) {
  await gotoRoute(page, '/trip-logs');
}

/**
 * No signal at all: the report is held, survives the page being loaded again, and leaves by itself
 * when the connection returns — once, and at the minute it was about.
 */
export async function aReportWithNoSignalIsHeldAndSentOnReturn(
  page: Page,
  guard: ConsoleErrorGuard,
) {
  guard.allow(
    /net::ERR_INTERNET_DISCONNECTED/,
    "the outage this test drives: the browser is taken offline, and each request it then refuses is one line of the browser's own",
  );
  guard.allow(
    /Failed to load resource: net::ERR_FAILED/,
    "the outage this test drives: with the browser back online the report's own request is still cut off, so that the page can be loaded again while the report cannot yet be sent",
  );

  const NOTE = `E2E typed with no signal ${Date.now()}`;
  const scene = await aRunningWatch(page, 'no signal');
  const isTheReport = (request: Request) =>
    request.method() === 'POST' && new URL(request.url()).pathname === scene.reportsPath;
  const offline = (is: boolean) =>
    page.waitForFunction((expected) => navigator.onLine === !expected, is);

  // ---- No signal: the report is composed, and held ----
  const composedFrom = Date.now();
  await page.context().setOffline(true);
  await offline(true);
  await reportThatSheWentIn(page, NOTE);
  await expectHeld(page);
  const heldBy = Date.now();
  expect(await keptInStorage(page)).toBe(1);
  // The card let the report go: left in the field it would be sent again as a second report.
  await expect(page.getByTestId('trip-tracking-note')).toHaveValue('');

  // ---- The connection is back for everything but the report ----
  // A page cannot be loaded with no network at all — nothing of this application is kept for
  // that — so the outage is narrowed to the one request that matters. The browser announces the
  // connection, the page tries the report by itself, and that try gets no answer either.
  const cutOff = (route: Route) =>
    route.request().method() === 'POST' ? route.abort('failed') : route.fallback();
  const reports = (url: URL) => url.pathname === scene.reportsPath;
  await page.route(reports, cutOff);
  const triedOnReturn = page.waitForEvent('requestfailed', {
    predicate: isTheReport,
    timeout: 60_000,
  });
  await page.context().setOffline(false);
  expect((await triedOnReturn).failure()?.errorText).toBe('net::ERR_FAILED');
  // Never reached the server: what follows is the first time it is written.
  expect((await scene.logOf()).filter((row) => row.note === NOTE)).toHaveLength(0);

  // ---- The page is loaded again, and the report is still held ----
  const triedOnOpening = page.waitForEvent('requestfailed', {
    predicate: isTheReport,
    timeout: 90_000,
  });
  await page.reload();
  await expect(page.getByTestId('trip-tracking-outbox-count')).toHaveText('Held reports: 1', {
    timeout: 60_000,
  });
  await expect(page.getByTestId('held-reports')).toBeVisible();
  expect(await keptInStorage(page)).toBe(1);
  // Opening the page is itself a reason to try, and that try is cut off like the one before it.
  // Waited for, so that the send below is provably the one the returning connection started.
  await triedOnOpening;
  await expect(page.getByTestId('trip-tracking-outbox-count')).toHaveText('Held reports: 1');
  expect((await scene.logOf()).filter((row) => row.note === NOTE)).toHaveLength(0);

  // ---- The connection returns: sent by itself, nothing pressed ----
  await page.context().setOffline(true);
  await offline(true);
  await page.unroute(reports, cutOff);
  await page.context().setOffline(false);
  await expectSentAndGone(page);

  // ---- On the log once, at the minute it was about ----
  await expect(page.getByTestId('trip-tracking-events')).toContainText(NOTE, { timeout: 30_000 });
  const written = (await scene.logOf()).filter((row) => row.note === NOTE);
  expect(written).toHaveLength(1);
  expect(written[0].kind).toBe('entered');
  // The browser's clock when it was composed — before the page said it was held — and not the
  // server's when the connection came back, which is a page load and two outages later.
  const at = Date.parse(written[0].recordedAt);
  expect(at).toBeGreaterThanOrEqual(composedFrom);
  expect(at).toBeLessThanOrEqual(heldBy);

  await leave(page);
}

/**
 * The request arrived and the answer did not: the report is held like any other, and sending it
 * again writes nothing — the log has the one row the first send wrote. The browser is online
 * throughout, so this is also the walk in which nothing but the page itself can start that send.
 */
export async function aReportWhoseAnswerWasLostIsNotWrittenTwice(
  page: Page,
  guard: ConsoleErrorGuard,
) {
  guard.allow(
    /Failed to load resource: net::ERR_FAILED/,
    "the outage this test drives: the report's request is let through to the server and its answer is dropped on the way back",
  );

  const NOTE = `E2E answer lost on the way back ${Date.now()}`;
  const scene = await aRunningWatch(page, 'answer lost');

  // ---- The server takes the report; the browser never hears so ----
  /** What the server answered each time, although the page was told nothing. */
  const answered: number[] = [];
  const answerLost = async (route: Route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    answered.push((await route.fetch()).status());
    await route.abort('failed');
  };
  const reports = (url: URL) => url.pathname === scene.reportsPath;
  await page.route(reports, answerLost);

  await reportThatSheWentIn(page, NOTE);
  await expectHeld(page);
  // The page tries again by itself every few seconds, and each of those is taken and lost the
  // same way: however many there have been by now, the server said yes to every one.
  expect(answered.length).toBeGreaterThanOrEqual(1);
  expect(answered.every((status) => status === 200)).toBe(true);
  expect(await keptInStorage(page)).toBe(1);
  // Both are true at once, and neither the page nor the person can know it: held here, and
  // already on the log.
  const first = (await scene.logOf()).filter((row) => row.note === NOTE);
  expect(first).toHaveLength(1);

  // ---- Sent again by itself, with nothing pressed ----
  // The browser never thought itself offline here, so it announces no connection: what sends the
  // report is the page trying again a little later, and the server answering its other requests.
  // The waits between tries grow to a minute, so this is given longer than one.
  await page.unroute(reports, answerLost);
  await expectSentAndGone(page, 90_000);

  // ---- Still one row, and it is the row the first send wrote ----
  await expect(page.getByTestId('trip-tracking-events')).toContainText(NOTE, { timeout: 30_000 });
  const after = (await scene.logOf()).filter((row) => row.note === NOTE);
  expect(after).toHaveLength(1);
  expect(after[0].id).toBe(first[0].id);
  expect(after[0].recordedAt).toBe(first[0].recordedAt);

  await leave(page);
}
