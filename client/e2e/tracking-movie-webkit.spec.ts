// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { devices, expect, type Locator, type Page } from '@playwright/test';
import { asPerson, localDay, surveyRead, tripBody } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * The movie dialog in the suite's other engine: what somebody gets who opens it in a browser
 * that is not built on Chromium.
 *
 * <b>Why this exists.</b> A movie is made by the browser, and its two video formats are written
 * by the browser's own encoder — which a browser may not have, or may have and be able to write
 * nothing with. The movie's long spec runs in Chromium, which has everything, so it cannot show
 * what the dialog does where something is missing. The promise checked here is the one a reader
 * depends on there: <b>a format this browser cannot write is said in words beside the choice,
 * never shown as a failure, and a GIF — written by the application's own code — can still be
 * made.</b>
 *
 * <b>The engine is asked, not assumed.</b> Which formats this build can write is put to the
 * engine by the application's own question, in the page, and the dialog is judged against that
 * answer: a format the engine can write must be offered, one it cannot must be disabled and
 * explained. So the spec holds on a WebKit build that encodes H.264 as on one that encodes
 * nothing, and what this build answered is written into the run's report.
 *
 * <b>Run by the suite's one WebKit project</b>, whose device is a phone. The window is made
 * desktop-sized here because the question is the engine's, and the dialog is laid out for a
 * desktop window.
 * Emulated WebKit is not Safari on a Mac or an iPhone — their builds bring the system's own
 * encoders — so this proves how the dialog says what an engine lacks, not what Safari lacks.
 */

const desktopSafari = devices['Desktop Safari'];
test.use({
  viewport: desktopSafari.viewport,
  userAgent: desktopSafari.userAgent,
  // One device pixel per CSS pixel: the model is drawn in software here, and four times the
  // pixels would be four times the wait for the same answer.
  deviceScaleFactor: 1,
  isMobile: false,
  hasTouch: false,
});

/** Stations of the committed survey fixture the party is reported at. */
const STATION_A = 'p8.p8.98';
const STATION_B = 'p8.p8.97';

/** Moves a trip's watch between states, honouring the read's etag. */
async function setWatch(page: Page, tripId: string, body: Record<string, unknown>) {
  const read = await page.request.fetch(`/api/v1/trip-logs/${tripId}/tracking`, {
    headers: { Authorization: `Bearer ${await bearerToken(page)}` },
  });
  expect(read.ok(), `the watch's read answered ${read.status()}`).toBeTruthy();
  const etag = read.headers()['etag'];
  return asPerson(page, 'PUT', `/api/v1/trip-logs/${tripId}/tracking`, body, etag ? { 'If-Match': etag } : {});
}

/** A number box of the settings form, whichever element the component hangs the test id on. */
function numberBox(dialog: Locator, testId: string): Locator {
  return dialog.locator(`input[data-testid="${testId}"], [data-testid="${testId}"] input`).first();
}

test('where the browser cannot write a video the dialog says so in words, and a GIF is still made', async ({
  page,
}) => {
  test.setTimeout(480_000);
  const stamp = Date.now();
  await login(page);

  // ---- The scene, stood up through the API: a cave, its survey, one trip tracked on it ----
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E Movie Engine Cave ${stamp}`,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
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
  const modelId = ((await upload.json()) as { id: string }).id;
  // The reports below name stations, and the survey's stations are stored after the upload answers.
  await surveyRead(page, modelId);

  const trip = await asPerson<{ id: string; participants: { caverId: string }[] }>(
    page,
    'POST',
    '/api/v1/trip-logs',
    tripBody(`E2E the movie in another engine ${stamp}`, localDay(0), {
      caveIds: [cave.id],
      participants: [
        { caverId: null, newCaverName: 'E2E Engine Ana', roleId: null, entryTime: null, exitTime: null, note: null },
      ],
    }),
  );
  const ana = trip.participants[0].caverId;
  const hoursAgo = (hours: number) => new Date(Date.now() - hours * 3_600_000).toISOString();
  const report = (kind: string, stationName: string | null, recordedAt: string) =>
    asPerson(page, 'POST', `/api/v1/trip-logs/${trip.id}/tracking/events`, {
      caverIds: [ana],
      kind,
      stationName,
      depthM: null,
      teamId: null,
      note: null,
      recordedAt,
    });
  await setWatch(page, trip.id, {
    state: 'armed',
    surveyModelId: modelId,
    referenceStationName: null,
    depthFilter: [],
  });
  await report('entered', null, hoursAgo(6));
  await report('atStation', STATION_A, hoursAgo(5));
  await report('atStation', STATION_B, hoursAgo(3));
  await report('exited', null, hoursAgo(1));
  await setWatch(page, trip.id, { state: 'closed' });

  // ---- The trip's Tracking tab, its model on screen, and the dialog opened from it ----
  await gotoRoute(page, `/trip-logs/${trip.id}`);
  await page.getByRole('tab', { name: 'Tracking' }).click();
  await page.getByTestId('trip-tracking-model-toggle').click();
  await expect(page.getByTestId('trip-tracking-model-panel').locator('canvas').first()).toBeVisible({
    timeout: 90_000,
  });
  await page.getByTestId('trip-tracking-movie').click();
  const dialog = page.getByRole('dialog').filter({ has: page.getByTestId('movie-preview') });
  await expect(dialog).toBeVisible({ timeout: 30_000 });
  await expect(dialog.getByTestId('movie-preview')).toHaveAttribute('data-status', 'ready', { timeout: 120_000 });

  // ---- The smallest frame, so the engine's answer and the dialog's are about the same size ----
  await dialog.getByTestId('movie-size').click();
  const sizes = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)').last();
  await expect(sizes).toBeVisible();
  // A press made while the list is still sliding open is lost on a browser drawing in software.
  await expect(sizes).not.toHaveClass(/ant-slide-(up|down)-(appear|enter)/, { timeout: 15_000 });
  await sizes.locator('.ant-select-item-option-content').filter({ hasText: /^320 × 180$/ }).first().click();
  await expect(dialog.getByTestId('movie-size')).toContainText('320 × 180');
  // A dialog opened for the first time in a browser opens on a GIF at ten frames a second.
  const fps = 10;
  await expect(dialog.getByTestId('movie-fps')).toContainText(String(fps));

  // ---- What this engine can write, asked with the application's own question in the page ----
  // The development server hands out the module the dialog itself asks through, so this is the
  // same question put to the same engine — not a second opinion about what it ought to answer.
  const engine = await page.evaluate<{
    videoEncoder: boolean;
    formats: { format: string; supported: boolean; codec: string | null }[];
  }>(`import('/src/caveview/movie/encode/movieEncoder.ts').then(async (m) => ({
    videoEncoder: typeof VideoEncoder !== 'undefined',
    formats: await m.probeMovieFormats({ width: 320, height: 180 }, ${fps}),
  }))`);
  const browser = page.context().browser();
  const written = engine.formats.map((entry) =>
    entry.supported
      ? `${entry.format} can be written${entry.codec ? ` (${entry.codec})` : ''}`
      : `${entry.format} cannot be written`,
  );
  const said =
    `${test.info().project.name}, ${browser?.browserType().name()} ${browser?.version()}: ` +
    `a video encoder ${engine.videoEncoder ? 'exists' : 'does not exist'}; ${written.join(', ')}`;
  // Into the report and into the run's own output: what this build answered is a finding of the
  // run, and somebody reading its log should not have to open the report to learn it.
  test.info().annotations.push({ type: 'what this engine can write', description: said });
  console.log(`[movie engine] ${said}`);

  // A GIF is the application's own work and is offered whatever the engine has.
  expect(engine.formats.find((entry) => entry.format === 'gif')?.supported).toBe(true);
  await expect(dialog.getByTestId('movie-format-gif')).toBeEnabled();
  await expect(dialog.getByTestId('movie-format-gif')).toBeChecked();

  const names: Record<string, string> = { webm: 'WebM', mp4: 'MP4' };
  for (const entry of engine.formats.filter((format) => format.format !== 'gif')) {
    const button = dialog.getByTestId(`movie-format-${entry.format}`);
    if (entry.supported) {
      // The positive case of the same rule: what the engine can write is offered, unexplained.
      await expect(button).toBeEnabled();
      continue;
    }
    // Not offered as a choice, and the reason is written out under the choices — a sentence,
    // in the secondary text of the form, naming either the format, size and rate refused or
    // the absence of any encoder.
    await expect(button).toBeDisabled();
    const reason = engine.videoEncoder
      ? new RegExp(`^This browser cannot write ${names[entry.format]} at 320 × 180, ${fps} frames a second\\.$`)
      : /^This browser has no video encoder, so only a GIF can be made here\.$/;
    await expect(dialog.getByTestId('movie-format-refusal').filter({ hasText: reason })).toHaveCount(1);
  }
  const unwritable = engine.formats.filter((entry) => !entry.supported).length;
  if (unwritable === 0) {
    await expect(dialog.getByTestId('movie-format-refusal')).toHaveCount(0);
  }
  // Said beside the choice, and nowhere as something that went wrong: the format in use is one
  // this browser writes, so there is no warning about it and no failure notice.
  await expect(dialog.getByTestId('movie-format-refused')).toHaveCount(0);
  await expect(dialog.getByTestId('movie-export-failed')).toHaveCount(0);

  // ---- And the film a person here can make is made: a short GIF, saved by the browser ----
  const duration = numberBox(dialog, 'movie-duration');
  await duration.focus();
  await page.keyboard.press('ControlOrMeta+A');
  await page.keyboard.type('4');
  await page.keyboard.press('Tab');
  await expect(duration).toHaveAttribute('aria-valuenow', '4');
  await expect(dialog.getByTestId('movie-export')).toBeEnabled({ timeout: 60_000 });
  const download = page.waitForEvent('download', { timeout: 300_000 });
  await dialog.getByTestId('movie-export').click();
  await expect(dialog.getByTestId('movie-progress')).toBeVisible({ timeout: 30_000 });
  const file = await download;
  const bytes = await readFile(await file.path());
  await expect(dialog.getByTestId('movie-progress')).toBeHidden({ timeout: 30_000 });
  expect(file.suggestedFilename()).toMatch(/^silexgis-.+\.gif$/);
  // A GIF by its own signature and with more in it than a header — not merely a file so named.
  expect(bytes.subarray(0, 6).toString('latin1')).toBe('GIF89a');
  expect(bytes.length).toBeGreaterThan(1_000);
  await expect(dialog.getByTestId('movie-export-failed')).toHaveCount(0);
  await expect(dialog).toBeVisible();
});
