// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Locator, type Page } from '@playwright/test';
import { surveyRead, tryAsPerson } from './arrange.ts';
import { coveredControls } from './caveviewLabelSizeFlow.ts';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { tintedPng } from './png.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';

/**
 * A photograph hung on a moment of a trip, in the movie made of that trip.
 *
 * <b>Why this is a browser flow.</b> When a photograph comes up, for how many frames and how
 * strongly is arithmetic, and it has a suite of its own. What that suite cannot do is draw one: it
 * hands the drawing a canvas that records calls. Whether the photograph the server hands over is
 * decoded, is in hand when its frame is drawn, and ends up in the pixels of the file the reader
 * saves is decided by a real browser, a real image and the real encoder — and a movie that left
 * every photograph out would still be a valid file of the right length.
 *
 * So the photograph here is one flat colour the model is never drawn in, shown over the whole
 * frame without fading, and the saved file is opened again and read frame by frame: the colour is
 * in the middle of exactly as many frames as the photograph was given, one after another, and in
 * none before its moment.
 */

const fixture = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'P8_Master.3d');
const STATION = 'p8.p8.98';
const FURTHER = 'p8.p8.97';
/** A colour nothing in the viewer draws with. */
const TINT = [255, 0, 255] as const;

/** The caves this file made, removed again when the test that made them ends — passed or failed. */
const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

/** A number box of the settings form, whichever element the test id hangs on. */
function numberBox(dialog: Locator, testId: string): Locator {
  return dialog.locator(`input[data-testid="${testId}"], [data-testid="${testId}"] input`).first();
}

/** Types a number into one of the form's boxes and leaves the box, which is when it is taken. */
async function typeNumber(page: Page, dialog: Locator, testId: string, value: number) {
  const box = numberBox(dialog, testId);
  await box.focus();
  await page.keyboard.press('ControlOrMeta+A');
  await page.keyboard.type(String(value));
  await page.keyboard.press('Tab');
  await expect(box).not.toBeFocused();
  await expect(box).toHaveAttribute('aria-valuenow', String(value));
}

/** Opens one of the settings form's folded groups by its heading. */
async function openGroup(dialog: Locator, heading: string) {
  const header = dialog.locator('.ant-collapse-header').filter({ hasText: heading });
  await header.click();
  await expect(header).toHaveAttribute('aria-expanded', 'true');
}

/** Picks an option of one of the form's drop-downs by its whole text. */
async function choose(page: Page, dialog: Locator, testId: string, text: string) {
  await dialog.getByTestId(testId).click();
  const exactly = new RegExp(`^${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`);
  // The list slides open, and a click made while it is still sliding is lost on a machine that
  // draws the model in software: the list stays open with the old value.
  const dropdown = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)').last();
  await expect(dropdown).toBeVisible();
  await expect(dropdown).not.toHaveClass(/ant-slide-(up|down)-(appear|enter)/, { timeout: 15_000 });
  await dropdown.locator('.ant-select-item-option-content').filter({ hasText: exactly }).first().click();
  await expect(dialog.getByTestId(testId)).toContainText(text);
}

/**
 * The colour in the middle of every frame of a GIF, read by the browser that will play it. The
 * middle is inside a photograph shown over the whole frame whatever its shape.
 */
async function middleOfEachFrame(page: Page, gif: Buffer): Promise<[number, number, number][]> {
  return page.evaluate(async (base64: string) => {
    const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
    const decoder = new ImageDecoder({ data: bytes, type: 'image/gif' });
    await Promise.race([
      Promise.all([decoder.tracks.ready, decoder.completed]),
      new Promise((_, reject) => setTimeout(() => reject(new Error('the GIF was never fully decoded')), 30_000)),
    ]);
    const track = decoder.tracks.selectedTrack!;
    const middles: [number, number, number][] = [];
    for (let index = 0; index < track.frameCount; index++) {
      const { image } = await decoder.decode({ frameIndex: index });
      const canvas = new OffscreenCanvas(image.displayWidth, image.displayHeight);
      const context = canvas.getContext('2d')!;
      context.drawImage(image, 0, 0);
      const [red, green, blue] = context.getImageData(
        Math.floor(image.displayWidth / 2),
        Math.floor(image.displayHeight / 2),
        1,
        1,
      ).data;
      middles.push([red, green, blue]);
      image.close();
    }
    decoder.close();
    return middles;
  }, gif.toString('base64'));
}

test('a photograph hung on a moment of a trip is in the movie of it, for as long as it was given and not before', async ({
  page,
}) => {
  test.setTimeout(420_000);
  const stamp = Date.now();
  await login(page);

  // ---- A cave, its survey, and a finished trip reported on it ----
  await page.goto('/caves/new');
  const caveName = `E2E Movie Pictures Cave ${stamp}`;
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())![1];
  madeCaves.push(caveId);

  const token = await bearerToken(page);
  const survey = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${token}` },
    multipart: {
      file: { name: 'Movie pictures.3d', mimeType: 'application/octet-stream', buffer: readFileSync(fixture) },
    },
  });
  expect(survey.status(), await survey.text()).toBe(201);
  const modelId = ((await apiJson(page, token, 'GET', `/api/v1/caves/${caveId}/survey-models`)) as { id: string }[])[0].id;
  // Every report below names a station, and the survey's stations are stored after the upload
  // answers: named before then, a station is refused as none of the survey's.
  await surveyRead(page, modelId);

  const trip = (await apiJson(page, token, 'POST', '/api/v1/trip-logs', {
    title: `E2E the day of the photograph ${stamp}`,
    tripTypeId: null,
    tripDate: new Date().toISOString().slice(0, 10),
    tripDateEnd: null, entryTime: null, exitTime: null,
    description: null, results: null, weatherConditions: null, locationText: null,
    organizingCavingGroupId: null, geom: null,
    caveIds: [caveId],
    participants: [
      { caverId: null, newCaverName: `E2E Photographed ${stamp}`, roleId: null, entryTime: null, exitTime: null, note: null },
    ],
    proposers: null, cavingGroupId: null, visibility: null,
    depthReachedM: null, lengthSurveyedM: null, surveyStations: null, ropeMetres: null,
    hadIncident: false, fieldData: null, logistics: null, safety: null,
    maxParticipants: null, meetingGeom: null,
  })) as { id: string; participants: { caverId: string }[] };
  const person = trip.participants[0].caverId;

  const setWatch = async (body: Record<string, unknown>) => {
    const read = await page.request.fetch(`/api/v1/trip-logs/${trip.id}/tracking`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(read.ok()).toBeTruthy();
    const etag = read.headers()['etag'];
    return apiJson(page, token, 'PUT', `/api/v1/trip-logs/${trip.id}/tracking`, body, etag ? { 'If-Match': etag } : {});
  };
  const hoursAgo = (hours: number) => new Date(Date.now() - hours * 3_600_000).toISOString();
  const report = async (kind: string, stationName: string | null, recordedAt: string) =>
    (
      (await apiJson(page, token, 'POST', `/api/v1/trip-logs/${trip.id}/tracking/events`, {
        caverIds: [person], kind, stationName, depthM: null, teamId: null, note: null, recordedAt,
      })) as { id: string }[]
    )[0];
  await setWatch({ state: 'armed', surveyModelId: modelId, referenceStationName: null, depthFilter: [] });
  // Eight hours underground, and the photograph two hours into them: well inside the movie, so
  // its showing is cut short by neither end.
  await report('entered', null, hoursAgo(9));
  const photographed = await report('atStation', STATION, hoursAgo(7));
  await report('atStation', FURTHER, hoursAgo(4));
  await report('exited', null, hoursAgo(1));

  // ---- The photograph: filed on the trip, then hung on the moment of one of its reports ----
  const upload = await page.request.post('/api/v1/files?allowDuplicate=false', {
    headers: { Authorization: `Bearer ${token}` },
    multipart: {
      file: { name: `e2e-movie-photo-${stamp}.png`, mimeType: 'image/png', buffer: tintedPng(96, TINT) },
    },
  });
  expect(upload.ok(), `upload answered ${upload.status()}`).toBeTruthy();
  const photo = (await upload.json()) as { id: string; documentId: string };
  await apiJson(page, token, 'POST', '/api/v1/attachments', {
    fileId: photo.id,
    entityType: 'tripLog',
    entityId: trip.id,
    role: 'photoInterior',
    caption: null,
    sortOrder: 0,
  });
  // The gallery lists it once it has been read as a picture; the dialog asks once, on opening.
  await expect
    .poll(
      async () =>
        (
          (await apiJson(page, token, 'GET', `/api/v1/photos?tripLogId=${trip.id}&pageSize=60`)) as {
            items: { documentId: string }[];
          }
        ).items.some((item) => item.documentId === photo.documentId),
      { timeout: 30_000 },
    )
    .toBe(true);

  await page.goto(`/trip-logs/${trip.id}?tab=tracking`);
  await page.waitForURL((url) => url.pathname === `/trip-logs/${trip.id}`, { timeout: 60_000 });
  await expect(page.getByTestId('trip-tracking-events')).toBeVisible({ timeout: 30_000 });
  await page.getByTestId(`trip-tracking-event-picture-${photographed.id}`).click();
  const hanging = page.getByRole('dialog', { name: 'Photographs of this moment' });
  await expect(hanging).toBeVisible();
  await hanging.getByTestId(`trip-tracking-pictures-pick-${photo.documentId}`).check();
  await hanging.getByRole('button', { name: 'Attach photographs (1)' }).click();
  await expect(page.getByText('Attached: 1. Not attached: 0.')).toBeVisible({ timeout: 15_000 });
  await expect(hanging).toBeHidden();
  await setWatch({ state: 'closed' });

  // ---- The movie dialog, from the trip's own model ----
  await page.reload();
  await expect(page.getByTestId('trip-tracking-events')).toBeVisible({ timeout: 60_000 });
  await page.getByTestId('trip-tracking-model-toggle').click();
  const panel = page.getByTestId('trip-tracking-model-panel');
  await expect(panel.locator('canvas').first()).toBeVisible({ timeout: 60_000 });
  // The trip's own model is a narrower surface than the dialog a cave opens one in, with the same
  // controls on it: each of them is still the topmost thing at its own middle here.
  await expect(panel.getByTestId('caveview-label-scale')).toBeVisible({ timeout: 60_000 });
  expect(await coveredControls(page)).toEqual([]);
  await page.getByTestId('trip-tracking-movie').click();
  const dialog = page.getByRole('dialog').filter({ has: page.getByTestId('movie-preview') });
  await expect(dialog).toBeVisible({ timeout: 30_000 });
  await expect(dialog.getByTestId('movie-preview')).toHaveAttribute('data-status', 'ready', { timeout: 90_000 });

  // The smallest frame, ten frames a second, four seconds and no hold at the end: forty frames.
  await expect(dialog.getByTestId('movie-format-gif')).toBeChecked();
  await choose(page, dialog, 'movie-size', '320 × 180');
  await choose(page, dialog, 'movie-fps', '10');
  await openGroup(dialog, 'Captions');
  await typeNumber(page, dialog, 'movie-hold', 0);
  await typeNumber(page, dialog, 'movie-duration', 4);
  await expect(dialog.getByTestId('movie-summary')).toContainText('40 frames');

  // ---- Photographs are off until asked for, and the form then says how many there are ----
  await openGroup(dialog, 'Photographs');
  await expect(dialog.getByTestId('movie-picture-mode')).toContainText('Not shown');
  await expect(dialog.getByTestId('movie-picture-count')).toHaveCount(0);
  await choose(page, dialog, 'movie-picture-mode', 'Over the whole frame');
  // Counted and loaded: the sentence names a photograph still on its way, or one that could not
  // be had, in other words than these.
  await expect(dialog.getByTestId('movie-picture-count')).toHaveText(
    "Photographs on these trips' moments: 1.",
    { timeout: 60_000 },
  );
  // One second each and no fading: ten frames of the photograph as it is, and none half drawn.
  await typeNumber(page, dialog, 'movie-picture-seconds', 1);
  const fade = dialog.getByTestId('movie-picture-fade');
  await expect(fade).toBeChecked();
  await fade.click();
  await expect(fade).not.toBeChecked();

  // ---- Made, saved, and read again frame by frame ----
  await expect(dialog.getByTestId('movie-export')).toBeEnabled({ timeout: 60_000 });
  const download = page.waitForEvent('download', { timeout: 240_000 });
  await dialog.getByTestId('movie-export').click();
  const file = await download;
  const gif = await readFile(await file.path());
  await expect(dialog.getByTestId('movie-progress')).toBeHidden({ timeout: 30_000 });
  await expect(dialog.getByTestId('movie-export-failed')).toHaveCount(0);
  expect(gif.subarray(0, 6).toString('latin1')).toBe('GIF89a');

  const middles = await middleOfEachFrame(page, gif);
  expect(middles).toHaveLength(40);
  // The file holds a palette, so the colour comes back near what went in rather than exactly it.
  const tinted = middles.map(([red, green, blue]) => red > 170 && green < 110 && blue > 170);
  const shownOn = tinted.flatMap((is, index) => (is ? [index] : []));
  expect(shownOn, 'the photograph is on as many frames as it was given').toHaveLength(10);
  expect(shownOn[9] - shownOn[0], 'and on frames that follow one another').toBe(9);
  // Not from the start: the party went in two hours before it was taken.
  expect(shownOn[0]).toBeGreaterThan(2);
  // And not to the end: the movie goes on after it, drawn as it was before.
  expect(shownOn[9]).toBeLessThan(39);
});
