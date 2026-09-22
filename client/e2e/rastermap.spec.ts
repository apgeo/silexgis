// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { uniquePng } from './png.ts';

/**
 * The raster-map tabs beside the 3D survey viewer: a declared map appears as a tab, its
 * image draws, and — the sentence the whole layout is built around — <b>switching tabs
 * never remounts the 3D viewer</b>. A remount re-fetches and re-parses the survey and
 * burns a WebGL context, so the proof here is physical: the canvas element is tagged
 * before the switch and must be the same element after it.
 *
 * The map and its pins are authored through the API rather than a UI, because batch one is
 * the read path: there is no declare-map or define-points flow yet, and this spec is what
 * proves the read path against the vocabulary the server now seeds. The image is a
 * generated synthetic PNG — noise, unmistakably not a real cave map — for the same reason
 * every uploaded fixture in this suite is generated: the archive refuses repeated bytes.
 */

/**
 * The signed-in page's own bearer token, read from the same in-memory session the SPA
 * spends. Vite serves the auth module at its source path and the browser's module cache
 * returns the instance the app itself built, so this is the session, not a second login.
 */
async function bearerToken(page: Page): Promise<string> {
  return page.evaluate<string>(`import('/src/auth/auth.tsx').then(async (m) => {
    const user = await m.userManager.getUser();
    if (!user) throw new Error('no signed-in session in the page');
    return user.access_token;
  })`);
}

async function apiJson(
  page: Page,
  token: string,
  method: 'GET' | 'POST',
  path: string,
  data?: unknown,
): Promise<unknown> {
  const response = await page.request.fetch(path, {
    method,
    headers: { Authorization: `Bearer ${token}` },
    ...(data === undefined ? {} : { data }),
  });
  expect(response.ok(), `${method} ${path} answered ${response.status()}`).toBeTruthy();
  return response.json();
}

test('a declared map is a tab beside the 3D view, and switching never remounts the viewer', async ({
  page,
}) => {
  const caveName = `E2E Rastermap Cave ${Date.now()}`;
  const mapName = `E2E map sheet ${Date.now()}`;
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

  // "Upload model" opens a modal with a dragger. The dragger is an antd Upload, so the
  // file arrives through its native chooser (intercepted here), which is what runs its
  // beforeUpload and enables the modal's submit.
  await page.getByRole('button', { name: 'Upload model' }).click();
  const uploadModal = page.getByRole('dialog');
  await expect(uploadModal.locator('.ant-upload-drag')).toBeVisible({ timeout: 15_000 });
  const chooser = page.waitForEvent('filechooser');
  await uploadModal.locator('.ant-upload-drag').click();
  await (await chooser).setFiles('e2e/fixtures/P8_Master.3d');
  // The dragger now shows the file name; a Survex .3d carries its own placement, so the
  // modal's submit is enabled with no origin answered.
  await uploadModal.getByRole('button', { name: 'Upload model' }).click();
  await expect(page.getByText('Survex .3d')).toBeVisible({ timeout: 30_000 });

  // Author the vocabulary's two link shapes through the API: the declaration and two pins.
  const token = await bearerToken(page);

  const models = (await apiJson(page, token, 'GET', `/api/v1/caves/${caveId}/survey-models`)) as {
    id: string;
  }[];
  expect(models.length).toBeGreaterThan(0);
  const modelId = models[0].id;

  const upload = await page.request.post('/api/v1/files?allowDuplicate=false', {
    headers: { Authorization: `Bearer ${token}` },
    multipart: {
      file: { name: `${mapName}.png`, mimeType: 'image/png', buffer: uniquePng(512) },
    },
  });
  expect(upload.ok(), `upload answered ${upload.status()}`).toBeTruthy();
  const mapFile = (await upload.json()) as { id: string; documentId: string };

  const relationTypes = (await apiJson(page, token, 'GET', '/api/v1/reslinks/relation-types')) as {
    id: number;
    code: string;
  }[];
  const relationId = (code: string) => {
    const row = relationTypes.find((r) => r.code === code);
    expect(row, `seeded relation ${code} is missing`).toBeTruthy();
    return row!.id;
  };

  // "This image is the plan view of this model" — document main, model whole.
  await apiJson(page, token, 'POST', '/api/v1/reslinks', {
    relationTypeId: relationId('map-plan-of'),
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

  // Two pins: fractions of the drawn picture, pinned to the very file just uploaded.
  for (const [station, x, y] of [
    ['p8.p8.98', 0.3, 0.4],
    ['p8.bens_dig.217', 0.7, 0.6],
  ] as const) {
    await apiJson(page, token, 'POST', '/api/v1/reslinks', {
      relationTypeId: relationId('map-station-point'),
      description: null,
      members: [
        {
          targetType: 'document',
          targetId: mapFile.documentId,
          isMain: true,
          sortOrder: 0,
          note: null,
          anchorKind: 'imageRegion',
          anchor: { shape: 'point', x, y },
          anchorFileId: mapFile.id,
        },
        {
          targetType: 'surveyModel',
          targetId: modelId,
          isMain: false,
          sortOrder: 1,
          note: null,
          anchorKind: 'modelStation',
          anchor: { station },
          anchorFileId: null,
        },
      ],
    });
  }

  // Open the viewer: the 3D pane loads, and the declared map stands beside it as a tab.
  await page.getByRole('button', { name: /View in 3D/ }).click();
  const dialog = page.getByRole('dialog');
  const caveCanvas = dialog.getByTestId('caveview-container').locator('canvas').first();
  await expect(caveCanvas).toBeAttached({ timeout: 30_000 });
  await expect(dialog.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
  await expect(dialog.getByRole('tab', { name: '3D' })).toBeVisible({ timeout: 15_000 });
  const mapTab = dialog.getByRole('tab', { name: new RegExp(mapName) });
  await expect(mapTab).toBeVisible({ timeout: 15_000 });

  // Tag the living canvas, so that "survived the switch" means this very element.
  await caveCanvas.evaluate((el) => {
    (el as HTMLElement).dataset.rastermapProbe = 'alive';
  });

  // To the map: the OL surface builds and draws — its own canvas, no missing-image state,
  // and no superseded-scan note, because both pins name the file on screen.
  await mapTab.click();
  await expect(dialog.getByTestId('rastermap-map')).toBeVisible({ timeout: 15_000 });
  await expect(dialog.getByTestId('rastermap-map').locator('canvas').first()).toBeAttached({
    timeout: 15_000,
  });
  await expect(dialog.getByTestId('rastermap-missing')).toHaveCount(0);
  await expect(dialog.getByTestId('rastermap-superseded')).toHaveCount(0);

  // A record that the map really drew on screen, at desk width and again at the phone
  // width the mobile binding is designed for. Written only when the sink is present, so an
  // ordinary run in another checkout does not need the directory.
  if (process.env.RASTERMAP_SHOTS) {
    await page.waitForTimeout(600);
    await dialog.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/20-rastermap-desktop.png` });
    await page.setViewportSize({ width: 360, height: 780 });
    await expect(dialog.getByTestId('rastermap-map').locator('canvas').first()).toBeAttached();
    await page.waitForTimeout(600);
    await dialog.screenshot({ path: `${process.env.RASTERMAP_SHOTS}/21-rastermap-360px.png` });
    await page.setViewportSize({ width: 1280, height: 900 });
  }

  // The 3D pane is hidden, not gone: the tagged canvas is still in the document…
  await expect(dialog.locator('canvas[data-rastermap-probe="alive"]')).toBeAttached();

  // …and back on the 3D tab it is the same element, with no loading pass restarted.
  await dialog.getByRole('tab', { name: '3D' }).click();
  await expect(dialog.locator('canvas[data-rastermap-probe="alive"]')).toBeAttached();
  await expect(dialog.getByTestId('caveview-loading')).toHaveCount(0);

  // Take the run's own rows down again: the cave and model through the page that owns
  // them. The document stays, as every uploaded fixture in this suite stays.
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();
  await page.locator('.ant-card', { hasText: '3D survey models' })
    .getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('No 3D models yet')).toBeVisible({ timeout: 15_000 });
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
});
