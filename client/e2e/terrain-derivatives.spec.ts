// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');

// The shaded and coloured pictures of the ground, on the map a reader actually looks at.
//
// What this file shows that no server test can. The server's own tests prove the arithmetic and
// the bookkeeping: that a slope over a known plane is the angle it should be, that a picture
// records the elevation it came from, and that one drawn from a build which is no longer the
// active one is reported as out of date. None of them can show that the picture arrives on the
// map at all, or — the part that matters more — that a reader looking at a stale one is told so
// where they are looking. A shaded relief drawn from superseded elevation is still worth drawing,
// and is dangerous only when nothing says it is stale: the reader sees hillshade that disagrees
// with the heights beneath it and concludes the cave data is wrong.
//
// Why the listing is supplied rather than built. Computing one needs prepared elevation, which is
// an operator's job needing network, disk and hours; a plain installation has none. So the layers
// are handed to the page and the rasters are served from a small GeoTIFF this suite already
// carries. What is under test here is the browser's half — the panel, the badge, and the layer
// reaching the map — and every part of that is exercised for real.

const BUILD_CURRENT = '01890000-0000-7000-8000-00000000c111';
const BUILD_SUPERSEDED = '01890000-0000-7000-8000-00000000d222';
const LAYER_CURRENT = '01890000-0000-7000-8000-0000000000a1';
const LAYER_STALE = '01890000-0000-7000-8000-0000000000b2';

function rasterOf(layerId: string) {
  return {
    id: 1,
    west: 25.6,
    south: 45.6,
    east: 25.7,
    north: 45.7,
    width: 64,
    height: 64,
    pixelSizeDegrees: 0.0015625,
    sizeBytes: 4096,
    url: `/api/v1/terrain/derivatives/${layerId}/rasters/1/content?token=e2e`,
  };
}

function derivativeListing() {
  return [
    {
      id: LAYER_CURRENT,
      terrainBuildId: BUILD_CURRENT,
      derivative: 'hillshade',
      name: 'E2E Shaded relief',
      settings: '{"derivative":"hillshade"}',
      status: 'ready',
      errorCode: null,
      message: null,
      version: 1,
      sizeBytes: 4096,
      stale: false,
      computedAt: '2026-09-01T10:00:00Z',
      createdAt: '2026-09-01T09:00:00Z',
      rasters: [rasterOf(LAYER_CURRENT)],
    },
    {
      id: LAYER_STALE,
      terrainBuildId: BUILD_SUPERSEDED,
      derivative: 'slope',
      name: 'E2E Steepness',
      settings: '{"derivative":"slope"}',
      status: 'ready',
      errorCode: null,
      message: null,
      version: 1,
      sizeBytes: 4096,
      stale: true,
      computedAt: '2026-08-01T10:00:00Z',
      createdAt: '2026-08-01T09:00:00Z',
      rasters: [rasterOf(LAYER_STALE)],
    },
  ];
}

/**
 * Serves the fixture raster, honouring byte ranges.
 *
 * A tile reader asks for pieces of a raster rather than the whole of it, and answering a ranged
 * request with the entire file and a plain 200 is the one way this stub could differ from the
 * server in a way that matters: the reader would parse the wrong bytes and report an error that
 * belongs to the stub rather than to the application.
 */
async function serveFixtureRaster(page: Page) {
  const bytes = readFileSync(path.join(fixtures, 'e2e-map.tif'));
  await page.route('**/api/v1/terrain/derivatives/*/rasters/*/content*', (route) => {
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
}

async function openMapWithDerivatives(page: Page) {
  await page.route('**/api/v1/terrain/derivatives', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(derivativeListing()),
    }),
  );
  await serveFixtureRaster(page);
  await login(page);
  await gotoRoute(page, '/map');
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 30_000 });
}

test('a computed picture of the ground reaches the map when it is switched on', async ({
  page,
}) => {
  await openMapWithDerivatives(page);

  const list = page.getByTestId('terrain-derivative-list');
  await expect(list).toBeVisible({ timeout: 30_000 });

  // Off until asked for: a picture of the ground is an overlay somebody chooses, not something
  // that arrives over the map on its own.
  //
  // Scoped to the list rather than matched by name across the page. Once the layer is on the map
  // the composer tree grows a row carrying the same name and its own checkbox, and that tree is
  // drawn above this list, so a by-name match would silently move from this checkbox to the
  // tree's as soon as the layer appeared. Unticking the tree's only hides a layer, which would
  // leave the removal assertion below testing a control that cannot remove anything.
  const current = list.getByTestId(`terrain-derivative-${LAYER_CURRENT}`).getByRole('checkbox');
  await expect(current).not.toBeChecked();
  await current.check();

  // It is on the map, not merely ticked in a list. The composer tree is built from the map's own
  // layers, so a row there is the layer existing; every active overlay row carries a transparency
  // slider, which is what makes it an overlay rather than a label.
  const treeRow = page
    .locator('.layer-composer .ant-tree-treenode')
    .filter({ hasText: 'E2E Shaded relief' })
    .first();
  await expect(treeRow).toBeVisible({ timeout: 30_000 });
  await expect(treeRow.locator('.ant-slider')).toBeVisible();

  // And it goes away again, rather than being left behind by a pass that only ever adds.
  await current.uncheck();
  await expect(treeRow).toHaveCount(0, { timeout: 15_000 });
});

test('a picture drawn from elevation that has since been replaced says so where it is read', async ({
  page,
}) => {
  await openMapWithDerivatives(page);

  const list = page.getByTestId('terrain-derivative-list');
  await expect(list).toBeVisible({ timeout: 30_000 });

  // The stale one is marked, and the current one is not. Both halves in one test: a badge that
  // appeared on everything would pass an assertion that only looked at the stale row, and would
  // tell a reader nothing at all.
  const stale = list.getByTestId(`terrain-derivative-${LAYER_STALE}`);
  const current = list.getByTestId(`terrain-derivative-${LAYER_CURRENT}`);
  await expect(stale.getByTestId('terrain-derivative-stale')).toBeVisible();
  await expect(current.getByTestId('terrain-derivative-stale')).toHaveCount(0);

  // The words, not just the marker: the badge has to say what is wrong with the picture.
  await expect(stale.getByTestId('terrain-derivative-stale')).toHaveText(/out of date/i);

  // Marked and still drawable — withdrawing it would leave bare map over ground that has probably
  // not moved, and the reader would lose a picture rather than gain a warning.
  await stale.getByRole('checkbox').check();
  await expect(
    page.locator('.layer-composer .ant-tree-treenode').filter({ hasText: 'E2E Steepness' }).first(),
  ).toBeVisible({ timeout: 30_000 });
});
