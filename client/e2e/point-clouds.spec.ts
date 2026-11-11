// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';
import { asPerson, registerAccount, signedInElsewhere, tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

// A point cloud's way through the product: dropped on a cave, described by its header on the
// card, looked at on its own, and put on the earth by entrance and bearing. The browser lane runs
// the API without a point-cloud worker, so the server inspects the header and stops at "not
// tiled"; the tiles the local viewer reads are canned here, by rewriting the upload's answer to
// name a tileset and serving a two-file tileset of invented points for it. What that proves is
// the client's side — the card offers the viewer once a tileset exists, the viewer reads a tileset
// from this installation's address and draws it — and not the tiler, which the server tests
// prove against a canned worker.

/**
 * An invented LAS 1.2 file: the 227-byte public header the server reads, then `points` records
 * of format 1, all zero. Nothing in it is a real scan. The header says the cloud spans
 * `size` metres and names no coordinate system, so the server treats it as local.
 */
function tinyLas(points: number, size: [number, number, number]): Buffer {
  const headerSize = 227;
  const recordLength = 28;
  const buffer = Buffer.alloc(headerSize + points * recordLength);
  buffer.write('LASF', 0, 'ascii');
  buffer[24] = 1; // version major
  buffer[25] = 2; // version minor
  buffer.write('e2e point clouds', 58, 'ascii');
  buffer.writeUInt16LE(headerSize, 94);
  buffer.writeUInt32LE(headerSize, 96); // offset to point data
  buffer.writeUInt32LE(0, 100); // no variable-length records
  buffer[104] = 1; // point format 1
  buffer.writeUInt16LE(recordLength, 105);
  buffer.writeUInt32LE(points, 107);
  const doubles = (at: number, a: number, b: number, c: number) => {
    buffer.writeDoubleLE(a, at);
    buffer.writeDoubleLE(b, at + 8);
    buffer.writeDoubleLE(c, at + 16);
  };
  doubles(131, 0.01, 0.01, 0.01); // scale
  doubles(155, 0, 0, 0); // offset
  doubles(179, size[0], 0, size[1]); // max x, min x, max y
  doubles(203, 0, size[2], 0); // min y, max z, min z
  return buffer;
}

/** A 3D Tiles point tile of `count` invented points scattered in a five-metre box. */
function tinyPnts(count: number): Buffer {
  const positions = Buffer.alloc(count * 12);
  for (let i = 0; i < count; i += 1) {
    positions.writeFloatLE(((i * 7919) % 100) / 20 - 2.5, i * 12);
    positions.writeFloatLE(((i * 104729) % 100) / 20 - 2.5, i * 12 + 4);
    positions.writeFloatLE(((i * 1299709) % 100) / 20 - 2.5, i * 12 + 8);
  }
  let json = JSON.stringify({ POINTS_LENGTH: count, POSITION: { byteOffset: 0 } });
  while ((28 + json.length) % 8 !== 0) {
    json += ' ';
  }
  const header = Buffer.alloc(28);
  header.write('pnts', 0, 'ascii');
  header.writeUInt32LE(1, 4);
  header.writeUInt32LE(28 + json.length + positions.length, 8);
  header.writeUInt32LE(json.length, 12);
  header.writeUInt32LE(positions.length, 16);
  header.writeUInt32LE(0, 20);
  header.writeUInt32LE(0, 24);
  return Buffer.concat([header, Buffer.from(json, 'utf8'), positions]);
}

const tilesetJson = JSON.stringify({
  asset: { version: '1.0' },
  geometricError: 10,
  root: {
    boundingVolume: { box: [0, 0, 0, 3, 0, 0, 0, 3, 0, 0, 0, 3] },
    geometricError: 0,
    refine: 'ADD',
    content: { uri: 'points.pnts' },
  },
});

interface UploadAnswer {
  id: string;
  status: string;
  stage: string;
  cave: { id: string };
  caveSurvey: { id: string };
  statusPageUrl: string;
  pointCloud: { modelId: string; pointCount: number; tilesetUrl: string | null } | null;
}

/** A cave of the run's own through the API, with an exact main entrance in the demo region. */
async function caveWithEntrance(page: Page, name: string, locationProtected: boolean): Promise<string> {
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected,
    explorationStatus: 'Unknown',
    isShowCave: false,
  });
  madeCaves.push(cave.id);
  const entranceTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/entrance-types');
  await asPerson(page, 'POST', `/api/v1/caves/${cave.id}/entrances`, {
    name: 'Main',
    entranceTypeId: entranceTypes[0].id,
    isMain: true,
    geom: { type: 'Point', coordinates: [25.2093, 45.5187] },
    altitude: 1200,
    description: null,
    positionQuality: 'gps',
    surveyedAt: null,
  });
  return cave.id;
}

/** Sends a LAS the way the upload dialog does — open, PUT, complete — and waits for the run to end. */
async function uploadLas(page: Page, caveId: string, title: string): Promise<UploadAnswer> {
  const bytes = tinyLas(1000, [12, 8, 3]);
  const name = 'scan.las';
  const opened = await asPerson<UploadAnswer>(page, 'POST', '/api/v1/survey-uploads', {
    source: 'pointcloud',
    cave: { id: caveId },
    surveyTitle: title,
    files: [{ name, size: bytes.length, role: 'archive' }],
  });
  await asPerson(page, 'PUT', `/api/v1/survey-uploads/${opened.id}/files/${name}`, bytes, {
    'Content-Type': 'application/octet-stream',
  });
  await asPerson(page, 'POST', `/api/v1/survey-uploads/${opened.id}/complete`);
  let last = opened;
  await expect
    .poll(
      async () => {
        last = await asPerson<UploadAnswer>(page, 'GET', `/api/v1/survey-uploads/${opened.id}`);
        return last.status;
      },
      { timeout: 90_000, message: 'the point-cloud upload never ended its run' },
    )
    .toMatch(/^(succeeded|failed)$/);
  return last;
}

const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

test('a point cloud is uploaded, described on its card, viewed on its own and placed by entrance and bearing', async ({
  page,
}) => {
  test.setTimeout(240_000);
  await login(page);
  const caveId = await caveWithEntrance(page, `E2E Scan ${Date.now()}`, false);
  const upload = await uploadLas(page, caveId, 'E2E scan survey');

  // Without a worker the run ends successfully at the tiling stage, having read the header.
  expect(upload.status, JSON.stringify(upload)).toBe('succeeded');
  expect(upload.pointCloud, 'the header was inspected').not.toBeNull();
  expect(upload.pointCloud!.pointCount).toBe(1000);
  const modelId = upload.pointCloud!.modelId;

  // The tiles a worker would have made, canned: the upload's answer is rewritten to name a
  // tileset at this installation's tiles address, and that address is answered here.
  const tilesetUrl = `/api/v1/survey-models/${modelId}/tiles/tileset.json?token=e2e`;
  await page.route(`**/api/v1/survey-uploads/${upload.id}`, async (route) => {
    const answer = (await (await route.fetch()).json()) as UploadAnswer & {
      pointCloud: Record<string, unknown>;
    };
    answer.pointCloud = { ...answer.pointCloud, tilesetUrl, tileBytes: 4096, tileCount: 1 };
    await route.fulfill({ json: answer });
  });
  const tileRequests: string[] = [];
  await page.route(`**/api/v1/survey-models/${modelId}/tiles/**`, async (route) => {
    tileRequests.push(route.request().url());
    if (route.request().url().includes('tileset.json')) {
      await route.fulfill({ contentType: 'application/json', body: tilesetJson });
    } else {
      await route.fulfill({ contentType: 'application/octet-stream', body: tinyPnts(200) });
    }
  });

  await gotoRoute(page, upload.statusPageUrl);
  const card = page.getByTestId('point-cloud-card');
  await expect(card).toBeVisible({ timeout: 15_000 });
  await expect(card.getByTestId('point-cloud-facts')).toContainText('1,000');
  await expect(card.getByTestId('point-cloud-tiles')).toBeVisible();

  // The local viewer: no globe, the cloud in its own metres, read from this installation only.
  await card.getByTestId('point-cloud-view').click();
  const viewer = page.getByTestId('point-cloud-viewer');
  await expect(viewer).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('point-cloud-surface').locator('canvas')).toBeAttached({ timeout: 60_000 });
  await expect.poll(() => tileRequests.some((url) => url.includes('tileset.json')), { timeout: 60_000 }).toBe(true);
  await expect.poll(() => tileRequests.some((url) => url.includes('points.pnts')), { timeout: 60_000 }).toBe(true);
  // The server hands out one signed address, the root's; every tile under it has to carry the
  // same token, or the real tiles endpoint refuses each one and the scene stays empty.
  expect(tileRequests.find((url) => url.includes('tileset.json'))).toContain('token=e2e');
  expect(tileRequests.find((url) => url.includes('points.pnts'))).toContain('token=e2e');
  await expect(page.getByTestId('point-cloud-failed')).toHaveCount(0);
  // The measure is offered once the cloud is drawn; the clicks themselves need a depth buffer
  // the software renderer does not answer for reliably, so what the browser proves is the
  // control and its prompt, and the distance arithmetic is proved against the engine's double.
  await expect(page.getByTestId('point-cloud-measure')).toBeEnabled({ timeout: 60_000 });
  await page.getByTestId('point-cloud-measure').click();
  await expect(page.getByTestId('point-cloud-measure-readout')).toHaveText('Click two points on the scan.');
  await page.keyboard.press('Escape');

  // Placement from the cave's page: the dialog starts at the main entrance, the bearing is typed.
  await gotoRoute(page, `/caves/${caveId}`);
  const state = page.getByTestId(`scan-placement-state-${modelId}`);
  await expect(state).toHaveText('Not placed', { timeout: 15_000 });
  await page.getByTestId(`scan-place-${modelId}`).click();
  const dialog = page.getByTestId('scan-placement-dialog');
  await expect(dialog).toBeVisible();
  // The fields by their labels: the number inputs carry no test id into the page.
  await expect(dialog.getByRole('spinbutton', { name: 'Longitude of the origin' })).toHaveValue(/^25\.2093/);
  await expect(dialog.getByRole('spinbutton', { name: 'Latitude of the origin' })).toHaveValue(/^45\.5187/);
  await dialog.getByRole('spinbutton', { name: /^Altitude of the file/ }).fill('1200');
  await dialog.getByRole('spinbutton', { name: /^Bearing of the scan/ }).fill('90');
  // The dialog's footer is outside its content element, so the button is found through the dialog itself.
  await page.getByRole('dialog', { name: 'Place on the map' }).getByRole('button', { name: 'Place', exact: true }).click();
  await expect(state).toContainText('Placed by hand', { timeout: 15_000 });
  await expect(state).toContainText('90°');

  const model = await asPerson<{ placementSource: string; placementHeadingDeg: number; anchorLongitude: number }>(
    page,
    'GET',
    `/api/v1/survey-models/${modelId}`,
  );
  expect(model.placementSource).toBe('manual');
  expect(model.placementHeadingDeg).toBe(90);
  expect(model.anchorLongitude).toBeCloseTo(25.2093, 4);

  // Taken off the map again, and still a scan.
  await page.getByTestId(`scan-unplace-${modelId}`).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(state).toHaveText('Not placed', { timeout: 15_000 });
});

test('a reader without the exact position is told neither where the scan is nor that its upload exists', async ({
  page,
  browser,
  consoleErrors,
}) => {
  test.setTimeout(180_000);
  await login(page);
  const caveName = `E2E Scan Protected ${Date.now()}`;
  const caveId = await caveWithEntrance(page, caveName, true);
  const upload = await uploadLas(page, caveId, 'E2E protected scan');
  expect(upload.status, JSON.stringify(upload)).toBe('succeeded');
  const modelId = upload.pointCloud!.modelId;
  await asPerson(page, 'PUT', `/api/v1/survey-models/${modelId}/scan-placement`, {
    longitude: 25.2093,
    latitude: 45.5187,
    heightM: 1200,
    headingDeg: 45,
  });

  // The positive case first: the administrator, who holds the exact position, is told where.
  const placed = await asPerson<{ anchorLongitude: number | null }>(page, 'GET', `/api/v1/survey-models/${modelId}`);
  expect(placed.anchorLongitude).not.toBeNull();
  await gotoRoute(page, `/caves/${caveId}`);
  await expect(page.getByTestId(`scan-placement-state-${modelId}`)).toContainText('Placed by hand', { timeout: 15_000 });
  await expect(page.getByTestId(`scan-placement-state-${modelId}`).locator('xpath=ancestor::tr[1]')).toContainText(
    'E2E protected scan',
  );

  // A reader granted read, and read alone, on the protected cave.
  const reader = await registerAccount(page.request, 'scanreader');
  await asPerson(page, 'PUT', `/api/v1/objects/feature/${caveId}/access`, {
    entries: [{ subjectKind: 'user', subjectId: reader.id, effect: 'allow', actions: 'read', scopeKind: 'object' }],
  });
  const theirs = await signedInElsewhere(browser, reader);
  try {
    const token = await theirs.page.evaluate<string>(`import('/src/auth/auth.tsx').then(async (m) => {
      const user = await m.userManager.getUser();
      return user.access_token;
    })`);
    const asReader = (path: string) =>
      theirs.page.request.get(path, { headers: { Authorization: `Bearer ${token}` } });

    // The scan's placement is the cave's position, and is withheld with it — as a model they are
    // not told exists, or as a model whose anchor members are empty.
    const model = await asReader(`/api/v1/survey-models/${modelId}`);
    if (model.ok()) {
      const body = (await model.json()) as { anchorLongitude: number | null; placementHeadingDeg: number | null };
      expect(body.anchorLongitude).toBeNull();
      expect(body.placementHeadingDeg).toBeNull();
    } else {
      expect(model.status()).toBe(404);
    }

    // The scene's listing of placed scans is not asserted here: without a worker nothing is
    // tiled, so the listing is empty for the administrator too and its absence for the reader
    // would prove nothing. The server's own tests list a tiled scan to one caller and not the
    // other.

    // The cave's page is theirs to read, with its survey models withheld as a whole — so the
    // placement row cannot be looked for there, and what is looked for instead is the page the
    // scan's facts are on: the upload's status page, which is refused to them as not found,
    // and which the client says so about rather than showing an empty card.
    // The cave page's figure panels ask for statistics a reader without the exact position is
    // refused; the browser logs each refusal as a failed resource, however the page handles it.
    consoleErrors.allow(
      /status of 40[34]/,
      "the cave page's figure panels request statistics the server withholds from a reader without the exact position",
    );
    await gotoRoute(theirs.page, `/caves/${caveId}`);
    await expect(theirs.page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
    await expect(theirs.page.getByText('3D survey models')).toBeVisible();
    // The model the administrator was shown by name is not on the reader's list at all.
    await expect(theirs.page.getByText('No 3D models yet')).toBeVisible();
    await gotoRoute(theirs.page, upload.statusPageUrl);
    const unavailable = theirs.page.getByTestId('survey-upload-unavailable');
    await expect(unavailable).toBeVisible({ timeout: 15_000 });
    await expect(theirs.page.getByTestId('point-cloud-card')).toHaveCount(0);
  } finally {
    await theirs.context.close();
  }
});

/** The east, north and up metres from one station to another, near enough for a few hundred metres. */
function offsetMetres(
  from: { longitude: number; latitude: number; altitudeM: number | null },
  to: { longitude: number; latitude: number; altitudeM: number | null },
): [number, number, number] {
  const metresPerDegree = 111_320;
  return [
    (to.longitude - from.longitude) * metresPerDegree * Math.cos((from.latitude * Math.PI) / 180),
    (to.latitude - from.latitude) * metresPerDegree,
    (to.altitudeM ?? 0) - (from.altitudeM ?? 0),
  ];
}

test('a scan is placed by control points on the cave line plot, its residuals shown and the pairs sent again', async ({
  page,
}) => {
  test.setTimeout(240_000);
  await login(page);
  const caveId = await caveWithEntrance(page, `E2E Scan Control ${Date.now()}`, false);

  // The cave's line plot, whose stations the scan is placed by: the upstream viewer's own demo
  // survey, which the repository carries as a fixture.
  const uploaded = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${await bearerToken(page)}` },
    multipart: {
      file: { name: 'P8_Master.3d', mimeType: 'application/octet-stream', buffer: readFileSync('e2e/fixtures/P8_Master.3d') },
    },
  });
  expect(uploaded.ok(), `the survey upload answered ${uploaded.status()}`).toBeTruthy();
  const linePlotId = ((await uploaded.json()) as { id: string }).id;
  await expect
    .poll(async () => (await asPerson<{ status: string }>(page, 'GET', `/api/v1/survey-models/${linePlotId}`)).status, {
      timeout: 90_000,
      message: 'the line plot was never read',
    })
    .toBe('ready');
  // The first line plot a cave receives is its current one, which is the one a scan is placed by.

  type Station = { name: string; longitude: number; latitude: number; altitudeM: number | null };
  const stations = (
    await asPerson<{ items: Station[] }>(page, 'GET', `/api/v1/survey-models/${linePlotId}/stations?pageSize=200`)
  ).items;
  // Two stations a sensible distance apart, so neither the scan points nor the stations are too close.
  const first = stations[0];
  const second = stations.find((s) => {
    const [e, n] = offsetMetres(first, s);
    const d = Math.hypot(e, n);
    return d > 5 && d < 300;
  });
  expect(second, 'the fixture has two stations between 5 and 300 m apart').toBeDefined();
  const [east, north, up] = offsetMetres(first, second!);

  const upload = await uploadLas(page, caveId, 'E2E control-point scan');
  expect(upload.status, JSON.stringify(upload)).toBe('succeeded');
  const modelId = upload.pointCloud!.modelId;

  // Placed by the two pairs over the API first: the scan's frame already north-aligned, so the
  // scan points are the stations' own offsets and the fit comes out close to exact.
  const placed = await asPerson<{ placementSource: string; placementControlPoints: { rmsM: number } | null }>(
    page,
    'PUT',
    `/api/v1/survey-models/${modelId}/scan-placement/control-points`,
    {
      pairs: [
        { scanX: 0, scanY: 0, scanZ: 0, station: first.name },
        { scanX: east, scanY: north, scanZ: up, station: second!.name },
      ],
      fitTilt: false,
    },
  );
  expect(placed.placementSource).toBe('controlPoints');
  expect(placed.placementControlPoints!.rmsM).toBeLessThan(0.5);

  // Canned tiles, as above: the model listing is rewritten to name a tileset, and it is served here.
  const tilesetUrl = `/api/v1/survey-models/${modelId}/tiles/tileset.json?token=e2e`;
  await page.route(`**/api/v1/caves/${caveId}/survey-models`, async (route) => {
    const answer = (await (await route.fetch()).json()) as { id: string; tilesetUrl: string | null }[];
    await route.fulfill({ json: answer.map((m) => (m.id === modelId ? { ...m, tilesetUrl } : m)) });
  });
  await page.route(`**/api/v1/survey-models/${modelId}/tiles/**`, async (route) => {
    if (route.request().url().includes('tileset.json')) {
      await route.fulfill({ contentType: 'application/json', body: tilesetJson });
    } else {
      await route.fulfill({ contentType: 'application/octet-stream', body: tinyPnts(200) });
    }
  });

  await gotoRoute(page, `/caves/${caveId}`);
  const state = page.getByTestId(`scan-placement-state-${modelId}`);
  await expect(state).toContainText('Placed by control points', { timeout: 15_000 });
  await expect(state).toContainText('RMS');

  // The placer opens on the pairs the scan was placed by, with their residuals.
  await page.getByTestId(`scan-place-points-${modelId}`).click();
  const panel = page.getByTestId('scan-control-points');
  await expect(panel).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('scan-control-station-0')).toHaveValue(first.name);
  await expect(page.getByTestId('scan-control-station-1')).toHaveValue(second!.name);
  await expect(page.getByTestId('scan-control-residuals').locator('tbody tr')).toHaveCount(2);
  await expect(page.getByTestId('point-cloud-surface')).toHaveAttribute('data-status', 'loaded', { timeout: 60_000 });

  // Picking arms the click on the cloud; the click itself needs a depth buffer the software
  // renderer does not answer reliably, so the pick is proved against the engine's double and
  // the browser proves the control, and that the pairs go to the server and come back fitted.
  await page.getByTestId('scan-control-pick-0').click();
  await expect(page.getByTestId('scan-control-pick-0')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('point-cloud-measure')).toBeDisabled();
  await page.getByTestId('scan-control-pick-0').click();

  const sent = page.waitForResponse(
    (response) =>
      response.url().includes(`/api/v1/survey-models/${modelId}/scan-placement/control-points`) &&
      response.request().method() === 'PUT',
  );
  await page.getByTestId('scan-control-save').click();
  expect((await sent).status()).toBe(200);
  await expect(page.getByText('The scan is placed by its control points.')).toBeVisible();
  await expect(page.getByTestId('scan-control-rms')).toContainText('m');
});

test('a placed scan is made into a surface for another cave, and the result points at that cave', async ({ page }) => {
  test.setTimeout(180_000);
  await login(page);
  const caveId = await caveWithEntrance(page, `E2E Surface scan ${Date.now()}`, false);
  const otherName = `E2E Surface walls ${Date.now()}`;
  const otherId = await caveWithEntrance(page, otherName, false);
  const upload = await uploadLas(page, caveId, 'E2E surface survey');
  expect(upload.status, JSON.stringify(upload)).toBe('succeeded');
  const modelId = upload.pointCloud!.modelId;
  await asPerson(page, 'PUT', `/api/v1/survey-models/${modelId}/scan-placement`, {
    longitude: 25.2093,
    latitude: 45.5187,
    heightM: 1200,
    headingDeg: 0,
  });

  // The browser lane has no worker, so the scan is never tiled and the server would refuse the
  // surface as having nothing to make it of. Both are canned: the upload's answer names a tileset,
  // and the request is answered as the server answers one it took — the upload queued again —
  // after which the upload's own answer (finished) stands for the run having saved the walls.
  // What this proves is the client's side: the action once placed, the cave chosen, the body sent
  // and the result pointed at; the run itself is proved by the server's tests.
  const tiled = { tilesetUrl: `/api/v1/survey-models/${modelId}/tiles/tileset.json`, tileBytes: 4096, tileCount: 1 };
  await page.route(`**/api/v1/survey-uploads/${upload.id}`, async (route) => {
    const answer = (await (await route.fetch()).json()) as UploadAnswer & { pointCloud: Record<string, unknown> };
    answer.pointCloud = { ...answer.pointCloud, ...tiled };
    await route.fulfill({ json: answer });
  });
  let sent: unknown = null;
  await page.route(`**/api/v1/survey-uploads/${upload.id}/surface`, async (route) => {
    sent = route.request().postDataJSON();
    await route.fulfill({
      status: 202,
      json: { ...upload, status: 'queued', pointCloud: { ...upload.pointCloud, ...tiled } },
    });
  });

  await gotoRoute(page, upload.statusPageUrl);
  const surface = page.getByTestId('scan-surface');
  await expect(surface).toBeVisible({ timeout: 15_000 });
  await expect(surface.getByTestId('scan-surface-make')).toBeEnabled({ timeout: 15_000 });
  await surface.getByTestId('scan-surface-make').click();

  const dialog = page.getByRole('dialog', { name: 'Make a surface of the scan' });
  await expect(dialog).toBeVisible();
  // The built-in that changes nothing is preselected for somebody who never made a surface.
  await expect(dialog.getByTestId('scan-surface-profile')).toContainText('Surface defaults');
  await dialog.getByText('Another cave').click();
  await expect(dialog.getByRole('button', { name: 'Make the surface' })).toBeDisabled();
  // The cave search opens from its button; its rows are matched on the name typed.
  await dialog.getByTestId('scan-surface-other-cave').click();
  await page.getByTestId('scan-surface-other-cave-input').fill(otherName);
  await page.getByTestId('scan-surface-other-cave-row').filter({ hasText: otherName }).first().click({ timeout: 15_000 });
  await dialog.getByRole('button', { name: 'Make the surface' }).click();

  await expect.poll(() => sent, { timeout: 15_000 }).not.toBeNull();
  expect((sent as { caveId: string }).caveId).toBe(otherId);
  expect((sent as { profileId: string | null }).profileId).not.toBeNull();
  const saved = page.getByTestId('scan-surface-saved');
  await expect(saved).toContainText(otherName, { timeout: 30_000 });
  await saved.getByTestId('scan-surface-open-cave').click();
  await expect(page).toHaveURL(new RegExp(`/caves/${otherId}$`));
});
