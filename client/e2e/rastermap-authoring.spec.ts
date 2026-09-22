// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import {
  bearerToken,
  expectPinCount,
  pointMemberOf,
  uploadMapPng,
  uploadMapVersion,
} from './rastermapApi.ts';

/**
 * The whole authoring path, driven through the UI against the real server: declare a map
 * from the survey viewer, arm a station by name, click the sheet to place its pin, be
 * warned about a duplicate and move the pin instead, delete a pin from its marker, and
 * re-place a pin stranded on a superseded scan. Every write the UI performs is verified
 * against the API — the links it created, the fractions and the file pin they carry —
 * because the canvas can only show that something drew, not that the right thing was
 * written.
 *
 * The map image is a generated noise PNG, unmistakably not a real cave map, for the same
 * two reasons as everywhere in this suite: the archive refuses repeated bytes, and real
 * survey scans never enter test data.
 */

/** Clicks the map sheet at an offset from its centre, in the pane's own pixels. */
async function clickSheet(map: Locator, dx = 0, dy = 0) {
  const box = await map.boundingBox();
  expect(box, 'the map pane has no box').toBeTruthy();
  await map.click({ position: { x: box!.width / 2 + dx, y: box!.height / 2 + dy } });
}

/** Arms a station through the typeahead and waits for the banner to say so. */
async function armByName(page: Page, query: string, station: string) {
  // Typed for real, not filled: the dropdown opens on keystrokes, and a synthetic value
  // set never opens it — which is also true of the person this flow is for.
  const input = page.getByTestId('rastermap-station-search').locator('input');
  await input.click();
  await input.pressSequentially(query, { delay: 40 });
  await page.locator(`.ant-select-item-option[title="${station}"]`).click();
  await expect(page.getByTestId('rastermap-armed')).toContainText(station);
}

/** Waits out the link refetch that follows a write, so the markers on screen are current. */
async function linksRefetched(page: Page, act: () => Promise<void>) {
  const refetch = page.waitForResponse(
    (response) => response.url().includes('/reslinks/for-target') && response.ok(),
    { timeout: 15_000 },
  );
  await act();
  await refetch;
}

test('a pin is authored, moved, deleted and re-placed, and every write lands as designed', async ({
  page,
}) => {
  test.setTimeout(240_000);
  const caveName = `E2E Rastermap Author ${Date.now()}`;
  const mapName = `E2E author sheet ${Date.now()}`;
  const stationA = 'p8.p8.97';
  const stationB = 'p8.bens_dig.217';
  await login(page);

  // A cave of this run's own, with the committed survey fixture on it (never real data).
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });

  await page.getByRole('button', { name: 'Upload model' }).click();
  const uploadModal = page.getByRole('dialog');
  await expect(uploadModal.locator('.ant-upload-drag')).toBeVisible({ timeout: 15_000 });
  const chooser = page.waitForEvent('filechooser');
  await uploadModal.locator('.ant-upload-drag').click();
  await (await chooser).setFiles('e2e/fixtures/P8_Master.3d');
  await uploadModal.getByRole('button', { name: 'Upload model' }).click();
  await expect(page.getByText('Survex .3d')).toBeVisible({ timeout: 30_000 });

  const token = await bearerToken(page);
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  const models = (await (
    await page.request.get(`/api/v1/caves/${caveId}/survey-models`, {
      headers: { Authorization: `Bearer ${token}` },
    })
  ).json()) as { id: string }[];
  expect(models.length).toBeGreaterThan(0);
  const modelId = models[0].id;
  const mapFile = await uploadMapPng(page, token, `${mapName}.png`);

  // ---- Declare the map from the viewer: the strip's add-map doorway, the document
  // picker, the view kind — one POST, and the tab appears.
  await page.getByRole('button', { name: /View in 3D/ }).click();
  const viewer = page.getByRole('dialog').filter({ has: page.getByTestId('caveview-container') });
  await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });

  await viewer.getByTestId('rastermap-add-map').click();
  await page.getByTestId('reslink-target-picker-input').fill(mapName);
  await expect(
    page.getByTestId('reslink-target-picker-row').filter({ hasText: mapName }).first(),
  ).toBeVisible({ timeout: 15_000 });
  await page.getByTestId('reslink-target-picker-row').filter({ hasText: mapName }).first().click();
  await page.getByTestId('rastermap-declare-submit').click();

  const mapTab = viewer.getByRole('tab', { name: new RegExp(mapName) });
  await expect(mapTab).toBeVisible({ timeout: 15_000 });

  // ---- Define points: arm station A by name, click the sheet, and the one designed
  // POST lands — fractions of the drawn picture, pinned to the very file on screen.
  await mapTab.click();
  const map = viewer.getByTestId('rastermap-map');
  await expect(map.locator('canvas').first()).toBeAttached({ timeout: 15_000 });
  await viewer.getByTestId('rastermap-define').click();
  await expect(page.getByTestId('rastermap-authoring')).toBeVisible();

  await armByName(page, 'p8.97', stationA);
  await linksRefetched(page, () => clickSheet(map));

  const placed = await expectPinCount(page, token, modelId, mapFile.documentId, stationA, 1);
  const placedPoint = pointMemberOf(placed[0]);
  expect(placedPoint.anchorFileId).toBe(mapFile.id);
  expect(placedPoint.anchor?.shape).toBe('point');
  expect(placedPoint.anchor?.x).toBeGreaterThan(0);
  expect(placedPoint.anchor?.x).toBeLessThan(1);
  expect(placedPoint.anchor?.y).toBeGreaterThan(0);
  expect(placedPoint.anchor?.y).toBeLessThan(1);
  // The arm was spent on the write; the mode stays for the next station.
  await expect(page.getByTestId('rastermap-armed')).toHaveCount(0);

  if (process.env.RASTERMAP_SHOTS) {
    await page.waitForTimeout(600);
    await viewer.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/30-authoring-placed.png` });
  }

  // ---- The duplicate warning: arming the same station and clicking elsewhere must not
  // write a second link — it offers moving the standing pin, and moving rewrites it.
  await armByName(page, 'p8.97', stationA);
  await clickSheet(map, 80, 50);
  await expect(page.getByTestId('rastermap-move-here')).toBeVisible();
  // Nothing was written while the warning stood.
  expect((await expectPinCount(page, token, modelId, mapFile.documentId, stationA, 1))[0].id).toBe(
    placed[0].id,
  );

  await linksRefetched(page, () => page.getByTestId('rastermap-move-here').click());
  const moved = await expectPinCount(page, token, modelId, mapFile.documentId, stationA, 1);
  expect(moved[0].id).not.toBe(placed[0].id);
  const movedPoint = pointMemberOf(moved[0]);
  expect(movedPoint.anchorFileId).toBe(mapFile.id);
  expect(movedPoint.anchor?.x).not.toBe(placedPoint.anchor?.x);

  // ---- Delete from the marker: pressing the pin offers its corrections; delete takes
  // the whole link, because the pair is the link.
  await clickSheet(map, 80, 50);
  await expect(page.getByTestId('rastermap-delete-pin')).toBeVisible();
  await linksRefetched(page, () => page.getByTestId('rastermap-delete-pin').click());
  await expectPinCount(page, token, modelId, mapFile.documentId, stationA, 0);

  // ---- The superseded-scan flow: a pin measured on scan v1, a v2 uploaded under it,
  // and the authoring surface lists the stranded station and re-places it on v2.
  await armByName(page, '217', stationB);
  await linksRefetched(page, () => clickSheet(map, -60, -40));
  const onV1 = await expectPinCount(page, token, modelId, mapFile.documentId, stationB, 1);
  expect(pointMemberOf(onV1[0]).anchorFileId).toBe(mapFile.id);

  const v2 = await uploadMapVersion(page, token, mapFile.id, `${mapName}-v2.png`);

  // Reopen the viewer so the pane reads the document's new current file.
  await page.keyboard.press('Escape');
  await expect(viewer).not.toBeVisible();
  await page.getByRole('button', { name: /View in 3D/ }).click();
  await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
  await viewer.getByRole('tab', { name: new RegExp(mapName) }).click();
  await expect(map.locator('canvas').first()).toBeAttached({ timeout: 15_000 });

  // The read surface admits the stranded point as a count…
  await expect(viewer.getByTestId('rastermap-superseded')).toBeVisible({ timeout: 15_000 });
  // …and the authoring surface turns it into work: the station, and its re-place.
  await viewer.getByTestId('rastermap-define').click();
  await expect(viewer.getByTestId('rastermap-superseded-list')).toContainText(stationB);
  await viewer.getByTestId('rastermap-replace').click();
  await expect(page.getByTestId('rastermap-armed')).toContainText(stationB);
  await linksRefetched(page, () => clickSheet(map));

  const onV2 = await expectPinCount(page, token, modelId, mapFile.documentId, stationB, 1);
  expect(onV2[0].id).not.toBe(onV1[0].id);
  expect(pointMemberOf(onV2[0]).anchorFileId).toBe(v2.id);
  // Nothing stranded remains, on either surface.
  await expect(viewer.getByTestId('rastermap-superseded-list')).toHaveCount(0);

  if (process.env.RASTERMAP_SHOTS) {
    await page.waitForTimeout(600);
    await viewer.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/31-authoring-replaced.png` });
  }

  // ---- Take the run's own rows down again: the cave and model through the page that
  // owns them. The document stays, as every uploaded fixture in this suite stays.
  await page.keyboard.press('Escape');
  await expect(viewer).not.toBeVisible();
  await page.locator('.ant-card', { hasText: '3D survey models' })
    .getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('No 3D models yet')).toBeVisible({ timeout: 15_000 });
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
});
