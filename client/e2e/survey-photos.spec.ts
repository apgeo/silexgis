// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, waitForScene3dReady } from './helpers.ts';
import { apiJson, bearerToken, uploadMapPng } from './rastermapApi.ts';

// A photograph linked to a station of a cave's survey, found where it was taken: marked over the
// model in the survey viewer, and counted at its station in the 3D scene. What this shows that no
// unit test can: that a link made through the real API reaches the real viewer bundle as a marker
// at a real station, that pressing the marker opens the station's pictures, and that the scene
// finds the station's position through the routes that decide what this reader may see.

const fixture = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'P8_Master.3d');

/**
 * A station of the committed survey fixture that the viewer draws. (Not every station the server
 * reads is one: the fixture's `p8.p8.98` is read into a row and has nothing drawn at it.)
 */
const STATION = 'p8.p8.97';

const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

async function createCave(page: Page, name: string): Promise<string> {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId).toBeTruthy();
  madeCaves.push(caveId!);
  return caveId!;
}

test('a photograph linked to a station is marked where it was taken, in the survey viewer and in the 3D scene', async ({
  page,
}) => {
  test.setTimeout(300_000);
  await login(page);
  const caveName = `E2E Photos ${Date.now()}`;
  const caveId = await createCave(page, caveName);
  const token = await bearerToken(page);

  const created = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${token}` },
    multipart: {
      file: { name: 'Survey.3d', mimeType: 'application/octet-stream', buffer: readFileSync(fixture) },
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const modelId = ((await created.json()) as { id: string }).id;
  // Read into stations: until then there is no station for the photograph to be placed at.
  await expect
    .poll(
      async () =>
        ((await apiJson(page, token, 'GET', `/api/v1/survey-models/${modelId}`)) as { status: string }).status,
      { timeout: 90_000 },
    )
    .toBe('ready');

  // A generated picture — never a real photograph — linked to one station of the survey.
  const picture = await uploadMapPng(page, token, `E2E photo ${Date.now()}.png`, 64);
  await apiJson(page, token, 'POST', '/api/v1/reslinks', {
    relationTypeId: null,
    description: null,
    members: [
      {
        targetType: 'document',
        targetId: picture.documentId,
        // An untyped link is undirected, so neither member is the main one.
        isMain: false,
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
        anchorKind: 'modelStation',
        anchor: { station: STATION },
        anchorFileId: null,
      },
    ],
  });

  // A picture is shown by its rendering, which a background job makes after the upload: until it
  // exists the link names a document with nothing to display, and no marker is owed.
  await expect
    .poll(
      async () => {
        const page_ = (await apiJson(
          page,
          token,
          'GET',
          `/api/v1/reslinks/for-target?type=surveyModel&id=${modelId}`,
        )) as { items: { members: { display: { thumbnailUrl: string | null } | null }[] }[] };
        return page_.items.some((link) => link.members.some((m) => m.display?.thumbnailUrl));
      },
      { timeout: 90_000, message: 'the picture was never given a rendering' },
    )
    .toBe(true);

  // ---- In the survey viewer: a camera over the model, at that station, pressed to open it ----
  await gotoRoute(page, `/caves/${caveId}`);
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  await page
    .locator('.ant-card', { hasText: '3D survey models' })
    .getByRole('row')
    .filter({ hasText: 'Survey' })
    .getByRole('button', { name: 'View in 3D' })
    .click();
  const viewer = page.getByRole('dialog').filter({ has: page.getByTestId('caveview-container') });
  await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 60_000 });

  const markers = viewer.locator('.cv-media-marker');
  await expect(markers).toHaveCount(1, { timeout: 30_000 });
  await expect(markers.first()).toHaveAttribute('data-station', STATION);
  // It is on the screen when its station is: the viewer opens on the whole model.
  await expect(markers.first()).toBeVisible();

  await markers.first().click();
  const strip = viewer.locator('.cv-media-strip');
  await expect(strip).toBeVisible();
  await expect(strip.locator('img')).toHaveCount(1);
  await page.keyboard.press('Escape');

  // ---- In the 3D scene: the cave's photographs, counted at their station ----
  await gotoRoute(page, `/caves/${caveId}`);
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  await page.getByRole('button', { name: 'Open in 3D' }).click();
  await page.waitForURL(/\/map3d/);
  await waitForScene3dReady(page);
  await page.getByTestId('scene3d-layers-trigger').click();
  await expect(page.getByTestId('scene3d-photos-status')).toHaveText(
    'Photographs: 1 · stations: 1 — press a camera to open them.',
    { timeout: 60_000 },
  );

  // And off is off.
  await page.getByTestId('scene3d-photos-toggle').click();
  await expect(page.getByTestId('scene3d-photos-status')).toHaveText('Not shown.');
});
