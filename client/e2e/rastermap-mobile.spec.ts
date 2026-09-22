// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import {
  apiJson,
  bearerToken,
  expectPinCount,
  pointMemberOf,
  relationId,
  uploadMapPng,
} from './rastermapApi.ts';

/**
 * The mobile binding of the authoring flow: on a phone profile — coarse pointer, touch —
 * at 360 CSS px, the paired mode must actually be usable. The armed-station banner has
 * to be on screen where a thumb can reach its cancel, the controls have to be sized for
 * the pointer driving them, and a tap on the sheet has to land the same honest write a
 * desktop click lands. This runs under the phone project (Pixel 7), which is what makes
 * `(pointer: coarse)` true for the sizing the assertions check.
 *
 * The map and its declaration are seeded through the API — the declare dialog has its
 * own coverage on desktop — so this spec is exactly the phone-shaped part: arming and
 * placing at 360 px.
 */

test('a pin is authored by finger at 360px, with finger-sized controls', async ({ page }) => {
  test.setTimeout(240_000);
  const caveName = `E2E Rastermap Phone ${Date.now()}`;
  const mapName = `E2E phone sheet ${Date.now()}`;
  const station = 'p8.p8.97';
  await login(page);

  // Seed at the device's own width: the cave through its form, the rest through the API.
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId).toBeTruthy();

  const token = await bearerToken(page);
  const model = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${token}` },
    multipart: {
      file: {
        name: 'P8_Master.3d',
        mimeType: 'application/octet-stream',
        buffer: readFileSync('e2e/fixtures/P8_Master.3d'),
      },
    },
  });
  expect(model.ok(), `model upload answered ${model.status()}`).toBeTruthy();
  const modelId = ((await model.json()) as { id: string }).id;

  const mapFile = await uploadMapPng(page, token, `${mapName}.png`);
  const relationTypes = (await apiJson(page, token, 'GET', '/api/v1/reslinks/relation-types')) as {
    id: number;
    code: string;
  }[];
  await apiJson(page, token, 'POST', '/api/v1/reslinks', {
    relationTypeId: relationId(relationTypes, 'map-plan-of'),
    description: null,
    members: [
      {
        targetType: 'document',
        targetId: mapFile.documentId,
        isMain: true,
        sortOrder: 0,
        note: null,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
      },
      {
        targetType: 'surveyModel',
        targetId: modelId,
        isMain: false,
        sortOrder: 1,
        note: null,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
      },
    ],
  });

  // The binding is 360 px — narrower than the device default, so the assertion is about
  // the layout the binding names, while the pointer stays the profile's coarse one.
  await page.setViewportSize({ width: 360, height: 780 });
  await page.reload();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 30_000 });

  await page.getByRole('button', { name: /View in 3D/ }).click();
  const viewer = page.getByRole('dialog').filter({ has: page.getByTestId('caveview-container') });
  await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 45_000 });

  await viewer.getByRole('tab', { name: new RegExp(mapName) }).click();
  const map = viewer.getByTestId('rastermap-map');
  await expect(map.locator('canvas').first()).toBeAttached({ timeout: 15_000 });

  // Finger-sized define control: the same coarse-pointer axis that grows the marker
  // hit tolerance grows the buttons a finger has to land on.
  const define = viewer.getByTestId('rastermap-define');
  await expect(define).toHaveClass(/ant-btn-lg/);
  await define.click();

  // Arm by name — the typeahead is the phone's practical arming path — and the banner
  // with its cancel is on screen at 360 px, above the strip, where a thumb reaches it.
  const search = page.getByTestId('rastermap-station-search').locator('input');
  await search.tap();
  await search.pressSequentially('p8.97', { delay: 40 });
  await page.locator(`.ant-select-item-option[title="${station}"]`).click();
  const armed = page.getByTestId('rastermap-armed');
  await expect(armed).toBeInViewport();
  await expect(page.getByTestId('rastermap-disarm')).toBeInViewport();
  await expect(page.getByTestId('rastermap-disarm')).toHaveClass(/ant-btn-lg/);

  // A tap on the sheet writes the same designed POST a desktop click writes.
  const box = await map.boundingBox();
  expect(box).toBeTruthy();
  await map.tap({ position: { x: box!.width / 2, y: box!.height / 2 } });

  const placed = await expectPinCount(page, token, modelId, mapFile.documentId, station, 1);
  const point = pointMemberOf(placed[0]);
  expect(point.anchorFileId).toBe(mapFile.id);
  expect(point.anchor?.shape).toBe('point');

  if (process.env.RASTERMAP_SHOTS) {
    await page.waitForTimeout(600);
    await viewer.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/32-authoring-360px.png` });
  }

  // Down again: model and cave through the API this run created them with. The page
  // leaves the cave first — deleting it under a page still polling it would fill the
  // sweep with 404s the application never caused.
  await page.keyboard.press('Escape');
  await expect(viewer).not.toBeVisible();
  await page.goto('/');
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 30_000 });
  const dropModel = await page.request.delete(`/api/v1/survey-models/${modelId}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(dropModel.ok()).toBeTruthy();
  const dropCave = await page.request.delete(`/api/v1/caves/${caveId}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(dropCave.ok()).toBeTruthy();
});
