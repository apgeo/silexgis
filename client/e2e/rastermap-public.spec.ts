// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { CHOICE_KEY } from '../src/i18n/languageStorage.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { apiJson, bearerToken, relationId, uploadMapPng } from './rastermapApi.ts';

/**
 * The map sheets on the published page — the one surface of this feature a stranger reads.
 *
 * A real publishable trip is stood up through the signed-in context — a cave, the committed
 * survey fixture, a declared map with two pins, a trip with two cavers reported underground —
 * and a follow link is really minted. The page is then read the way a follower reads it: a
 * <b>fresh browser context with no storage at all</b>, holding nothing but the link, because
 * the whole claim under test is that the envelope alone carries the sheets. At the end the
 * link is revoked and the same context watches the page close.
 *
 * The signed-in half runs in the suite's own context and is swept by the console guard as
 * usual. The anonymous context is created by this spec and sits outside that sweep, so its
 * errors are collected here and asserted empty — stricter than the guard's report mode, and
 * the only honest option: a surface read by strangers has no person watching its console.
 */

/** The pinned station Ana reports from: her dot must appear at this point of the sheet. */
const PIN_A = { station: 'p8.p8.98', x: 0.3, y: 0.4 };
/** A second pinned station nobody stands at — drawn as a plain point, pressable by nobody. */
const PIN_B = { station: 'p8.p8.97', x: 0.7, y: 0.6 };
/** A real station of the survey with NO point on the map: the absent-with-message case. */
const OFF_MAP_STATION = 'p8.bens_dig.217';

/** The uploaded map picture is square, `uploadMapPng`'s default. */
const IMAGE = 512;

/**
 * Where a stored fraction point of the sheet sits on screen, before anybody pans: the view
 * opens fitted to the whole picture with a symmetric 16px padding. The same math as the
 * tracking spec's, because it is the same component drawing the same fit.
 */
function sheetPoint(
  box: { width: number; height: number },
  fx: number,
  fy: number,
) {
  const resolution = Math.max(IMAGE / (box.width - 32), IMAGE / (box.height - 32));
  const upY = IMAGE - fy * IMAGE;
  return {
    x: box.width / 2 + (fx * IMAGE - IMAGE / 2) / resolution,
    y: box.height / 2 - (upY - IMAGE / 2) / resolution,
  };
}

test('a follower sees the party on the sheet, and a revoked link closes it', async ({
  page,
  browser,
}) => {
  const caveName = `E2E Public Sheet Cave ${Date.now()}`;
  const mapName = `E2E public sheet ${Date.now()}`;
  await login(page);

  // ---- The fixture, through the signed-in context: a cave, its survey, a mapped sheet ----
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

  // ---- A tracked trip with the party underground, published for real ----
  const participant = (name: string) => ({
    caverId: null, newCaverName: name, roleId: null, entryTime: null, exitTime: null, note: null,
  });
  const trip = (await apiJson(page, token, 'POST', '/api/v1/trip-logs', {
    title: `E2E public sheet ${Date.now()}`,
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

  const share = (await apiJson(
    page,
    token,
    'POST',
    `/api/v1/trip-logs/${trip.id}/tracking/shares`,
  )) as { id: string; token: string };

  // ---- The follower: a fresh context holding nothing but the link ----
  const anonymous = await browser.newContext();
  try {
    // The suite's context fixture seeds English the same way; a manual context repeats it or
    // fails on the login form's language, which this page does not even have.
    await anonymous.addInitScript(
      ({ key, language }: { key: string; language: string }) => {
        try {
          window.localStorage.setItem(key, language);
        } catch {
          // Blocked storage falls back to Romanian, and the assertions below would say so.
        }
      },
      { key: CHOICE_KEY, language: 'en' },
    );
    const pub = await anonymous.newPage();
    const consoleErrors: string[] = [];
    pub.on('pageerror', (error) => consoleErrors.push(String(error)));
    pub.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });

    await pub.goto(`/shared/trips/${share.token}`);
    await expect(pub.getByTestId('public-trip-title')).toBeVisible({ timeout: 30_000 });

    // The strip beside the 3D drawing names the sheet; opening it is what fetches it.
    const mapTab = pub.getByRole('tab', { name: new RegExp(mapName) });
    await expect(mapTab).toBeVisible({ timeout: 15_000 });
    await mapTab.click();

    const pane = pub.getByTestId('public-sheet-pane');
    const sheet = pane.getByTestId('rastermap-map');
    await expect(sheet).toBeVisible({ timeout: 15_000 });
    await expect(sheet.locator('canvas').first()).toBeAttached({ timeout: 15_000 });

    // The list beside the sheet: everybody, each absence with its reason. Bogdan's is the
    // sheet's own fourth state, and it must name him — a follower reads names, not ordinals,
    // on an installation that publishes them.
    const overlay = pane.getByTestId('caveview-tracking');
    await expect(overlay).toBeVisible({ timeout: 15_000 });
    const bogdanRow = overlay.locator('[data-testid^="caveview-caver-"]', {
      hasText: 'E2E Bogdan',
    });
    await expect(bogdanRow).toContainText('No point on this map');
    const anaRow = overlay.locator('[data-testid^="caveview-caver-"]', { hasText: 'E2E Ana' });
    await expect(anaRow).not.toContainText('No point on this map');

    // Ana's dot is really on the sheet, at the pin's own point: a press there, and only
    // there, opens her card.
    const box = (await sheet.boundingBox())!;
    const at = sheetPoint(box, PIN_A.x, PIN_A.y);
    await sheet.click({ position: at });
    await expect(pane.getByTestId('caveview-caver-card')).toContainText('E2E Ana');

    if (process.env.RASTERMAP_SHOTS) {
      await pub.waitForTimeout(400);
      await pub.screenshot({
        path: `${process.env.RASTERMAP_SHOTS}/40-public-sheet-desktop.png`,
      });
    }
    await pane.getByTestId('caveview-caver-card-close').click();

    // The embed carries the same strip inside its frame, fed by the same envelope.
    await pub.goto(`/shared/trips/${share.token}/embed`);
    const embedTab = pub.getByRole('tab', { name: new RegExp(mapName) });
    await expect(embedTab).toBeVisible({ timeout: 30_000 });
    await embedTab.click();
    await expect(
      pub.getByTestId('public-sheet-pane').getByTestId('rastermap-map').locator('canvas').first(),
    ).toBeAttached({ timeout: 15_000 });
    if (process.env.RASTERMAP_SHOTS) {
      await pub.waitForTimeout(400);
      await pub.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/41-public-sheet-embed.png` });
    }

    // The anonymous surface ran clean to here — asserted before the revocation because this
    // context sits outside the suite guard's sweep, and asserted at all because a surface
    // read by strangers has no person watching its console.
    expect(consoleErrors).toEqual([]);

    // ---- Revoked, the same link answers what an invented one answers ----
    await apiJson(
      page,
      token,
      'DELETE',
      `/api/v1/trip-logs/${trip.id}/tracking/shares/${share.id}`,
    );
    await pub.goto(`/shared/trips/${share.token}`);
    await expect(pub.getByTestId('public-trip-not-found')).toBeVisible({ timeout: 15_000 });

    // The refusal itself is the one thing the browser is expected to log: Chromium prints a
    // console line for every 404 resource, and the revoked envelope read IS a 404 — that is
    // the page working. Anything else recorded on this page is a defect.
    expect(
      consoleErrors.filter((line) => !/the server responded with a status of 404/.test(line)),
    ).toEqual([]);
  } finally {
    await anonymous.close();
  }

  // Off the page before its rows go, so the signed-in page's polling never reads a deleted
  // trip and files a 404 with the console guard.
  await gotoRoute(page, '/trip-logs');
});
