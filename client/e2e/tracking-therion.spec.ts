// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, localDay, tripBody, tryAsPerson, versionOf } from './arrange.ts';
import { chooseOption, gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A party followed on a survey compiled by Therion is drawn on it, each person where they were
 * reported, and a station pressed in that drawing can be reported from.
 *
 * <b>Why this stands here.</b> Every other browser flow about a watch uses the one Survex model
 * among the fixtures, so the whole Therion path — the server reading a `.lox` into stations, the
 * viewer parsing the same file into its own tree, and the two agreeing on what each station is
 * called — was exercised by nothing that runs in a browser. A report that "a party is never drawn
 * on a Therion model" was once taken in, examined by hand and found to describe nothing that
 * happens; this is that examination, kept, so the next such report is answered by a run rather
 * than by an afternoon.
 *
 * <b>The viewer itself is asked, not the page around it.</b> The list beside the model names each
 * person and their station whether or not the viewer could place them, so it cannot settle
 * whether anybody was drawn. The viewer keeps its own list of markers and says of each whether
 * the loaded model holds the station it names — a marker it could not resolve is kept and drawn
 * nowhere. That list is what is read here, from the viewer the page built: its constructor is
 * counted as the vendored script defines it, before the application first asks for it, and
 * nothing about the viewer or the page is changed by the counting.
 *
 * The survey is a published one: a branch of the demonstration model the viewer's own project
 * distributes, already among the server's fixtures and credited in the repository's notice. It is
 * read from there rather than copied beside the other browser fixtures, because two copies of a
 * binary file are two files the day one of them is replaced. The people are invented, and so is
 * where the cave is put: the file is in plain metres and says nothing of where on Earth it lies.
 *
 * <b>What this does not show.</b> Two things about names, both for want of a published file that
 * has them. This file's outermost survey has no name, so the server stores each station under
 * the very letters the viewer has it under, and the conversion between the two spellings — made
 * when the outermost survey is named — is the identity here: a fault in it would leave this green.
 * And every station in this file has a name, so nothing here loads a station the file left
 * nameless into the viewer and reports at it. Both are held by the server's own tests, against
 * rows, and not by anything that runs the viewer.
 */
const here = path.dirname(fileURLToPath(import.meta.url));
const THERION_MODEL = path.join(
  here, '..', '..', 'server', 'tests', 'SilexGis.Api.Tests', 'Fixtures', 'Cheddar-Whitebeam.lox',
);

interface Station {
  name: string;
  altitudeM: number;
  isEntrance: boolean;
}

interface Report {
  caverId: string;
  kind: string;
  stationName: string | null;
}

/** One marker as the viewer answers for it, cut down to what can cross out of the page. */
interface Drawn {
  at: string;
  resolved: boolean;
}

/**
 * Keeps every viewer the page builds where a test can ask it questions.
 *
 * The vendored script publishes one global and the application constructs its viewer from it. The
 * global is given a face here that hands out the same constructor counted: each viewer built is
 * the real one, built by the real constructor with the application's own arguments, and is also
 * remembered. Installed before any script of the page runs, so the application never sees the
 * uncounted one. The script first publishes an empty object and fills it afterwards, which is why
 * the face is made when the global is read rather than when it is written.
 */
async function rememberViewers(page: Page) {
  await page.addInitScript(() => {
    const built: unknown[] = [];
    let published: Record<string, unknown> | undefined;
    let face: unknown;
    let counted: unknown;
    Object.defineProperty(window, '__e2eCaveViewers', { value: built });
    Object.defineProperty(window, 'CV2', {
      configurable: true,
      get() {
        if (published === undefined || typeof published.CaveViewer !== 'function') {
          return published;
        }
        face ??= new Proxy(published, {
          get(target, key) {
            const value: unknown = Reflect.get(target, key);
            if (key !== 'CaveViewer' || typeof value !== 'function') {
              return value;
            }
            counted ??= new Proxy(value as new (...args: unknown[]) => object, {
              construct(constructor, args: unknown[]) {
                const viewer = Reflect.construct(constructor, args) as object;
                built.push(viewer);
                return viewer;
              },
            });
            return counted;
          },
        });
        return face;
      },
      set(value: Record<string, unknown> | undefined) {
        published = value;
        face = undefined;
        counted = undefined;
      },
    });
  });
}

/**
 * The markers the newest viewer of the page is holding, by the id each was added under — which
 * for a tracked party is the person's — or null while the page has built no viewer.
 *
 * The newest, because a panel is free to build its viewer more than once on the way to showing it
 * and only the last is the one on the screen.
 */
function drawnMarkers(page: Page): Promise<Record<string, Drawn> | null> {
  return page.evaluate(() => {
    const viewers = (
      window as unknown as {
        __e2eCaveViewers: {
          getLiveMarkers(): { id: string; ref: string | readonly string[]; resolved: boolean }[];
        }[];
      }
    ).__e2eCaveViewers;
    const viewer = viewers.at(-1);
    if (viewer === undefined) {
      return null;
    }
    const answer: Record<string, { at: string; resolved: boolean }> = {};
    for (const marker of viewer.getLiveMarkers()) {
      answer[marker.id] = {
        at: typeof marker.ref === 'string' ? marker.ref : marker.ref.join('.'),
        resolved: marker.resolved,
      };
    }
    return answer;
  });
}

/** Turns the newest viewer's camera so that a station stands in the middle of the drawing. */
function aimAt(page: Page, station: string): Promise<boolean> {
  return page.evaluate(
    (name) =>
      (
        window as unknown as {
          __e2eCaveViewers: {
            focusStation(ref: string, options: { highlight: boolean }): Promise<unknown>;
          }[];
        }
      ).__e2eCaveViewers
        .at(-1)!
        .focusStation(name, { highlight: false })
        .then(() => true),
    station,
  );
}

test('a party followed on a Therion model is drawn where it was reported, and a pressed station is reported from', async ({
  page,
}) => {
  // The model is drawn in software and the survey is read by a background job: slow, not stuck.
  test.setTimeout(360_000);
  const stamp = Date.now();
  const ana = `E2E Therion Ana ${stamp}`;
  const bujor = `E2E Therion Bujor ${stamp}`;
  const carmen = `E2E Therion Carmen ${stamp}`;

  await rememberViewers(page);
  await login(page);
  let caveId: string | undefined;
  let modelId: string | undefined;
  let tripId: string | undefined;

  try {
    // ---- A cave of this run's own, with the published Therion model on it ----
    const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
    const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
      name: `E2E Therion Cave ${stamp}`,
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
          name: 'Cheddar-Whitebeam.lox',
          mimeType: 'application/octet-stream',
          buffer: readFileSync(THERION_MODEL),
        },
        // Where the file's zero point is, since a survey in plain metres cannot say. Invented.
        originLongitude: '25.2',
        originLatitude: '45.5',
        originHeightM: '1200',
      },
    });
    expect(upload.status(), await upload.text()).toBe(201);
    const uploaded = (await upload.json()) as { id: string; format: string };
    const model = uploaded.id;
    modelId = model;
    // A Therion model and nothing else: the whole point of this flow is which reader read it.
    expect(uploaded.format).toBe('lox');
    await expect
      .poll(
        async () =>
          (await asPerson<{ status: string }>(page, 'GET', `/api/v1/survey-models/${model}`)).status,
        { timeout: 120_000, message: 'the uploaded Therion model was never read' },
      )
      .toBe('ready');

    // ---- Three stations of it, chosen from what the server read rather than written down ----
    // The highest, the lowest and one between them by height: three different places whatever the
    // file holds, and none of them a name this spec would have to be edited for.
    const stations = (
      await asPerson<{ items: Station[] }>(
        page,
        'GET',
        `/api/v1/survey-models/${model}/stations?page=1&pageSize=500`,
      )
    ).items
      .filter((station) => !station.isEntrance)
      .sort((a, b) => b.altitudeM - a.altitudeM || a.name.localeCompare(b.name));
    expect(stations.length, 'the model was read into too few stations to tell three apart').toBeGreaterThan(10);
    const high = stations[0];
    const low = stations[stations.length - 1];
    const between = stations[Math.floor(stations.length / 2)];
    expect(new Set([high.name, low.name, between.name]).size).toBe(3);
    expect(high.altitudeM).toBeGreaterThan(low.altitudeM);

    // ---- A trip of three, its watch started on that model, two of them reported at stations ----
    const person = (name: string) => ({
      caverId: null, newCaverName: name, roleId: null, entryTime: null, exitTime: null, note: null,
    });
    const trip = await asPerson<{ id: string; participants: { caverId: string; name: string }[] }>(
      page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(`E2E Therion watch ${stamp}`, localDay(0), {
        caveIds: [cave.id],
        participants: [person(ana), person(bujor), person(carmen)],
      }),
    );
    tripId = trip.id;
    const idOf = (name: string) => trip.participants.find((p) => p.name === name)!.caverId;

    const watchPath = `/api/v1/trip-logs/${trip.id}/tracking`;
    const setWatch = async (body: Record<string, unknown>) =>
      asPerson(page, 'PUT', watchPath, body, { 'If-Match': await versionOf(page, watchPath) });
    await setWatch({ state: 'armed', surveyModelId: model, referenceStationName: null, depthFilter: [] });

    const report = (name: string, kind: string, stationName: string | null) =>
      asPerson(page, 'POST', `${watchPath}/events`, {
        caverIds: [idOf(name)], kind, stationName, depthM: null, teamId: null, note: null,
        recordedAt: null,
      });
    await report(ana, 'entered', null);
    await report(bujor, 'entered', null);
    await report(ana, 'atStation', high.name);
    await report(bujor, 'atStation', low.name);

    const logOf = async () =>
      (await asPerson<{ items: Report[] }>(page, 'GET', `${watchPath}/events?page=1&pageSize=200`))
        .items;
    const placeOf = async (name: string) =>
      (await logOf()).find((row) => row.caverId === idOf(name) && row.kind === 'atStation')
        ?.stationName;
    // What the log keeps is what the viewer will be asked to draw. This file's outermost survey
    // has no name, so the server's spelling of a station and the viewer's are the same letters.
    expect(await placeOf(ana)).toBe(high.name);
    expect(await placeOf(bujor)).toBe(low.name);

    // ---- The trip's Tracking tab, with the model on the screen ----
    await gotoRoute(page, `/trip-logs/${trip.id}`);
    await page.getByRole('tab', { name: 'Tracking' }).click();
    await page.getByTestId('trip-tracking-model-toggle').click();
    const panel = page.getByTestId('trip-tracking-model-panel');
    const canvas = panel.locator('canvas').first();
    await expect(canvas).toBeVisible({ timeout: 90_000 });

    // ---- Both drawn, each where they were reported ----
    // The viewer's own answer: a marker for each of the two, naming the station that was reported
    // for that person, and resolved — the loaded model holds that station, so the marker is on
    // it. Nobody else has a marker: the third person was reported nowhere. Had the party not been
    // drawn, this is where it would show: as no markers, or as markers the model could not place.
    await expect
      .poll(() => drawnMarkers(page), {
        timeout: 120_000,
        message: 'the viewer never held a placed marker for each of the two people reported',
      })
      .toEqual({
        [idOf(ana)]: { at: high.name, resolved: true },
        [idOf(bujor)]: { at: low.name, resolved: true },
      });
    // And the page says the same thing the viewer does: nobody is flagged as missing from the
    // drawing. Read after the viewer's answer, which is what makes an absent flag mean something.
    await expect(page.locator('[data-testid^="trip-tracking-position-not-on-model-"]')).toHaveCount(0);
    await expect(page.getByTestId('caveview-position-not-on-model')).toHaveCount(0);

    // ---- A station pressed in the model is reported from ----
    // The camera is turned so that a station nobody is at stands in the middle of the drawing,
    // and the middle is pressed with the mouse: the press, and everything it sets off, is the
    // real one. Which station answers a press is the viewer's to say — another may lie on the
    // same line of sight, nearer — so the station reported from is the one the page then names,
    // whichever it is, and it has to be one of the model's own.
    const offer = page.getByTestId('trip-tracking-picked-station');
    await expect(async () => {
      expect(await aimAt(page, between.name)).toBe(true);
      const box = (await canvas.boundingBox())!;
      await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
      await expect(offer).toBeVisible({ timeout: 5_000 });
    }).toPass({ timeout: 90_000 });

    await page.getByTestId('trip-tracking-record-here-open').click();
    const dialog = page.getByRole('dialog', { name: 'Record a report' });
    await expect(dialog).toBeVisible();
    const pressedName = dialog.getByTestId('trip-tracking-dialog-station-name');
    await expect(pressedName).not.toBeEmpty();
    const pressed = ((await pressedName.textContent()) ?? '').trim();
    expect(stations.map((station) => station.name)).toContain(pressed);

    await chooseOption(page, dialog.getByTestId('trip-tracking-dialog-cavers'), carmen);
    // The list of people stays open over the dialog after a choice; a press on the station's name
    // puts it away without choosing or dismissing anything.
    await pressedName.click();
    await dialog.getByRole('button', { name: 'Record for 1 selected' }).click();
    await expect(dialog).toBeHidden({ timeout: 30_000 });

    // The server took the viewer's spelling of the pressed station as one of the model's, and the
    // log has the third person there.
    await expect.poll(() => placeOf(carmen), { timeout: 30_000 }).toBe(pressed);
    // And the viewer now holds three placed markers, the new one at the station that was pressed.
    await expect
      .poll(() => drawnMarkers(page), {
        timeout: 60_000,
        message: 'the person reported from the pressed station was never drawn there',
      })
      .toEqual({
        [idOf(ana)]: { at: high.name, resolved: true },
        [idOf(bujor)]: { at: low.name, resolved: true },
        [idOf(carmen)]: { at: pressed, resolved: true },
      });
    await expect(page.getByTestId('caveview-position-not-on-model')).toHaveCount(0);

    // ---- Taken down again ----
    // Off the page before its rows go, so its own polling never reads a deleted trip. The watch is
    // closed first: a model a running watch stands on is not deleted from under it.
    await gotoRoute(page, '/trip-logs');
    await setWatch({ state: 'closed' });
    await asPerson(page, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
    tripId = undefined;
    await asPerson(page, 'DELETE', `/api/v1/survey-models/${model}`);
    modelId = undefined;
    await asPerson(page, 'DELETE', `/api/v1/caves/${cave.id}`);
    caveId = undefined;
  } finally {
    // Only what a failure left behind; each removal above clears its own id.
    if (tripId !== undefined || modelId !== undefined || caveId !== undefined) {
      try {
        await gotoRoute(page, '/trip-logs');
      } catch {
        // The failure that brought the flow here is the one to report.
      }
    }
    if (tripId !== undefined) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    if (modelId !== undefined) {
      await tryAsPerson(page, 'DELETE', `/api/v1/survey-models/${modelId}`);
    }
    if (caveId !== undefined) {
      await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
    }
  }
});
