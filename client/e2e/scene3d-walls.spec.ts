// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, waitForScene3dReady } from './helpers.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';

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
  madeCaves.push(caveId!);
  return caveId!;
}

/**
 * The caves this file made, removed again when the test that made them ends — passed or failed.
 *
 * The suite shares one database, and a cave left behind with an entrance is not inert there: it
 * joins the clusters the map draws at low zoom, so a test elsewhere that taps "the cluster in the
 * middle of the view" meets a different cluster than it was written against.
 */
const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

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

  // By address, so through a whole sign-in round trip: waited out before the page is looked at,
  // or a busy machine has this looking for the heading on the redirect pages.
  await gotoRoute(page, `/caves/${caveId}`);
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

/** Uploads the invented walls for a cave, declared in plain metres about a position, and answers the model's id. */
async function uploadWalls(
  page: Page,
  token: string,
  caveId: string,
  longitude: number,
  latitude: number,
): Promise<string> {
  const created = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${token}` },
    multipart: {
      file: { name: 'walls.stl', mimeType: 'application/octet-stream', buffer: tinyStl() },
      originLongitude: longitude.toFixed(5),
      originLatitude: latitude.toFixed(5),
      originHeightM: '1200',
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  return ((await created.json()) as { id: string }).id;
}

/** Waits for the API's own worker to turn an upload into a mesh a scene can draw. */
async function waitForMesh(page: Page, token: string, modelId: string) {
  await expect
    .poll(
      async () => {
        const model = (await apiJson(page, token, 'GET', `/api/v1/survey-models/${modelId}`)) as {
          status: string;
          meshUrl: string | null;
        };
        return model.status === 'ready' && model.meshUrl !== null;
      },
      { timeout: 90_000, message: 'the uploaded walls were never converted into a drawable mesh' },
    )
    .toBe(true);
}

test('the walls of every cave in view are drawn together, counted in words, and all released on request', async ({
  page,
}) => {
  test.setTimeout(300_000);
  const stamp = Date.now();
  await login(page);
  const firstName = `E2E Walls North ${stamp}`;
  const firstId = await createCave(page, firstName);
  const secondId = await createCave(page, `E2E Walls South ${stamp}`);
  const token = await bearerToken(page);

  // Two caves under three hundred metres apart, hundreds of kilometres from every seeded cave and
  // from the cave the test above anchors: the mode counts every cave in the view, and a neighbour
  // — seeded, or left by another test — would make the count this asserts depend on what else is
  // in the database.
  const longitude = 27.9;
  const latitude = 47.6;
  const firstModel = await uploadWalls(page, token, firstId, longitude, latitude);
  const secondModel = await uploadWalls(page, token, secondId, longitude, latitude - 0.0025);

  // An entrance gives the first cave a position of its own, which is what its page's
  // "Open in 3D" puts the camera on. Without one the scene would open wherever it opens, and
  // "every cave in view" is a question about where the camera is.
  const entranceTypes = (await apiJson(page, token, 'GET', '/api/v1/entrance-types')) as {
    id: number;
  }[];
  await apiJson(page, token, 'POST', `/api/v1/caves/${firstId}/entrances`, {
    name: 'E2E entrance',
    entranceTypeId: entranceTypes[0].id,
    isMain: true,
    geom: { type: 'Point', coordinates: [longitude, latitude] },
    altitude: 1200,
    description: null,
    positionQuality: 'gps',
    surveyedAt: null,
  });

  await waitForMesh(page, token, firstModel);
  await waitForMesh(page, token, secondModel);

  const meshRequests: string[] = [];
  page.on('request', (request) => {
    if (/\/api\/v1\/files\/[0-9a-f-]+\/content/.test(request.url())) {
      meshRequests.push(request.url());
    }
  });

  await gotoRoute(page, `/caves/${firstId}`);
  await expect(page.getByRole('heading', { name: firstName })).toBeVisible({ timeout: 15_000 });
  await page.getByRole('button', { name: 'Open in 3D' }).click();
  await page.waitForURL(/\/map3d/);
  await waitForScene3dReady(page);

  // The scene arrives in its default mode with the cave selected, so that one cave's walls are
  // read first. Waited for, so the fetches counted below are the other mode's and only its.
  await page.getByTestId('scene3d-layers-trigger').click();
  const status = page.getByTestId('scene3d-mesh-status');
  await expect(status).toHaveText(/walls are drawn/, { timeout: 60_000 });
  const fetchedForTheSelectedCave = meshRequests.length;

  await page.getByRole('radio', { name: 'Every cave in view' }).click();

  // Both caves, said as a count out of a count and with nothing still on its way.
  await expect(status).toHaveText(/^Walls of 2 of 2 caves in view — [\d.]+ (KB|MB)\.$/, {
    timeout: 60_000,
  });
  expect(
    meshRequests.length - fetchedForTheSelectedCave,
    'one fetch per cave in view, and none for a mesh already asked for',
  ).toBe(2);

  // Off releases all of them, and the row says so in the words a viewer reads.
  await page.getByTestId('scene3d-mesh-toggle').click();
  await expect(status).toHaveText(/not loaded/);
  expect(meshRequests.length - fetchedForTheSelectedCave).toBe(2);
});
