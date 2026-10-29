// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHash } from 'node:crypto';
import { expect, type Locator, type Page } from '@playwright/test';
import { asPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { uniquePng } from './png.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A phone's map of a cave, in the browser. The map and its pins are sent the way a phone sends them
 * — through the sync upload, with the picture staged first — and the browser then opens the map
 * from the cave's files, moves a pin, removes one and adds one; every write is checked on the
 * rasters stream the phones read, because a canvas can only show that something drew. A second case
 * declares the same picture as a survey's plan and finds the pinned place drawn on the survey's map
 * tab.
 *
 * The picture is a generated noise PNG and the places are invented names: real maps and real cave
 * registers never enter test data.
 */

const PICTURE = 512;

/** The place colour a pinned place is drawn in, as red, green and blue. */
const PLACE_COLOUR = [0x9a, 0x34, 0x12];

interface PhoneMap {
  caveId: string;
  setId: string;
  mapId: string;
  places: { id: string; name: string }[];
  pinA: string;
  pinB: string;
}

/**
 * A cave with three places and a phone's map of it carrying two pins: the first place at the
 * picture's centre, the second near a corner. Uploaded as a phone uploads, so the pins are the
 * links a phone's upload writes.
 */
async function arrangePhoneMap(page: Page, stamp: string, caveId?: string): Promise<PhoneMap> {
  let cave = caveId;
  if (cave === undefined) {
    const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
    cave = (
      await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
        name: `E2E Phone Map Cave ${stamp}`,
        description: null,
        caveTypeId: caveTypes[0].id,
        visibility: 'authenticated',
        locationProtected: false,
        explorationStatus: 'unknown',
        isShowCave: false,
      })
    ).id;
  }
  const set = await asPerson<{ id: string }>(page, 'POST', '/api/v1/sync/sets/', {
    name: `E2E phone map ${stamp}`.slice(0, 30),
    uploadVisibility: 'private',
    rootFeatureIds: [cave],
    settings: {},
  });

  const places = ['Sala Mare', 'Puțul', 'Galeria Nouă'].map((name) => ({
    id: crypto.randomUUID(),
    name: `${name} ${stamp}`,
  }));
  await asPerson(page, 'POST', `/api/v1/sync/sets/${set.id}/upload`, {
    batchId: crypto.randomUUID(),
    contractVersion: 1,
    rows: places.map((place) => ({
      id: place.id,
      kind: 'generic',
      parentId: cave,
      baseRevision: null,
      deleted: false,
      name: place.name,
      featureTypeCode: 'cave_place',
      isMain: false,
    })),
  });

  // The picture staged first, as a phone stages it: opened, sent, completed.
  const bytes = uniquePng(PICTURE);
  const staged = await asPerson<{ id: string }>(page, 'POST', '/api/v1/files/uploads', {
    fileName: 'map.png',
    sizeBytes: bytes.length,
    stageForSync: true,
  });
  const token = await bearerToken(page);
  const sent = await page.request.put(`/api/v1/files/uploads/${staged.id}?offset=0`, {
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/octet-stream' },
    data: bytes,
  });
  expect(sent.ok(), `staging answered ${sent.status()}`).toBeTruthy();
  await asPerson(page, 'POST', `/api/v1/files/uploads/${staged.id}/complete`);

  const mapId = crypto.randomUUID();
  const pinA = crypto.randomUUID();
  const pinB = crypto.randomUUID();
  const answer = await asPerson<{ rasterMaps: { status: string }[]; rasterPins: { status: string }[] }>(
    page,
    'POST',
    `/api/v1/sync/sets/${set.id}/upload`,
    {
      batchId: crypto.randomUUID(),
      contractVersion: 1,
      rasterMaps: [
        {
          id: mapId,
          caveId: cave,
          title: `E2E phone map ${stamp}`,
          mapType: 'planView',
          fileName: 'map.png',
          sha256: createHash('sha256').update(bytes).digest('hex'),
          sizeBytes: bytes.length,
          stagedUploadId: staged.id,
        },
      ],
      rasterPins: [
        { id: pinA, rasterMapId: mapId, placeId: places[0].id, x: PICTURE / 2, y: PICTURE / 2 },
        { id: pinB, rasterMapId: mapId, placeId: places[1].id, x: 40, y: 40 },
      ],
    },
  );
  expect(answer.rasterMaps.map((row) => row.status)).toEqual(['created']);
  expect(answer.rasterPins.map((row) => row.status)).toEqual(['created', 'created']);
  return { caveId: cave, setId: set.id, mapId, places, pinA, pinB };
}

interface StreamPin {
  id: string;
  placeId: string;
  x: number;
  y: number;
}

/** What the phones are given of the map: its pins, and the pins that went. */
async function streamOf(page: Page, phone: PhoneMap) {
  const page_ = await asPerson<{
    maps: { id: string; pins: StreamPin[] }[];
    pinTombstones: { id: string }[];
  }>(page, 'GET', `/api/v1/sync/sets/${phone.setId}/rasters`);
  return {
    pins: page_.maps.find((map) => map.id === phone.mapId)?.pins ?? [],
    gone: page_.pinTombstones.map((tombstone) => tombstone.id),
  };
}

/** Clicks the sheet at an offset from its centre, in the pane's own pixels. */
async function clickSheet(map: Locator, dx = 0, dy = 0) {
  const box = await map.boundingBox();
  expect(box, 'the map has no box').toBeTruthy();
  await map.click({ position: { x: box!.width / 2 + dx, y: box!.height / 2 + dy } });
}

/** Waits for one write the screen sends while acting. */
async function wrote(page: Page, method: string, path: RegExp, act: () => Promise<void>) {
  const response = page.waitForResponse(
    (r) => r.request().method() === method && path.test(new URL(r.url()).pathname),
    { timeout: 15_000 },
  );
  await act();
  const answered = await response;
  expect(answered.ok(), `${method} answered ${answered.status()}`).toBeTruthy();
}

/** The colour drawn at the centre of a map pane, read off its canvases, topmost first. */
async function colourAtCentre(map: Locator): Promise<number[] | null> {
  return map.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const canvases = Array.from(element.querySelectorAll('canvas')).reverse();
    for (const canvas of canvases) {
      const frame = canvas.getBoundingClientRect();
      const context = canvas.getContext('2d', { willReadFrequently: true });
      if (context === null || frame.width === 0) {
        continue;
      }
      const x = Math.round((box.left + box.width / 2 - frame.left) * (canvas.width / frame.width));
      const y = Math.round((box.top + box.height / 2 - frame.top) * (canvas.height / frame.height));
      const [r, g, b, a] = context.getImageData(x, y, 1, 1).data;
      if (a > 0) {
        return [r, g, b];
      }
    }
    return null;
  });
}

function isPlaceColour(colour: number[] | null): boolean {
  return colour !== null && colour.every((channel, at) => Math.abs(channel - PLACE_COLOUR[at]) <= 12);
}

test("a phone's map opens as a map, and its places are moved, removed and added there", async ({ page }) => {
  test.setTimeout(180_000);
  const stamp = String(Date.now());
  await login(page);
  const phone = await arrangePhoneMap(page, stamp);
  const [placeA, , placeC] = phone.places;

  // From the cave's files: the map's picture offers to open as a map.
  await gotoRoute(page, `/caves/${phone.caveId}`);
  await page.getByTestId('placemap-open').first().click();
  const modal = page.getByTestId('placemap-modal');
  const map = modal.getByTestId('placemap-map');
  await expect(map.locator('canvas').first()).toBeAttached({ timeout: 15_000 });
  await expect(modal.getByTestId('placemap-earlier')).toHaveCount(0);
  await expect(modal.getByTestId('placemap-offpicture')).toHaveCount(0);

  // The place pinned at the centre is a marker there, named for its place.
  await clickSheet(map);
  const pressed = page.getByTestId('placemap-marker');
  await expect(pressed).toContainText(placeA.name, { timeout: 10_000 });

  // Moved: the phones are given the same pin, at a new pixel.
  await pressed.getByTestId('placemap-move').click();
  await expect(modal.getByTestId('placemap-hint')).toContainText(placeA.name);
  await wrote(page, 'PUT', /\/place-point$/, () => clickSheet(map, 80, 50));
  await expect
    .poll(async () => (await streamOf(page, phone)).pins.find((pin) => pin.id === phone.pinA))
    .toMatchObject({ id: phone.pinA, placeId: placeA.id });
  const moved = (await streamOf(page, phone)).pins.find((pin) => pin.id === phone.pinA)!;
  expect(moved.x).toBeGreaterThan(PICTURE / 2);
  expect(moved.y).toBeGreaterThan(PICTURE / 2);

  // Removed from where it now stands: the phones are told it went.
  await clickSheet(map, 80, 50);
  await expect(pressed).toContainText(placeA.name, { timeout: 10_000 });
  await pressed.getByTestId('placemap-delete').click();
  await wrote(page, 'DELETE', new RegExp(`/reslinks/${phone.pinA}$`), () =>
    page.locator('.ant-modal-confirm').getByRole('button', { name: 'Delete' }).click(),
  );
  const afterRemoval = await streamOf(page, phone);
  expect(afterRemoval.gone).toContain(phone.pinA);
  expect(afterRemoval.pins.map((pin) => pin.id)).not.toContain(phone.pinA);

  // Added: a click on the map, then the place it is. The phones are given it as any other pin.
  await modal.getByTestId('placemap-add').click();
  await clickSheet(map, -60, 70);
  const pick = page.getByTestId('placemap-pick');
  await pick.getByTestId('reslink-target-picker-input').fill(placeC.name);
  await pick.getByTestId('reslink-target-picker-row').filter({ hasText: placeC.name }).first().click();
  await wrote(page, 'POST', /\/reslinks\/place-points$/, () => pick.getByTestId('placemap-save').click());
  const added = (await streamOf(page, phone)).pins.find((pin) => pin.placeId === placeC.id);
  expect(added, 'the added place is not given to the phones').toBeTruthy();
  expect(added!.x).toBeLessThan(PICTURE / 2);
  expect(added!.y).toBeGreaterThan(PICTURE / 2);
  expect((await streamOf(page, phone)).pins.map((pin) => pin.id)).toContain(phone.pinB);
});

test("the places a phone pinned on a survey's declared map are drawn on its map tab", async ({ page }) => {
  test.setTimeout(240_000);
  const stamp = String(Date.now());
  const caveName = `E2E Phone Survey Cave ${stamp}`;
  await login(page);

  // A cave with the committed survey fixture on it, made as a person makes it.
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

  // The phone's map of that cave, its picture then declared the survey's plan.
  const phone = await arrangePhoneMap(page, stamp, caveId!);
  const models = await asPerson<{ id: string }[]>(page, 'GET', `/api/v1/caves/${caveId}/survey-models`);
  // The picture is the pin's main member: the map's own document.
  const pinLink = await asPerson<{ members: { targetId: string; isMain: boolean }[] }>(
    page,
    'GET',
    `/api/v1/reslinks/${phone.pinB}`,
  );
  const documentId = pinLink.members.find((member) => member.isMain)!.targetId;
  const relationTypes = await asPerson<{ id: number; code: string }[]>(page, 'GET', '/api/v1/reslinks/relation-types');
  await asPerson(page, 'POST', '/api/v1/reslinks', {
    relationTypeId: relationTypes.find((row) => row.code === 'map-plan-of')!.id,
    description: null,
    members: [
      { targetType: 'document', targetId: documentId, isMain: true, sortOrder: 0, note: null, anchorKind: 'whole', anchor: null, anchorFileId: null },
      { targetType: 'surveyModel', targetId: models[0].id, isMain: false, sortOrder: 1, note: null, anchorKind: 'whole', anchor: null, anchorFileId: null },
    ],
  });

  // The survey's map tab draws the place pinned at the picture's centre, in the place colour.
  await gotoRoute(page, `/caves/${caveId}`);
  await page.getByRole('button', { name: /View in 3D/ }).click();
  const viewer = page.getByRole('dialog').filter({ has: page.getByTestId('caveview-container') });
  await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
  await viewer.getByRole('tab', { name: new RegExp(`E2E phone map ${stamp}`) }).click();
  const map = viewer.getByTestId('rastermap-map');
  await expect(map.locator('canvas').first()).toBeAttached({ timeout: 15_000 });
  await expect.poll(async () => isPlaceColour(await colourAtCentre(map)), { timeout: 15_000 }).toBe(true);
});
