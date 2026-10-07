// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile } from 'node:fs/promises';
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';
import { settledPicture } from './settled.ts';

/**
 * A movie of a survey model and the trips tracked on it, made in the browser and saved as a file.
 *
 * <b>Everything the movie is made of is real.</b> The cave, its survey (the committed fixture) and
 * two trips are stood up through the API, each trip's watch is armed on that survey, and each
 * party is reported at stations of it hours apart, so the movie has somewhere to move them. One
 * caver on each trip has a Romanian name, because the viewer draws the labels and an accented
 * letter it cannot draw is the defect a reader would see first.
 *
 * <b>The file is judged by the browser's own decoders, not by this project's encoder.</b> A GIF's
 * frames are counted by `ImageDecoder`, and a WebM is handed to a `<video>` element, which has to
 * find its length in the file. A check that read the file back with the code that wrote it would
 * pass whatever that code got wrong.
 *
 * <b>Tiny on purpose.</b> This browser draws WebGL in software, so every frame is a real render
 * that takes real time; four seconds at the smallest size is enough to prove the path end to end.
 * The one long export here is started only to be asked about and called off.
 *
 * <b>The pointer is left on the preview and the form is worked from the keyboard.</b> The viewer
 * takes keys from the whole document while the pointer rests on it, which is exactly where a
 * reader's pointer is while they look at what they are about to export.
 */

/** Stations of the committed survey fixture the parties are reported at. */
const STATION_A = 'p8.p8.98';
const STATION_B = 'p8.bens_dig.217';
const STATION_C = 'p8.p8.97';

const participant = (name: string) => ({
  caverId: null,
  newCaverName: name,
  roleId: null,
  entryTime: null,
  exitTime: null,
  note: null,
});

async function makeTrip(page: Page, token: string, caveId: string, title: string, names: string[]) {
  return (await apiJson(page, token, 'POST', '/api/v1/trip-logs', {
    title,
    tripTypeId: null,
    tripDate: new Date().toISOString().slice(0, 10),
    tripDateEnd: null,
    entryTime: null,
    exitTime: null,
    description: null,
    results: null,
    weatherConditions: null,
    locationText: null,
    organizingCavingGroupId: null,
    geom: null,
    caveIds: [caveId],
    participants: names.map(participant),
    proposers: null,
    cavingGroupId: null,
    visibility: null,
    depthReachedM: null,
    lengthSurveyedM: null,
    surveyStations: null,
    ropeMetres: null,
    hadIncident: false,
    fieldData: null,
    logistics: null,
    safety: null,
    maxParticipants: null,
    meetingGeom: null,
  })) as { id: string; participants: { caverId: string; name: string }[] };
}

/** Moves a trip's watch between states, honouring the read's etag. */
async function setWatch(page: Page, token: string, tripId: string, body: Record<string, unknown>) {
  const read = await page.request.fetch(`/api/v1/trip-logs/${tripId}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(read.ok()).toBeTruthy();
  const etag = read.headers()['etag'];
  return apiJson(page, token, 'PUT', `/api/v1/trip-logs/${tripId}/tracking`, body, etag ? { 'If-Match': etag } : {});
}

/** A number box of the settings form, whichever element antd hangs the test id on. */
function numberBox(dialog: Locator, testId: string): Locator {
  return dialog.locator(`input[data-testid="${testId}"], [data-testid="${testId}"] input`).first();
}

/**
 * Types a number into one of the form's boxes, key by key and without touching the mouse — so the
 * pointer stays wherever the test left it, and the digits are real key presses that anything
 * listening on the page gets its chance to take.
 */
async function typeNumber(page: Page, dialog: Locator, testId: string, value: number) {
  const box = numberBox(dialog, testId);
  await box.focus();
  await page.keyboard.press('ControlOrMeta+A');
  await page.keyboard.type(String(value));
  await page.keyboard.press('Tab');
  await expect(box).not.toBeFocused();
  // Read as a number: a box stepping by halves shows 0 as "0.0".
  await expect(box).toHaveAttribute('aria-valuenow', String(value));
}

/** Opens one of the settings form's folded groups by its heading. */
async function openGroup(dialog: Locator, heading: string) {
  const header = dialog.locator('.ant-collapse-header').filter({ hasText: heading });
  await header.click();
  await expect(header).toHaveAttribute('aria-expanded', 'true');
}

/** The file name the dialog says the movie will be saved under. */
async function shownFileName(dialog: Locator): Promise<string> {
  const line = (await dialog.getByTestId('movie-file-name').textContent()) ?? '';
  return /silexgis-[^\s]+\.(gif|webm|mp4)/.exec(line)?.[0] ?? '';
}

/**
 * Picks an option of one of the form's drop-downs by its whole text — "10" frames a second must not
 * find "1024 × 768" in a list of sizes still fading out.
 */
async function choose(page: Page, dialog: Locator, testId: string, text: string) {
  await dialog.getByTestId(testId).click();
  const exactly = new RegExp(`^${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`);
  // The list slides open, and on this browser — drawing the model in software on a busy machine —
  // a click made while it is still sliding is lost: the list stayed open with the old value. So the
  // list is given until its opening motion has ended before its option is pressed.
  const dropdown = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)').last();
  await expect(dropdown).toBeVisible();
  await expect(dropdown).not.toHaveClass(/ant-slide-(up|down)-(appear|enter)/, { timeout: 15_000 });
  await dropdown.locator('.ant-select-item-option-content').filter({ hasText: exactly }).first().click();
  await expect(dialog.getByTestId(testId)).toContainText(text);
}

/** Exports the movie as it is set up, and returns the downloaded file's name and bytes. */
async function exportMovie(page: Page, dialog: Locator) {
  const download = page.waitForEvent('download', { timeout: 240_000 });
  await dialog.getByTestId('movie-export').click();
  // Progress is reported while the frames are drawn.
  await expect(dialog.getByTestId('movie-progress')).toBeVisible({ timeout: 30_000 });
  const file = await download;
  const path = await file.path();
  const bytes = await readFile(path);
  await expect(dialog.getByTestId('movie-progress')).toBeHidden({ timeout: 30_000 });
  return { name: file.suggestedFilename(), bytes };
}

/**
 * The tracking panel's own picture, as the page shows it: taken before and after a movie is made,
 * to prove the movie was drawn by a viewer of its own and left this one — camera, markers — alone.
 */
async function panelPicture(page: Page, options: { stillFor?: number } = {}): Promise<string> {
  const canvas = page.getByTestId('trip-tracking-model-panel').locator('canvas').first();
  return (await settledPicture(canvas, options)).toString('base64');
}

/** The share of pixels two same-sized PNGs differ at by more than a whisper, counted in the page. */
async function differingShare(page: Page, before: string, after: string): Promise<number> {
  return page.evaluate(
    async ([a, b]) => {
      const pixels = async (base64: string) => {
        const bitmap = await createImageBitmap(await (await fetch(`data:image/png;base64,${base64}`)).blob());
        const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
        const context = canvas.getContext('2d')!;
        context.drawImage(bitmap, 0, 0);
        return context.getImageData(0, 0, bitmap.width, bitmap.height);
      };
      const [one, two] = await Promise.all([pixels(a), pixels(b)]);
      if (one.width !== two.width || one.height !== two.height) {
        return 1;
      }
      let differing = 0;
      for (let i = 0; i < one.data.length; i += 4) {
        const delta = Math.max(
          Math.abs(one.data[i] - two.data[i]),
          Math.abs(one.data[i + 1] - two.data[i + 1]),
          Math.abs(one.data[i + 2] - two.data[i + 2]),
        );
        if (delta > 8) {
          differing++;
        }
      }
      return differing / (one.width * one.height);
    },
    [before, after] as const,
  );
}

test('a movie of two tracked trips is made from a trip, judged by the browser, and offered from the cave', async ({
  page,
}) => {
  test.setTimeout(480_000);
  const stamp = Date.now();
  const caveName = `E2E Movie Cave ${stamp}`;
  await login(page);

  // ---- The cave and its survey ----
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId).toBeTruthy();

  await page.getByRole('button', { name: 'Upload model' }).click();
  const uploadModal = page.getByRole('dialog');
  await expect(uploadModal.locator('.ant-upload-drag')).toBeVisible({ timeout: 15_000 });
  const chooser = page.waitForEvent('filechooser');
  await uploadModal.locator('.ant-upload-drag').click();
  await (await chooser).setFiles('e2e/fixtures/P8_Master.3d');
  await uploadModal.getByRole('button', { name: 'Upload model' }).click();
  await expect(page.getByText('Survex .3d')).toBeVisible({ timeout: 30_000 });

  const auth = await bearerToken(page);
  const models = (await apiJson(page, auth, 'GET', `/api/v1/caves/${caveId}/survey-models`)) as { id: string }[];
  const modelId = models[0].id;

  // ---- Two trips, each tracked on the survey and reported hours apart ----
  const hoursAgo = (hours: number) => new Date(Date.now() - hours * 3_600_000).toISOString();
  const report = (
    tripId: string,
    caverIds: string[],
    kind: string,
    stationName: string | null,
    teamId: string | null,
    recordedAt: string,
  ) =>
    apiJson(page, auth, 'POST', `/api/v1/trip-logs/${tripId}/tracking/events`, {
      caverIds,
      kind,
      stationName,
      depthM: null,
      teamId,
      note: null,
      recordedAt,
    });
  const armOn = (tripId: string) =>
    setWatch(page, auth, tripId, {
      state: 'armed',
      surveyModelId: modelId,
      referenceStationName: null,
      depthFilter: [],
    });

  const firstTitle = `E2E the downstream push ${stamp}`;
  const first = await makeTrip(page, auth, caveId!, firstTitle, ['Ștefan Mărgărit', 'E2E Ileana']);
  const stefan = first.participants.find((row) => row.name === 'Ștefan Mărgărit')!.caverId;
  const ileana = first.participants.find((row) => row.name === 'E2E Ileana')!.caverId;
  await armOn(first.id);
  const survey = (await apiJson(page, auth, 'POST', `/api/v1/trip-logs/${first.id}/tracking/teams`, {
    title: 'Survey',
  })) as { id: string };
  await report(first.id, [stefan, ileana], 'entered', null, null, hoursAgo(8));
  await report(first.id, [stefan], 'atStation', STATION_A, survey.id, hoursAgo(6));
  await report(first.id, [ileana], 'atStation', STATION_B, survey.id, hoursAgo(5));
  await report(first.id, [stefan], 'atStation', STATION_C, survey.id, hoursAgo(3));
  await report(first.id, [stefan, ileana], 'exited', null, null, hoursAgo(1));
  await setWatch(page, auth, first.id, { state: 'closed' });

  const secondTitle = `E2E the dig ${stamp}`;
  const second = await makeTrip(page, auth, caveId!, secondTitle, ['Țicu Pătrașcu']);
  const ticu = second.participants[0].caverId;
  await armOn(second.id);
  await report(second.id, [ticu], 'entered', null, null, hoursAgo(7));
  await report(second.id, [ticu], 'atStation', STATION_B, null, hoursAgo(6.5));
  await report(second.id, [ticu], 'atStation', STATION_A, null, hoursAgo(4));
  await report(second.id, [ticu], 'exited', null, null, hoursAgo(2));
  await setWatch(page, auth, second.id, { state: 'closed' });

  // ---- The first trip's Tracking tab, with its model on screen ----
  await gotoRoute(page, `/trip-logs/${first.id}`);
  await page.getByRole('tab', { name: 'Tracking' }).click();
  await page.getByTestId('trip-tracking-model-toggle').click();
  const panelCanvas = page.getByTestId('trip-tracking-model-panel').locator('canvas').first();
  await expect(panelCanvas).toBeVisible({ timeout: 60_000 });
  // Taken once the panel's markers have settled where the replay puts them. The model is drawn
  // in software and arrives in its own time, so two pictures alike are not yet the scene at rest:
  // it has to have stood still for a while. Everything after is compared with this one.
  const panelBefore = await panelPicture(page, { stillFor: 2_000 });

  // ---- The movie dialog, opened from the panel with this trip ticked ----
  await page.getByTestId('trip-tracking-movie').click();
  const dialog = page.getByRole('dialog').filter({ has: page.getByTestId('movie-preview') });
  await expect(dialog).toBeVisible({ timeout: 30_000 });
  await expect(dialog.getByTestId('movie-preview')).toHaveAttribute('data-status', 'ready', { timeout: 90_000 });
  await expect(dialog.getByTestId('movie-privacy')).toBeVisible();
  // The viewer draws in the box of the frame's shape — not at the fixed size its own stylesheet
  // gives every container, which would make the preview a cropped picture of something else.
  const drawnIn = await dialog.locator('.movie-preview-surface').evaluate((surface) => ({
    surface: [surface.clientWidth, surface.clientHeight],
    box: [surface.parentElement!.clientWidth, surface.parentElement!.clientHeight],
  }));
  expect(drawnIn.surface).toEqual(drawnIn.box);

  // Every file the page hands over from here on is counted: an export that was called off must
  // not have saved anything.
  let downloads = 0;
  page.on('download', () => downloads++);

  const firstRow = dialog.getByTestId(`movie-trip-${first.id}`);
  const secondRow = dialog.getByTestId(`movie-trip-${second.id}`);
  await expect(firstRow.getByRole('checkbox')).toBeChecked({ timeout: 30_000 });
  await expect(secondRow.getByRole('checkbox')).not.toBeChecked();
  await secondRow.getByRole('checkbox').check();
  await expect(secondRow.getByRole('checkbox')).toBeChecked();

  // ---- A tiny GIF: the smallest frame, ten frames a second, four seconds, no hold ----
  await expect(dialog.getByTestId('movie-format-gif')).toBeEnabled();
  await choose(page, dialog, 'movie-size', '320 × 180');
  await choose(page, dialog, 'movie-fps', '10');
  await openGroup(dialog, 'Captions');
  // ---- Keys pressed on the dialog's controls are the dialog's, wherever the pointer rests ----
  // The viewer listens for keys on the whole document for as long as the pointer is over it, and
  // has a shortcut on most letters and every digit. So the pointer is left resting on the preview
  // from here on, and everything below is typed: a digit that changed the preview's shading
  // instead of the number in the box, or a Tab that left the focus where it was, fails here.
  await dialog.getByTestId('movie-preview').hover();
  await typeNumber(page, dialog, 'movie-hold', 0);

  // ---- Escape while the frames are being drawn asks; it does not throw the export away ----
  // Asked of an export far too long to end while the question is up: three hundred frames, of
  // which only the first few are ever drawn. One short enough to be worth finishing could be over
  // before the second key, and then Escape would rightly close a dialog that had nothing running.
  await typeNumber(page, dialog, 'movie-duration', 30);
  await expect(dialog.getByTestId('movie-summary')).toContainText('300 frames');
  await expect(dialog.getByTestId('movie-export')).toBeEnabled({ timeout: 60_000 });
  await dialog.getByTestId('movie-export').click();
  await page.keyboard.press('Escape');
  const question = page.locator('.ant-modal-confirm').filter({ hasText: 'Stop making the movie?' });
  await expect(question).toBeVisible();
  // It opens on the answer that loses nothing.
  await expect(question.getByRole('button', { name: 'Keep going' })).toBeFocused();
  // Escape again answers the question, and only the question: the dialog under it stays, and the
  // export goes on being made — its bar moves on past where it stood.
  await page.keyboard.press('Escape');
  await expect(question).toBeHidden();
  await expect(dialog).toBeVisible();
  const bar = dialog.getByTestId('movie-progress').getByRole('progressbar');
  const reached = Number(await bar.getAttribute('aria-valuenow'));
  await expect
    .poll(async () => Number(await bar.getAttribute('aria-valuenow')), {
      message: 'the export did not go on after the question was answered',
      timeout: 60_000,
    })
    .toBeGreaterThan(reached);
  // The button that says what it does stops the export at once and asks nothing: no question, no
  // failure, no file.
  await dialog.getByTestId('movie-cancel').click();
  await expect(dialog.getByTestId('movie-progress')).toBeHidden({ timeout: 30_000 });
  await expect(page.locator('.ant-modal-confirm')).toHaveCount(0);
  await expect(dialog.getByTestId('movie-export-failed')).toHaveCount(0);
  await expect(dialog).toBeVisible();
  expect(downloads).toBe(0);

  await dialog.getByTestId('movie-preview').hover();
  await typeNumber(page, dialog, 'movie-duration', 4);
  await expect(dialog.getByTestId('movie-summary')).toContainText('40 frames');
  // Tab from a button, which the viewer does not leave alone by itself as it does a text box: the
  // focus moves on to the next control, the slider's handle, and back again with Shift.
  const play = dialog.getByTestId('movie-play');
  await expect(play).toBeEnabled();
  await play.focus();
  await page.keyboard.press('Tab');
  await expect(dialog.getByTestId('movie-position').getByRole('slider')).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(play).toBeFocused();
  // The X in the corner is drawn outside the dialog's body and is the dialog's all the same: with
  // the pointer still on the preview, Tab leaves it and Shift+Tab comes back to it. Left to the
  // viewer, both keys were cancelled there and the focus could not be moved off the X at all.
  const corner = dialog.locator('.ant-modal-close');
  await corner.focus();
  await expect(corner).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(corner).not.toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(corner).toBeFocused();
  // And Space presses the focused button rather than turning the model under it. The focus goes
  // back to the play button first: Space on the X would press that, and close the dialog.
  await play.focus();
  await expect(play).toBeFocused();
  await page.keyboard.press('Space');
  await expect(play).toHaveAttribute('aria-label', 'Stop the preview');
  await page.keyboard.press('Space');
  await expect(play).toHaveAttribute('aria-label', 'Play the preview');

  // ---- The name the file will be saved under is on screen before anything is made ----
  const fileName = dialog.getByTestId('movie-file-name');
  // The title caption starts on, and the title of a movie of two trips is the cave's name.
  await expect(fileName).toContainText(new RegExp(`silexgis-e2e-movie-cave-${stamp}-\\d{4}-\\d{2}-\\d{2}\\.gif`));
  // A title the reader writes names the file as it titles the picture. Typed with the pointer
  // still on the preview: letters, a space and digits all have to arrive.
  const titleText = dialog.getByTestId('movie-title-text');
  await titleText.focus();
  await page.keyboard.type('E2E Dig 42');
  await expect(titleText).toHaveValue('E2E Dig 42');
  await expect(fileName).toContainText(/silexgis-e2e-dig-42-\d{4}-\d{2}-\d{2}\.gif/);
  // With the title caption off nothing in the picture names the cave or the trip, and neither does
  // the file. The switch is a button, pressed from the keyboard like the rest.
  const titleSwitch = dialog.getByTestId('movie-caption-title');
  await titleSwitch.focus();
  await page.keyboard.press('Space');
  await expect(titleSwitch).toHaveAttribute('aria-checked', 'false');
  await expect(fileName).toContainText(/silexgis-movie-\d{4}-\d{2}-\d{2}\.gif/);
  const gifNamed = await shownFileName(dialog);
  await expect(dialog.getByTestId('movie-export')).toBeEnabled({ timeout: 60_000 });

  const gif = await exportMovie(page, dialog);
  // Saved under the name the dialog showed, which names neither the cave nor a trip.
  expect(gif.name).toMatch(/^silexgis-movie-\d{4}-\d{2}-\d{2}\.gif$/);
  expect(gif.name).toBe(gifNamed);
  expect(gif.bytes.subarray(0, 6).toString('latin1')).toBe('GIF89a');
  const decoded = await page.evaluate(async (base64: string) => {
    const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
    const decoder = new ImageDecoder({ data: bytes, type: 'image/gif' });
    await Promise.race([
      Promise.all([decoder.tracks.ready, decoder.completed]),
      new Promise((_, reject) => setTimeout(() => reject(new Error('the GIF was never fully decoded')), 30_000)),
    ]);
    const track = decoder.tracks.selectedTrack!;
    const last = await decoder.decode({ frameIndex: track.frameCount - 1 });
    const answer = {
      frames: track.frameCount,
      width: last.image.displayWidth,
      height: last.image.displayHeight,
    };
    last.image.close();
    decoder.close();
    return answer;
  }, gif.bytes.toString('base64'));
  // Every frame differs from the one before — the camera turns and the progress bar grows — so
  // none is merged into its neighbour and the browser counts exactly the frames the dialog said.
  expect(decoded).toEqual({ frames: 40, width: 320, height: 180 });

  // ---- A tiny WebM, which a <video> element has to find a length in ----
  await expect(dialog.getByTestId('movie-format-webm')).toBeEnabled({ timeout: 30_000 });
  // The test id sits on the radio's own input, which antd draws under its button face: the face
  // is what a reader presses.
  await dialog.locator('label').filter({ has: page.getByTestId('movie-format-webm') }).click();
  await expect(dialog.getByTestId('movie-format-webm')).toBeChecked();
  await expect(dialog.getByTestId('movie-size')).toContainText('320 × 180');
  // The title caption back on: the title written earlier is in the picture again, and in the name.
  await titleSwitch.click();
  await expect(titleSwitch).toHaveAttribute('aria-checked', 'true');
  await expect(fileName).toContainText(/silexgis-e2e-dig-42-\d{4}-\d{2}-\d{2}\.webm/);
  const webmNamed = await shownFileName(dialog);
  await expect(dialog.getByTestId('movie-export')).toBeEnabled({ timeout: 30_000 });
  const webm = await exportMovie(page, dialog);
  expect(webm.name).toMatch(/^silexgis-e2e-dig-42-\d{4}-\d{2}-\d{2}\.webm$/);
  expect(webm.name).toBe(webmNamed);
  // The EBML header's magic.
  expect(webm.bytes.subarray(0, 4).toString('hex')).toBe('1a45dfa3');
  const played = await page.evaluate(async (base64: string) => {
    const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: 'video/webm' }));
    const video = document.createElement('video');
    video.muted = true;
    video.preload = 'auto';
    try {
      await new Promise<void>((resolve, reject) => {
        video.onloadedmetadata = () => resolve();
        video.onerror = () => reject(new Error(`the video did not load: ${video.error?.message ?? 'no detail'}`));
        setTimeout(() => reject(new Error('the video never said how long it is')), 30_000);
        video.src = url;
      });
      return { duration: video.duration, width: video.videoWidth, height: video.videoHeight };
    } finally {
      URL.revokeObjectURL(url);
    }
  }, webm.bytes.toString('base64'));
  expect(Number.isFinite(played.duration)).toBe(true);
  expect(played.duration).toBeGreaterThan(3.5);
  expect(played.duration).toBeLessThan(4.5);
  expect({ width: played.width, height: played.height }).toEqual({ width: 320, height: 180 });

  expect(downloads).toBe(2);

  // ---- Closing the dialog leaves the panel's own viewer as it was ----
  // Closed by Escape with the pointer back on the preview — where the viewer has every key first —
  // and with nothing being exported, so there is nothing to ask about.
  await dialog.getByTestId('movie-preview').hover();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(page.locator('.ant-modal-confirm')).toHaveCount(0);
  // The panel redraws from under the dialog some frames after it has gone, so its picture is taken
  // until it is the one from before — which it never becomes if making the movie moved this
  // viewer's camera or its markers.
  await expect
    .poll(async () => differingShare(page, panelBefore, await panelPicture(page)), {
      message: "the panel's own viewer did not come back as it was",
      timeout: 30_000,
    })
    .toBeLessThan(0.002);

  // ---- The cave's survey list offers the same movie, with both trips to choose from ----
  await gotoRoute(page, `/caves/${caveId}`);
  await page.getByTestId(`survey-model-movie-${modelId}`).click();
  const fromCave = page.getByRole('dialog').filter({ has: page.getByTestId('movie-preview') });
  await expect(fromCave).toBeVisible({ timeout: 30_000 });
  await expect(fromCave.getByTestId(`movie-trip-${first.id}`)).toContainText(firstTitle, { timeout: 30_000 });
  await expect(fromCave.getByTestId(`movie-trip-${second.id}`)).toContainText(secondTitle);
  // Nothing on a cave page says which trip is meant, so none is ticked for the reader.
  await expect(fromCave.getByTestId(`movie-trip-${first.id}`).getByRole('checkbox')).not.toBeChecked();
  await expect(fromCave.getByTestId(`movie-trip-${second.id}`).getByRole('checkbox')).not.toBeChecked();
  await expect(fromCave.getByTestId('movie-export')).toBeDisabled();
  // The settings chosen for the last movie were remembered by this browser.
  await expect(fromCave.getByTestId('movie-format-webm')).toBeChecked();
  // Closed from the keyboard on the X, with the pointer resting on a preview that has its survey —
  // which is when the viewer takes every key it is left, Enter among them.
  await expect(fromCave.getByTestId('movie-preview')).toHaveAttribute('data-status', 'ready', { timeout: 90_000 });
  await fromCave.getByTestId('movie-preview').hover();
  await fromCave.locator('.ant-modal-close').focus();
  await page.keyboard.press('Enter');
  await expect(fromCave).toBeHidden();
});
