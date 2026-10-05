// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login, waitForScene3dReady } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

// The walls of a cave are the heaviest thing the product draws, and until now nothing in the
// suite would have noticed if they stopped drawing. They load off the cave selection, which no
// synthetic pointer event can make at a camera that answers every pick with a cluster — so the
// path driven here is the one a person uses: the cave's own page opens the scene with the cave
// selected and the camera on it.

/** Two invented triangles in local metres, written as a binary STL: 80-byte header, count, 50 bytes each. */
function tinyStl(): Buffer {
  const triangles: number[][][] = [
    [[0, 0, 0], [4, 0, 0], [0, 4, 0]],
    [[0, 0, -3], [4, 0, -3], [0, 4, -3]],
  ];
  const buffer = Buffer.alloc(84 + 50 * triangles.length);
  buffer.write('e2e walls fixture', 0, 'ascii');
  buffer.writeUInt32LE(triangles.length, 80);
  let at = 84;
  for (const triangle of triangles) {
    for (const component of [0, 0, 1]) {
      buffer.writeFloatLE(component, at);
      at += 4;
    }
    for (const vertex of triangle) {
      for (const component of vertex) {
        buffer.writeFloatLE(component, at);
        at += 4;
      }
    }
    buffer.writeUInt16LE(0, at);
    at += 2;
  }
  return buffer;
}

/** A cave of this run's own, through the form, so nothing here depends on seeded names. */
async function createCave(page: Page, name: string): Promise<string> {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId, 'the cave page names the cave in its address').toBeTruthy();
  return caveId!;
}

test('the walls of a cave are uploaded, converted, drawn in the scene from its page, and unloaded on request', async ({
  page,
}) => {
  test.setTimeout(240_000);
  const caveName = `E2E Walls ${Date.now()}`;
  await login(page);
  const caveId = await createCave(page, caveName);
  const token = await bearerToken(page);

  // Declared in plain metres about a point in the demo region: an .stl says nothing about
  // where its numbers are, so the declaration is what places it.
  const created = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${token}` },
    multipart: {
      file: { name: 'walls.stl', mimeType: 'application/octet-stream', buffer: tinyStl() },
      originLongitude: '25.20930',
      originLatitude: '45.51870',
      originHeightM: '1200',
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const modelId = ((await created.json()) as { id: string }).id;

  // The conversion runs in the API's own worker; the row says when the mesh exists, and how big.
  await expect
    .poll(
      async () => {
        const model = (await (
          await page.request.get(`/api/v1/survey-models/${modelId}`, {
            headers: { Authorization: `Bearer ${token}` },
          })
        ).json()) as { status: string; meshUrl: string | null; meshSizeBytes: number | null };
        return model.status === 'ready' && model.meshUrl !== null && (model.meshSizeBytes ?? 0) > 0;
      },
      { timeout: 90_000, message: 'the uploaded walls were never converted into a drawable mesh' },
    )
    .toBe(true);

  // The mesh is fetched from this installation, once, and from nowhere else.
  const meshRequests: string[] = [];
  page.on('request', (request) => {
    if (/\/api\/v1\/files\/[0-9a-f-]+\/content/.test(request.url())) {
      meshRequests.push(request.url());
    }
  });

  await page.goto(`/caves/${caveId}`);
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  await page.getByRole('button', { name: 'Open in 3D' }).click();
  await page.waitForURL(/\/map3d/);
  await waitForScene3dReady(page);

  await page.getByTestId('scene3d-layers-trigger').click();
  const status = page.getByTestId('scene3d-mesh-status');
  await expect(status).toHaveText(/walls are drawn/, { timeout: 60_000 });
  // How big it was is said in words, which is the whole of what a viewer on a metered
  // connection gets to decide by.
  await expect(status).toHaveText(/KB|MB/);
  expect(meshRequests.length, 'one fetch of the converted mesh').toBe(1);
  expect(new URL(meshRequests[0]).origin).toBe(new URL(page.url()).origin);

  // Off is a real unload, and the row says so in the words a viewer reads.
  await page.getByTestId('scene3d-mesh-toggle').click();
  await expect(status).toHaveText(/not loaded/);
  expect(meshRequests.length).toBe(1);
});
