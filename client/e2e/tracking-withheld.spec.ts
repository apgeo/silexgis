// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import {
  asPerson,
  localDay,
  registerAccount,
  signedInElsewhere,
  surveyRead,
  tripBody,
  tryAsPerson,
  versionOf,
} from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A trip's watch, read by somebody who may read the trip and may not be told where its cave is.
 *
 * A cave whose position is protected keeps it from most of the people who can read a trip there,
 * and a tracked position — a station of the survey, a depth below its datum — is that position by
 * another name. So the watch such a reader is sent says who went in and who is out, and withholds
 * every place. That rule is decided on the server and has tests of its own there. What only a
 * browser can show is the other half: that the page drawn from such an answer says so, row by row,
 * and that nothing that browser was sent — not just the two answers the tab is drawn from — holds a
 * station or a depth.
 *
 * <b>Why the watched page is the administrator's and the reader has a browser of their own.</b>
 * The administrator sets the scene and is the positive case: the same trip, opened by somebody who
 * may be told, shows the station and the depth, so a page that showed nobody anything would fail
 * here rather than pass. The reader is an account registered for this run — it holds no rule about
 * the cave at all, which is the state being tested; an account in the seeded editors' group would
 * read past the cave's visibility and prove nothing.
 *
 * <b>Two readers in turn, because the page says different things to them.</b> First the account as
 * registered, which may only read the trip. Then the same account once it has been let write to the
 * trip's log — a coordinator on the surface who was never told where the entrance is — which is the
 * reader the setup card warns that the watch has a survey and a datum they are not being shown.
 *
 * Everything is stood up by this run and taken down after it. The survey is the committed fixture;
 * the people are invented.
 */

/** The station the depths of this watch are measured from. */
const DATUM_STATION = 'p8.p8.98';
/** The station one of the two is reported at. */
const REPORTED_STATION = 'p8.p8.97';
/** What every station of the fixture survey begins with: no name of that shape may travel at all. */
const ANY_STATION = 'p8.p8.';
/**
 * The depth the other is reported at. One decimal, which is what the log keeps, and a figure that
 * the text of a page cannot hold by accident: a moment is written with a colon before its seconds
 * and the run's stamps hold no point.
 */
const REPORTED_DEPTH = 137.5;

interface Received {
  path: string;
  status: number;
  /** Null when the browser moved on before the body could be read. */
  body: string | null;
}

/**
 * The answers whose body was lost and that no read answer stands in for.
 *
 * A search over what a browser was sent is a search over the bodies that could be read, and a body
 * cut off by a navigation cannot be. Such an answer is accounted for only when the same address
 * answered the same way at another moment and that body was read; otherwise something reached the
 * browser that the search never looked at, and it is named here instead of being passed over.
 * Redirects are left out: they have no body to read.
 */
function neverSearched(received: Received[]): string[] {
  const searched = new Set(
    received.filter((one) => one.body !== null).map((one) => `${one.status} ${one.path}`),
  );
  const lost = received
    .filter((one) => one.body === null && (one.status < 300 || one.status >= 400))
    .map((one) => `${one.status} ${one.path}`)
    .filter((one) => !searched.has(one));
  return [...new Set(lost)];
}

/**
 * Everything the application's server answers this browser from here on, bodies included.
 *
 * Kept whole rather than narrowed to the two tracking reads: the question is whether a place
 * reached this person's browser, and a place that arrived on the trip's own read or on a list of the
 * cave's surveys has arrived all the same.
 */
function everythingAnswered(page: Page) {
  const reading: Promise<void>[] = [];
  const received: Received[] = [];
  page.on('response', (response) => {
    const { pathname, search } = new URL(response.url());
    if (!pathname.startsWith('/api/')) {
      return;
    }
    const entry: Received = { path: pathname + search, status: response.status(), body: null };
    received.push(entry);
    reading.push(
      response.text().then(
        (body) => {
          entry.body = body;
        },
        () => {
          // Left as unread: a navigation discards the bodies still arriving.
        },
      ),
    );
  });
  return async () => {
    await Promise.all(reading);
    return received;
  };
}

/** What the server answers this person for one address — its status and what it says. */
async function answerFor(page: Page, path: string): Promise<{ status: number; body: unknown }> {
  const token = await bearerToken(page);
  const response = await page.request.get(path, { headers: { Authorization: `Bearer ${token}` } });
  const text = await response.text();
  return { status: response.status(), body: text.length > 0 ? (JSON.parse(text) as unknown) : null };
}

/** A refusal with the parts that differ from one request to the next left out. */
function refusalShape(body: unknown) {
  const rest = { ...((body ?? {}) as Record<string, unknown>) };
  delete rest.traceId;
  delete rest.instance;
  return rest;
}

/** Opens the trip's tracking tab by its address and waits until the watch is drawn. */
async function openWatch(page: Page, tripId: string) {
  // Not through `gotoRoute`, whose wait compares the path alone after the frame is drawn — the tab
  // is in the query string, and the table below is the better proof that the page arrived.
  await page.goto(`/trip-logs/${tripId}?tab=tracking`);
  await page.waitForURL((url) => url.pathname === `/trip-logs/${tripId}`, { timeout: 60_000 });
  await expect(page.getByTestId('trip-tracking-participants')).toBeVisible({ timeout: 60_000 });
}

test('somebody who may read a trip but not where its cave is is told who is underground and never where', async ({
  page,
  browser,
  request,
  consoleErrors,
}) => {
  // Two people and three full sign-ins, and a survey the server has to read before a watch can be
  // started on it.
  test.slow();
  const stamp = Date.now();
  const ana = `E2E Withheld Ana ${stamp}`;
  const bujor = `E2E Withheld Bujor ${stamp}`;
  const reader = await registerAccount(request, 'withheld');

  await login(page);
  let caveId: string | undefined;
  let modelId: string | undefined;
  let tripId: string | undefined;
  const theirs = await signedInElsewhere(browser, reader);
  const answered = everythingAnswered(theirs.page);

  try {
    // ---- The scene, as the administrator: a protected cave, its survey, a watch with two reports ----
    const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
    const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
      name: `E2E Withheld Cave ${stamp}`,
      caveTypeId: caveTypes[0].id,
      // Readable by anybody signed in: what is kept from the reader is where the cave is, not that
      // it exists. A cave they could not read at all would be a different refusal, earlier.
      visibility: 'authenticated',
      locationProtected: true,
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
    await surveyRead(page, model);

    const person = (name: string) => ({
      caverId: null, newCaverName: name, roleId: null, entryTime: null, exitTime: null, note: null,
    });
    const trip = await asPerson<{ id: string; participants: { caverId: string; name: string }[] }>(
      page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(`E2E Withheld Trip ${stamp}`, localDay(0), {
        caveIds: [cave.id],
        participants: [person(ana), person(bujor)],
      }),
    );
    tripId = trip.id;
    const watchPath = `/api/v1/trip-logs/${trip.id}/tracking`;
    const anaId = trip.participants.find((p) => p.name === ana)!.caverId;
    const bujorId = trip.participants.find((p) => p.name === bujor)!.caverId;

    await asPerson(
      page,
      'PUT',
      watchPath,
      { state: 'armed', surveyModelId: model, referenceStationName: DATUM_STATION, depthFilter: [] },
      { 'If-Match': await versionOf(page, watchPath) },
    );
    const report = (caverId: string, kind: string, stationName: string | null, depthM: number | null) =>
      asPerson(page, 'POST', `${watchPath}/events`, {
        caverIds: [caverId], kind, stationName, depthM, teamId: null, note: null, recordedAt: null,
      });
    await report(anaId, 'entered', null, null);
    await report(bujorId, 'entered', null, null);
    await report(anaId, 'atStation', REPORTED_STATION, null);
    await report(bujorId, 'atDepth', null, REPORTED_DEPTH);
    // A depth is recorded at the station the survey holds nearest to it below the datum, which is
    // the server's to work out. Read back rather than assumed: it is a third name that must not
    // travel, and the cave declares no place of its own for this depth — what a cave declares its
    // depths to mean is read with the cave, by a rule of its own, and is not what is tested here.
    const depthStation = (
      await asPerson<{ items: { kind: string; stationName: string | null }[] }>(
        page,
        'GET',
        `${watchPath}/events?page=1&pageSize=200`,
      )
    ).items.find((row) => row.kind === 'atDepth')?.stationName;
    expect(depthStation, 'the station the reported depth was recorded at').toBeTruthy();
    const stations = [ANY_STATION, DATUM_STATION, REPORTED_STATION, depthStation!];

    // ---- The positive case: somebody who may be told is told ----
    // Without this, everything below would pass on a watch whose reports had carried no place.
    await openWatch(page, trip.id);
    const told = page.getByTestId('trip-tracking-participants');
    await expect(told.getByRole('row', { name: new RegExp(ana) })).toContainText(REPORTED_STATION, {
      timeout: 30_000,
    });
    const toldBujor = told.getByRole('row', { name: new RegExp(bujor) });
    await expect(toldBujor).toContainText(depthStation!);
    await expect(toldBujor).toContainText(String(REPORTED_DEPTH));
    await expect(page.getByTestId('trip-tracking-positions-withheld')).toHaveCount(0);
    await expect(page.getByTestId('trip-tracking-position-withheld')).toHaveCount(0);
    // The survey is offered to them, and so is a movie of the watch on it.
    await expect(page.getByTestId('trip-tracking-model-panel')).toBeVisible();
    await expect(page.getByTestId('trip-tracking-movie')).toBeVisible();
    const trackedForKeeper = await answerFor(page, `/api/v1/survey-models/${model}/tracked-trips`);
    expect(trackedForKeeper.status).toBe(200);
    expect((trackedForKeeper.body as { tripLogId: string }[]).map((row) => row.tripLogId)).toContain(
      trip.id,
    );
    expect(
      await asPerson<unknown[]>(page, 'GET', `/api/v1/caves/${cave.id}/survey-models`),
    ).toHaveLength(1);

    // ---- The reader: may read the trip, holds nothing about the cave ----
    const readerPage = theirs.page;
    await openWatch(readerPage, trip.id);

    // Said once at the top…
    const notice = readerPage.getByTestId('trip-tracking-positions-withheld');
    await expect(notice).toBeVisible({ timeout: 30_000 });
    await expect(notice).toContainText('Some positions are not shown to you');
    // …and on each row, beside a standing that is still said: where somebody is is kept back, that
    // they are in the cave is not.
    const party = readerPage.getByTestId('trip-tracking-participants');
    for (const name of [ana, bujor]) {
      const row = party.getByRole('row', { name: new RegExp(name) });
      await expect(row.getByTestId('trip-tracking-standing-underground')).toHaveText('Underground');
      await expect(row.getByTestId('trip-tracking-position-withheld')).toHaveText('Not shown to you');
    }
    await expect(readerPage.getByTestId('trip-tracking-count-underground')).toHaveText('2');
    await expect(readerPage.getByTestId('trip-tracking-count-out')).toHaveText('0');
    // The log says the same of the two reports that were about a place, and nothing of the kind
    // about the two that were not.
    const log = readerPage.getByTestId('trip-tracking-events');
    await expect(log.getByTestId('trip-tracking-position-withheld')).toHaveCount(2, {
      timeout: 30_000,
    });

    // No drawing of the survey is offered, and nothing stands in its place claiming a fault: this
    // reader's page has no panel for a model, so it has no way to open a movie of one either.
    for (const absent of [
      'trip-tracking-model-panel',
      'trip-tracking-model-toggle',
      'trip-tracking-movie',
      'trip-tracking-model-missing',
      'trip-tracking-model-unreadable',
      'trip-tracking-model-unplaceable',
    ]) {
      await expect(readerPage.getByTestId(absent), absent).toHaveCount(0);
    }
    // A reader who cannot change the setup is shown no setup form, and so no warning about one.
    await expect(readerPage.getByTestId('trip-tracking-config-withheld')).toHaveCount(0);
    await expect(readerPage.getByTestId('trip-tracking-reference')).toHaveCount(0);

    const placesIn = async (target: Page) => {
      // The station names are looked for in everything the page holds, hover texts included. The
      // depth only in what it reads as: a figure of that shape is an ordinary coordinate in the
      // drawing of an icon.
      const markup = await target.content();
      const text = await target.locator('body').innerText();
      return {
        station: stations.some((name) => markup.includes(name)),
        depth: text.includes(String(REPORTED_DEPTH)),
      };
    };
    expect(await placesIn(page), 'the page of somebody who may be told').toEqual({
      station: true,
      depth: true,
    });
    expect(await placesIn(readerPage), 'the page of somebody who may not').toEqual({
      station: false,
      depth: false,
    });

    // What the server answers this person, asked directly: the watch and its log with every place
    // empty and the standing kept, the survey as something that is not there.
    const watch = (await answerFor(readerPage, watchPath)).body as {
      positionsWithheld: boolean;
      surveyModelId: string | null;
      referenceStationName: string | null;
      depthFilter: string[];
      participants: {
        stationName: string | null;
        depthM: number | null;
        positionRecordedAt: string | null;
        positionSurveyModelId: string | null;
        in: boolean;
        lastKind: string | null;
      }[];
    };
    expect(watch.positionsWithheld).toBe(true);
    expect([watch.surveyModelId, watch.referenceStationName, watch.depthFilter]).toEqual([null, null, []]);
    expect(watch.participants).toHaveLength(2);
    for (const one of watch.participants) {
      expect([one.stationName, one.depthM, one.positionRecordedAt, one.positionSurveyModelId]).toEqual([
        null, null, null, null,
      ]);
      expect(one.in).toBe(true);
    }
    expect(watch.participants.map((one) => one.lastKind).sort()).toEqual(['atDepth', 'atStation']);
    const reports = (
      (await answerFor(readerPage, `${watchPath}/events?page=1&pageSize=200`)).body as {
        items: { stationName: string | null; depthEnteredM: number | null; surveyModelId: string | null }[];
      }
    ).items;
    expect(reports).toHaveLength(4);
    for (const one of reports) {
      expect([one.stationName, one.depthEnteredM, one.surveyModelId]).toEqual([null, null, null]);
    }

    // The trips tracked on the survey: answered exactly as for a survey that does not exist, so the
    // answer cannot be used to learn that this one does.
    const tracked = await answerFor(readerPage, `/api/v1/survey-models/${model}/tracked-trips`);
    const nowhere = await answerFor(
      readerPage,
      '/api/v1/survey-models/00000000-0000-4000-8000-000000000001/tracked-trips',
    );
    expect(tracked.status).toBe(404);
    expect(nowhere.status).toBe(404);
    expect(refusalShape(tracked.body)).toEqual(refusalShape(nowhere.body));
    expect((tracked.body as { code?: string }).code).toBe('survey_model.not_found');
    // And the cave lists no survey for them.
    const surveys = await answerFor(readerPage, `/api/v1/caves/${cave.id}/survey-models`);
    expect(surveys.status).toBe(200);
    expect(surveys.body).toEqual([]);

    // ---- The same person, let write to the log: a coordinator who was never told where the cave is ----
    await asPerson(page, 'PUT', `/api/v1/objects/tripLog/${trip.id}/access`, {
      entries: [
        {
          subjectKind: 'user',
          subjectId: reader.id,
          effect: 'allow',
          actions: 'read, write',
          scopeKind: 'object',
        },
      ],
    });
    const held = await asPerson<{ actions: string }>(
      readerPage,
      'GET',
      `/api/v1/objects/tripLog/${trip.id}/effective-access`,
    );
    expect(held.actions, 'the rule that lets them write to this trip').toContain('write');
    // A rule about the trip says nothing about the cave: its position is still not theirs.
    expect((await answerFor(readerPage, watchPath)).body).toMatchObject({
      positionsWithheld: true,
      surveyModelId: null,
      referenceStationName: null,
    });

    // The card for reporting asks what places the cave has declared, of the watch rather than of
    // the cave, and somebody who may not place the cave is refused that list instead of being handed
    // an empty one. The refusal is the server keeping the rule; that it was this request and no
    // other is checked below, against everything the browser received.
    consoleErrors.allow(
      /Failed to load resource.*409/,
      'the list of declared places is refused to a coordinator who may not place the cave',
    );
    // Opening the page afresh discards whatever is still arriving, so what has been answered so far
    // is read through first: the first reader's answers are searched below with the second's.
    await answered();
    await openWatch(readerPage, trip.id);
    // Now there is a setup form, and it says that its emptiness is a withholding rather than a
    // watch nobody configured — with the fields closed, so that typing into a blank cannot clear a
    // datum this person was never shown.
    const setup = readerPage.getByTestId('trip-tracking-config-withheld');
    await expect(setup).toBeVisible({ timeout: 30_000 });
    await expect(setup).toContainText('This tracking setup is not shown to you');
    await expect(readerPage.getByTestId('trip-tracking-config-replace')).toBeVisible();
    const datum = readerPage.getByTestId('trip-tracking-reference');
    await expect(datum).toBeDisabled();
    await expect(datum).toHaveValue('');
    // The chooser is closed too. Wherever the component library hangs the name — on the control or
    // on the field inside it — the closed control is the one that carries it.
    const chooser = readerPage.getByTestId('trip-tracking-model');
    await expect(
      readerPage
        .locator('.ant-select-disabled')
        .filter({ has: chooser })
        .or(readerPage.locator('.ant-select-disabled[data-testid="trip-tracking-model"]')),
    ).toHaveCount(1);
    // It does not say the cave has no survey uploaded, which would send them to upload one.
    await expect(readerPage.getByTestId('trip-tracking-no-survey-uploaded')).toHaveCount(0);
    // Writing to the log opened no drawing and no place.
    await expect(readerPage.getByTestId('trip-tracking-positions-withheld')).toBeVisible();
    await expect(
      readerPage
        .getByTestId('trip-tracking-participants')
        .getByTestId('trip-tracking-position-withheld'),
    ).toHaveCount(2);
    await expect(
      readerPage.getByTestId('trip-tracking-events').getByTestId('trip-tracking-position-withheld'),
    ).toHaveCount(2, { timeout: 30_000 });
    await expect(readerPage.getByTestId('trip-tracking-model-panel')).toHaveCount(0);
    await expect(readerPage.getByTestId('trip-tracking-movie')).toHaveCount(0);
    expect(await placesIn(readerPage), 'their page once they may write to the log').toEqual({
      station: false,
      depth: false,
    });

    // ---- Nothing that browser was sent, in either state, holds a place ----
    // What has been answered is read through before leaving, since leaving discards the rest; then
    // off the trip, so that the page's own polling has stopped and every later body has arrived.
    await answered();
    await gotoRoute(readerPage, '/trip-logs');
    const everything = await answered();
    const read = everything.filter((one) => one.body !== null);
    // The search below is over the bodies that were read. An answer cut off in passing is covered
    // only by another read of the same address; one that is not would make "nothing" a claim about
    // part of what was sent.
    expect(
      neverSearched(everything),
      'answers that browser was sent and whose body was never read',
    ).toEqual([]);
    // The check is only worth something over the answers the tab is drawn from: both were received
    // and read, in a browser that was told they succeeded.
    const watchReads = read.filter((one) => one.path === watchPath && one.status === 200);
    const logReads = read.filter(
      (one) => one.path.startsWith(`${watchPath}/events`) && one.status === 200,
    );
    expect(watchReads.length, 'reads of the watch that browser received').toBeGreaterThan(0);
    expect(logReads.length, 'reads of the log that browser received').toBeGreaterThan(0);
    const carrying = (needle: string) =>
      read.filter((one) => one.body!.includes(needle)).map((one) => one.path);
    // The search finds what is there: the people of the trip are named in what that browser read.
    expect(carrying(ana).length, 'answers naming somebody on the trip').toBeGreaterThan(0);
    for (const name of stations) {
      expect(carrying(name), `answers carrying the station ${name}`).toEqual([]);
    }
    expect(carrying(String(REPORTED_DEPTH)), 'answers carrying the reported depth').toEqual([]);
    expect(carrying(model), 'answers naming the survey').toEqual([]);
    // And the only thing it was refused, in either state, is the list of declared places — refused
    // as a conflict, to the coordinator, which is the one refusal declared above.
    const refused = everything.filter((one) => one.status >= 400);
    expect([...new Set(refused.map((one) => `${one.status} ${one.path}`))]).toEqual([
      `409 ${watchPath}/places`,
    ]);
  } finally {
    await theirs.context.close();
    // Off the trip before its rows go, so the page's own polling never reads a deleted trip and
    // files the refusal with the console guard.
    try {
      await gotoRoute(page, '/trip-logs');
    } catch {
      // The failure that brought the flow here is the one to report.
    }
    if (tripId) {
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
