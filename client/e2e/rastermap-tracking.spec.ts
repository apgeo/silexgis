// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { apiJson, bearerToken, relationId, uploadMapPng } from './rastermapApi.ts';

/**
 * The party on a scanned map sheet, inside the coordinator's tracking tab.
 *
 * A real armed watch is stood up through the API — a cave, a survey model, a declared map
 * with two pins, a trip with two cavers, entries and station reports — and the browser
 * then proves the two halves of the owner's honesty rule on the live pair:
 *
 * - a caver whose reported station has a point on this sheet IS drawn there, proven by a
 *   press landing on their dot and opening their card;
 * - a caver whose station has no point is absent WITH the message — "No point on this
 *   map", the sheet's own fourth state — never guessed onto the drawing.
 *
 * The replay is scrubbed on the same sheet: at the armed moment nobody is drawn, at the
 * end of the log the party is back — the sheet reads the very fold the 3D pane reads.
 */

/** The pinned station Ana reports from: her dot must appear at this point of the sheet. */
const PIN_A = { station: 'p8.p8.98', x: 0.3, y: 0.4 };
/** A pinned station nobody stands at: pressing it must offer to record there. */
const PIN_B = { station: 'p8.p8.97', x: 0.7, y: 0.6 };
/** A real station of the survey with NO point on the map: Bogdan's absent-with-message case. */
const OFF_MAP_STATION = 'p8.bens_dig.217';

/** The uploaded map picture is square, `uploadMapPng`'s default. */
const IMAGE = 512;

/**
 * Where a stored fraction point of the sheet sits on screen, before anybody pans: the
 * view opens fitted to the whole picture with a symmetric 16px padding, so the extent's
 * centre is the box's centre and the resolution is whichever axis is tighter.
 */
function sheetPoint(
  box: { x: number; y: number; width: number; height: number },
  fx: number,
  fy: number,
) {
  const resolution = Math.max(IMAGE / (box.width - 32), IMAGE / (box.height - 32));
  // Stored fractions are top-left origin; the extent's y points up.
  const upY = IMAGE - fy * IMAGE;
  return {
    x: box.x + box.width / 2 + (fx * IMAGE - IMAGE / 2) / resolution,
    y: box.y + box.height / 2 - (upY - IMAGE / 2) / resolution,
  };
}

test('the watch draws its party on the sheet, and says who the sheet cannot place', async ({
  page,
}) => {
  const caveName = `E2E Sheet Tracking Cave ${Date.now()}`;
  const mapName = `E2E tracking sheet ${Date.now()}`;
  await login(page);

  // A cave of this run's own, with the committed survey fixture on it.
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

  const token = await bearerToken(page);
  const models = (await apiJson(page, token, 'GET', `/api/v1/caves/${caveId}/survey-models`)) as {
    id: string;
  }[];
  const modelId = models[0].id;

  // The map and its two pins, authored the way batch-2's flows write them.
  const mapFile = await uploadMapPng(page, token, `${mapName}.png`);
  const relationTypes = (await apiJson(page, token, 'GET', '/api/v1/reslinks/relation-types')) as {
    id: number;
    code: string;
  }[];
  await apiJson(page, token, 'POST', '/api/v1/reslinks', {
    relationTypeId: relationId(relationTypes, 'map-plan-of'),
    description: null,
    members: [
      { targetType: 'document', targetId: mapFile.documentId, isMain: true, sortOrder: 0,
        note: null, anchorKind: 'whole', anchor: null, anchorFileId: null },
      { targetType: 'surveyModel', targetId: modelId, isMain: false, sortOrder: 1,
        note: null, anchorKind: 'whole', anchor: null, anchorFileId: null },
    ],
  });
  for (const pin of [PIN_A, PIN_B]) {
    await apiJson(page, token, 'POST', '/api/v1/reslinks', {
      relationTypeId: relationId(relationTypes, 'map-station-point'),
      description: null,
      members: [
        { targetType: 'document', targetId: mapFile.documentId, isMain: true, sortOrder: 0,
          note: null, anchorKind: 'imageRegion',
          anchor: { shape: 'point', x: pin.x, y: pin.y }, anchorFileId: mapFile.id },
        { targetType: 'surveyModel', targetId: modelId, isMain: false, sortOrder: 1,
          note: null, anchorKind: 'modelStation', anchor: { station: pin.station },
          anchorFileId: null },
      ],
    });
  }

  // A trip with two invented cavers, armed on the model, with the party underground:
  // Ana reported at the pinned station, Bogdan at a real station the sheet has no point
  // for. Stood up through the API because what this spec is for is the browser.
  const participant = (name: string) => ({
    caverId: null, newCaverName: name, roleId: null, entryTime: null, exitTime: null, note: null,
  });
  const trip = (await apiJson(page, token, 'POST', '/api/v1/trip-logs', {
    title: `E2E sheet tracking ${Date.now()}`,
    tripTypeId: null,
    tripDate: new Date().toISOString().slice(0, 10),
    tripDateEnd: null, entryTime: null, exitTime: null,
    description: null, results: null, weatherConditions: null, locationText: null,
    organizingCavingGroupId: null, geom: null,
    caveIds: [caveId],
    participants: [participant('E2E Ana'), participant('E2E Bogdan')],
    proposers: null, cavingGroupId: null, visibility: null,
    depthReachedM: null, lengthSurveyedM: null, surveyStations: null, ropeMetres: null,
    hadIncident: false, fieldData: null, logistics: null, safety: null,
    maxParticipants: null, meetingGeom: null,
  })) as { id: string; participants: { caverId: string; name: string }[] };
  const ana = trip.participants.find((p) => p.name === 'E2E Ana')!.caverId;
  const bogdan = trip.participants.find((p) => p.name === 'E2E Bogdan')!.caverId;

  // Arming is an If-Match write: the watch hands out its state under an ETag and takes
  // instructions back only against the version somebody read.
  const watchRead = await page.request.fetch(`/api/v1/trip-logs/${trip.id}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(watchRead.ok()).toBeTruthy();
  const watchTag = watchRead.headers()['etag'];
  await apiJson(
    page,
    token,
    'PUT',
    `/api/v1/trip-logs/${trip.id}/tracking`,
    { state: 'armed', surveyModelId: modelId, referenceStationName: null, depthFilter: [] },
    watchTag ? { 'If-Match': watchTag } : {},
  );
  const report = (caverIds: string[], kind: string, stationName: string | null = null) =>
    apiJson(page, token, 'POST', `/api/v1/trip-logs/${trip.id}/tracking/events`, {
      caverIds, kind, stationName, depthM: null, teamId: null, note: null, recordedAt: null,
    });
  await report([ana, bogdan], 'entered');
  await report([ana], 'atStation', PIN_A.station);
  await report([bogdan], 'atStation', OFF_MAP_STATION);

  // ---- The coordinator's tab, and the sheet beside the 3D scene ----
  await gotoRoute(page, `/trip-logs/${trip.id}`);
  await page.getByRole('tab', { name: 'Tracking' }).click();
  await page.getByTestId('trip-tracking-model-toggle').click();

  await expect(page.getByRole('tab', { name: '3D' })).toBeVisible({ timeout: 30_000 });
  const mapTab = page.getByRole('tab', { name: new RegExp(mapName) });
  await expect(mapTab).toBeVisible({ timeout: 15_000 });
  await mapTab.click();

  // Scoped to the sheet pane throughout: the hidden 3D pane keeps its own overlay — the
  // strip hides panes rather than unmounting them, that is the whole bargain — so the
  // bare test ids exist twice on this page, once visibly.
  const pane = page.getByTestId('rastermap-tracking-pane');
  const sheet = pane.getByTestId('rastermap-map');
  await expect(sheet).toBeVisible({ timeout: 15_000 });
  await expect(sheet.locator('canvas').first()).toBeAttached({ timeout: 15_000 });

  // The list beside the sheet is the same product as the 3D pane's: everybody on the
  // watch, each with a reason where no marker could be drawn. Bogdan's reason is the
  // sheet's own fourth state, worded apart from the drawing/withheld/unreported three.
  const overlay = pane.getByTestId('caveview-tracking');
  await expect(overlay).toBeVisible({ timeout: 15_000 });
  await expect(overlay.getByTestId(`caveview-caver-${bogdan}`)).toContainText(
    'No point on this map',
  );
  await expect(overlay.getByTestId('caveview-position-not-on-map')).toBeVisible();
  await expect(overlay.getByTestId(`caveview-caver-${ana}`)).not.toContainText(
    'No point on this map',
  );

  // …and at length on his card, so the remedy is said where there is room to say it.
  await overlay.getByTestId(`caveview-caver-${bogdan}`).click();
  await expect(pane.getByTestId('caveview-caver-card-not-on-map')).toContainText(
    'this map has no point defined',
  );
  await overlay.getByTestId(`caveview-caver-${bogdan}`).click();

  // ---- Ana's marker is really on the sheet, where the pin says ----
  //
  // Proven physically: a press at the pin's own screen point answers the person. The
  // click tolerance is a handful of pixels, so nothing but a dot drawn at that point
  // could open this card. The press is element-relative rather than page-absolute:
  // content above the strip (the offer, the replay bar, polled rows) comes and goes and
  // each arrival slides the sheet, so a page point measured a frame ago can land on
  // whatever moved in — Playwright's own click re-measures the element at click time
  // and waits for its box to hold still first. Only the box's SIZE is read here, for
  // the fit math, and the size does not change when the sheet merely slides.
  const press = async (fx: number, fy: number) => {
    const box = (await sheet.boundingBox())!;
    const at = sheetPoint({ ...box, x: 0, y: 0 }, fx, fy);
    await sheet.click({ position: { x: at.x, y: at.y } });
  };
  await press(PIN_A.x, PIN_A.y);
  await expect(pane.getByTestId('caveview-caver-card')).toContainText('E2E Ana');
  await pane.getByTestId('caveview-caver-card-close').click();

  // A record of the sheet as it stands: desk width, then the 360px phone binding.
  if (process.env.RASTERMAP_SHOTS) {
    await page.waitForTimeout(400);
    await page.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/30-sheet-party-desktop.png` });
  }

  // ---- A pinned station with nobody at it offers the very record-here a 3D press does ----
  await press(PIN_B.x, PIN_B.y);
  const offer = page.getByTestId('trip-tracking-picked-station');
  await expect(offer).toBeVisible();
  await expect(offer).toContainText(/97/);
  await page.getByTestId('trip-tracking-record-here-open').click();
  const recordDialog = page.getByRole('dialog', { name: 'Record a report' });
  await expect(recordDialog).toBeVisible();
  await expect(page.getByTestId('trip-tracking-dialog-station-name')).toContainText('97');
  await page.keyboard.press('Escape');
  await expect(recordDialog).not.toBeVisible();
  // And the standing offer is put away, so the sheet gets its screen room back.
  await offer.locator('.ant-alert-close-icon').click();
  await expect(offer).not.toBeVisible();

  // ---- The replay drives the sheet: the same fold, at the scrubbed moment ----
  await page.getByTestId('trip-tracking-replay-open').click();
  await expect(page.getByTestId('trip-tracking-model-panel').getByRole('slider')).toBeVisible();
  // A replay opens at the armed moment, before anybody had said anything: the sheet
  // draws nobody, and a press where Ana's dot stood answers nothing.
  await press(PIN_A.x, PIN_A.y);
  await expect(pane.getByTestId('caveview-caver-card')).toHaveCount(0);
  await expect(overlay.getByTestId('caveview-position-not-on-map')).toHaveCount(0);

  // Wound to the end of the log, the party is back — Ana's dot at her pin, Bogdan's
  // message back on his row — through the same fold the 3D replay consumes.
  await page.getByTestId('trip-tracking-model-panel').getByRole('slider').press('End');
  await press(PIN_A.x, PIN_A.y);
  await expect(pane.getByTestId('caveview-caver-card')).toContainText('E2E Ana');
  await pane.getByTestId('caveview-caver-card-close').click();
  await expect(overlay.getByTestId('caveview-position-not-on-map')).toBeVisible();

  if (process.env.RASTERMAP_SHOTS) {
    await page.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/31-sheet-replay-desktop.png` });
    await page.setViewportSize({ width: 360, height: 780 });
    await expect(sheet.locator('canvas').first()).toBeAttached();
    await sheet.scrollIntoViewIfNeeded();
    await page.waitForTimeout(600);
    await page.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/32-sheet-party-360.png` });
    await page.setViewportSize({ width: 1280, height: 720 });
  }
  await page.getByTestId('trip-tracking-replay-leave').click();

  // Off the page before its rows go, so the page's own polling never reads a deleted
  // trip and files a 404 with the console guard.
  await gotoRoute(page, '/trip-logs');

  // Take the run's own rows down: the trip, then the model, then the cave.
  await apiJson(page, token, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
  await apiJson(page, token, 'DELETE', `/api/v1/survey-models/${modelId}`);
  await apiJson(page, token, 'DELETE', `/api/v1/caves/${caveId}`);
});
