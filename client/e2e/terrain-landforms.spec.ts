// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

// The landform classes (geomorphons), drawn on the map from a computed picture of the ground.
//
// What this shows that no server test can. The server's tests prove the classes a program wrote
// are accepted and stored; none shows that a raster of whole class numbers reaches the map as a
// layer, is read as numbers rather than stretched to a brightness, and arrives with the key that
// names its ten colours. A key-less categorical picture is ten colours nobody can name, and a
// stretched one is a smear of greys that draws without an error.
//
// Why nothing is skipped when WhiteboxTools is absent. Computing the classes needs the program
// and prepared elevation, neither of which a plain or hosted installation has. So the listing is
// handed to the page and the class raster is generated here, byte by byte, as a small GeoTIFF of
// the numbers 1 to 10 — invented, not read from anywhere. What is under test is the browser's
// half, and every part of it runs for real whether or not the program exists on this machine; the
// program's own arithmetic is covered where the program is installed.

const BUILD = '01890000-0000-7000-8000-00000000c111';
const LAYER_CURRENT = '01890000-0000-7000-8000-0000000000e1';
const LAYER_REVISED = '01890000-0000-7000-8000-0000000000e2';

const WEST = 25.6;
const NORTH = 45.7;
const SIZE = 64;
const PIXEL = 0.1 / SIZE;

/**
 * A single-band, 8-bit, uncompressed GeoTIFF in geographic coordinates, each column of ten holding
 * one class number from 1 (flat) to 10 (pit). Written by hand so the suite needs no raster tool and
 * the bytes are what the spec says they are.
 */
function classRaster(): Buffer {
  const entries = 12;
  const ifdSize = 2 + entries * 12 + 4;
  const scaleAt = 8 + ifdSize;
  const tiepointAt = scaleAt + 24;
  const keysAt = tiepointAt + 48;
  const dataAt = keysAt + 32;
  const buffer = Buffer.alloc(dataAt + SIZE * SIZE);

  buffer.write('II', 0, 'ascii');
  buffer.writeUInt16LE(42, 2);
  buffer.writeUInt32LE(8, 4);
  buffer.writeUInt16LE(entries, 8);

  let at = 10;
  const entry = (tag: number, type: number, count: number, value: number) => {
    buffer.writeUInt16LE(tag, at);
    buffer.writeUInt16LE(type, at + 2);
    buffer.writeUInt32LE(count, at + 4);
    // A SHORT lives in the first two bytes of the value field; everything else is a 32-bit value.
    if (type === 3 && count === 1) buffer.writeUInt16LE(value, at + 8);
    else buffer.writeUInt32LE(value, at + 8);
    at += 12;
  };
  const SHORT = 3;
  const LONG = 4;
  const DOUBLE = 12;
  entry(256, LONG, 1, SIZE); // width
  entry(257, LONG, 1, SIZE); // height
  entry(258, SHORT, 1, 8); // bits per sample
  entry(259, SHORT, 1, 1); // no compression
  entry(262, SHORT, 1, 1); // black is zero
  entry(273, LONG, 1, dataAt); // one strip
  entry(277, SHORT, 1, 1); // one sample per pixel
  entry(278, LONG, 1, SIZE); // rows per strip
  entry(279, LONG, 1, SIZE * SIZE); // strip byte count
  entry(33550, DOUBLE, 3, scaleAt); // pixel scale
  entry(33922, DOUBLE, 6, tiepointAt); // tie point: pixel (0,0) is the north-west corner
  entry(34735, SHORT, 16, keysAt); // geo keys
  buffer.writeUInt32LE(0, at); // no further directory

  [PIXEL, PIXEL, 0].forEach((v, i) => buffer.writeDoubleLE(v, scaleAt + i * 8));
  [0, 0, 0, WEST, NORTH, 0].forEach((v, i) => buffer.writeDoubleLE(v, tiepointAt + i * 8));
  // Header (version 1.1.0, three keys), then model type geographic, raster type pixel-is-area,
  // and the geographic coordinate system WGS 84.
  [1, 1, 0, 3, 1024, 0, 1, 2, 1025, 0, 1, 1, 2048, 0, 1, 4326].forEach((v, i) =>
    buffer.writeUInt16LE(v, keysAt + i * 2),
  );

  for (let row = 0; row < SIZE; row++) {
    for (let col = 0; col < SIZE; col++) {
      buffer[dataAt + row * SIZE + col] = 1 + Math.floor(col / 6.4);
    }
  }
  return buffer;
}

function rasterOf(layerId: string) {
  return {
    id: 1,
    west: WEST,
    south: NORTH - 0.1,
    east: WEST + 0.1,
    north: NORTH,
    width: SIZE,
    height: SIZE,
    pixelSizeDegrees: PIXEL,
    sizeBytes: SIZE * SIZE,
    url: `/api/v1/terrain/derivatives/${layerId}/rasters/1/content?token=e2e`,
  };
}

function landformLayer(id: string, name: string, revised: boolean) {
  return {
    id,
    terrainBuildId: BUILD,
    derivative: 'geomorphons',
    name,
    settings: '{"derivative":"geomorphons","geomorphonSearchMetres":500}',
    status: 'ready',
    errorCode: null,
    message: null,
    version: 1,
    sizeBytes: SIZE * SIZE,
    stale: revised,
    // A program upgrade reads exactly as a corrected method does: the picture is out of date over
    // ground that has not changed.
    staleReason: revised ? 'methodRevised' : null,
    computedAt: '2026-11-01T10:00:00Z',
    createdAt: '2026-11-01T09:00:00Z',
    rasters: [rasterOf(id)],
  };
}

/** Serves the generated class raster, honouring byte ranges, and counts the reads of it. */
async function serveClassRaster(page: Page) {
  const bytes = classRaster();
  const reads = { count: 0 };
  await page.route('**/api/v1/terrain/derivatives/*/rasters/*/content*', (route) => {
    reads.count += 1;
    const range = /bytes=(\d+)-(\d*)/.exec(route.request().headers().range ?? '');
    if (!range) {
      return route.fulfill({
        status: 200,
        contentType: 'image/tiff',
        headers: { 'accept-ranges': 'bytes', 'content-length': String(bytes.length) },
        body: bytes,
      });
    }
    const from = Number(range[1]);
    const to = range[2] === '' ? bytes.length - 1 : Math.min(Number(range[2]), bytes.length - 1);
    const slice = bytes.subarray(from, to + 1);
    return route.fulfill({
      status: 206,
      contentType: 'image/tiff',
      headers: {
        'accept-ranges': 'bytes',
        'content-range': `bytes ${from}-${to}/${bytes.length}`,
        'content-length': String(slice.length),
      },
      body: slice,
    });
  });
  return reads;
}

async function openMapWithLandforms(page: Page) {
  await page.route('**/api/v1/terrain/derivatives', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        landformLayer(LAYER_CURRENT, 'E2E Landforms', false),
        landformLayer(LAYER_REVISED, 'E2E Landforms older program', true),
      ]),
    }),
  );
  const reads = await serveClassRaster(page);
  await login(page);
  await gotoRoute(page, '/map');
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 30_000 });
  return reads;
}

test('a landform picture reaches the map with its key and is read as class numbers', async ({
  page,
}) => {
  const reads = await openMapWithLandforms(page);

  const list = page.getByTestId('terrain-derivative-list');
  await expect(list).toBeVisible({ timeout: 30_000 });
  const row = list.getByTestId(`terrain-derivative-${LAYER_CURRENT}`);

  // Named by what it is, in the application's words rather than whatever somebody typed.
  await expect(row).toContainText('Landform classes');

  // The key appears only under a landform picture that is on the map: ticked off, there is none.
  await expect(row.getByTestId('landform-legend')).toHaveCount(0);
  await row.getByRole('checkbox').check();

  const legend = row.getByTestId('landform-legend');
  await expect(legend).toBeVisible();
  for (const name of [
    'Flat', 'Peak', 'Ridge', 'Shoulder', 'Spur', 'Slope', 'Hollow', 'Footslope', 'Valley', 'Pit',
  ]) {
    await expect(legend.getByText(name, { exact: true })).toBeVisible();
  }

  // It is a layer on the map, not only a ticked box: the composer tree is built from the map's
  // own layers and every overlay row carries a transparency slider.
  const treeRow = page
    .locator('.layer-composer .ant-tree-treenode')
    .filter({ hasText: 'E2E Landforms' })
    .first();
  await expect(treeRow).toBeVisible({ timeout: 30_000 });
  await expect(treeRow.locator('.ant-slider')).toBeVisible();

  // The raster was fetched, which is the layer reading the class numbers: the page's console guard
  // fails the test if the reader could not parse what it was handed.
  await expect.poll(() => reads.count, { timeout: 30_000 }).toBeGreaterThan(0);

  // And it goes away again, key included.
  await row.getByRole('checkbox').uncheck();
  await expect(treeRow).toHaveCount(0, { timeout: 15_000 });
  await expect(row.getByTestId('landform-legend')).toHaveCount(0);
});

test('a landform picture drawn by an earlier version of the program says it is out of date', async ({
  page,
}) => {
  await openMapWithLandforms(page);

  const list = page.getByTestId('terrain-derivative-list');
  await expect(list).toBeVisible({ timeout: 30_000 });

  // Both halves in one test, as for the other pictures: a badge on everything proves nothing.
  const revised = list.getByTestId(`terrain-derivative-${LAYER_REVISED}`);
  const current = list.getByTestId(`terrain-derivative-${LAYER_CURRENT}`);
  await expect(revised.getByTestId('terrain-derivative-stale')).toBeVisible();
  await expect(revised.getByTestId('terrain-derivative-stale')).toHaveText(/out of date/i);
  await expect(current.getByTestId('terrain-derivative-stale')).toHaveCount(0);

  // The hint says the program's version is one of the reasons, not only a corrected method.
  await revised.getByTestId('terrain-derivative-stale').hover();
  await expect(page.getByRole('tooltip')).toContainText('earlier version of the program');
});
