// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type BrowserContext, type Page } from '@playwright/test';
import { writeCogFixture } from '../src/scene3d/cogFixture.ts';
import { toMercator } from '../src/scene3d/renderedTiles3d.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, scene3dSurface, waitForScene3dReady } from './helpers.ts';
import { tintedPng } from './png.ts';

// Catalogue backgrounds the 3D scene draws itself — a cloud-optimised GeoTIFF read tile by tile
// from its own pyramid, and a vector background rendered in its style by the flat map's renderer —
// reaching the globe, in the 3D route and in the popped-out 3D window.
//
// What only a browser can show. The unit tests read a generated file through the real reader and
// check every pixel a tile is painted from; they run against a stand-in for the engine and for the
// map renderer, so they cannot show that the engine asks for the tiles, takes what it is handed and
// puts it on the ground, or that a style really renders into a tile. Here the real engine draws
// under software rendering and the picture is probed: the share of the scene's middle that is the
// background's colour.
//
// Nothing leaves the machine. The catalogue's answer is the installation's own plus rows this
// spec adds; the GeoTIFF, the shapes and the style are generated here and served by the page's own
// routes (the GeoTIFF honouring byte ranges, as any web server would); the default street map is
// answered with a flat tile of its own colour, so "the background changed" is a colour that changed.

/** The street map, as this spec serves it: a flat blue no GeoTIFF here is. */
const STREET_BLUE: [number, number, number] = [118, 168, 208];
/** The GeoTIFF's one colour. */
const GEOTIFF_MAGENTA: [number, number, number] = [220, 30, 200];
/** The vector style's fill: the shapes, not the style's background, which is white. */
const VECTOR_GREEN: [number, number, number] = [20, 200, 20];

const GEOTIFF_URL = '/e2e-scene3d/background.tif';
const UNREADABLE_URL = '/e2e-scene3d/unreadable.tif';
const SHAPES_URL = '/e2e-scene3d/shapes.geojson';
const STYLE_URL = '/e2e-scene3d/style.json';
const MISSING_STYLE_URL = '/e2e-scene3d/missing-style.json';

/**
 * Far from any id the installation's catalogue holds, so the rows added here never collide with
 * one of its own.
 */
const GEOTIFF_ID = 990_041;
const UNREADABLE_ID = 990_042;
const VECTOR_ID = 990_043;
const UNSTYLED_ID = 990_044;

/** One square over Romania and around it, to be filled by the style. */
const SHAPES = {
  type: 'FeatureCollection',
  features: [
    {
      type: 'Feature',
      properties: { name: 'E2E square' },
      geometry: {
        type: 'Polygon',
        coordinates: [[[20.5, 43.0], [30.5, 43.0], [30.5, 48.6], [20.5, 48.6], [20.5, 43.0]]],
      },
    },
  ],
};

/** A MapLibre style: a white background and the square filled green. No labels, so no fonts. */
const STYLE = {
  version: 8,
  sources: { shapes: { type: 'geojson', data: SHAPES_URL } },
  layers: [
    { id: 'background', type: 'background', paint: { 'background-color': '#ffffff' } },
    { id: 'square', type: 'fill', source: 'shapes', paint: { 'fill-color': `rgb(${VECTOR_GREEN.join(',')})` } },
  ],
};

/** A GeoTIFF over all of Romania and around it, in web mercator, with a pyramid down to 256 pixels. */
function backgroundGeoTiff(): Buffer {
  const [west, north] = toMercator(20.5, 48.6);
  return Buffer.from(
    writeCogFixture({
      width: 2048,
      height: 2048,
      tileSize: 256,
      bands: 3,
      origin: [west, north],
      pixelSize: [500, 500],
      epsg: 3857,
      overviews: 3,
      value: (band) => GEOTIFF_MAGENTA[band]!,
    }),
  );
}

function catalogueRow(id: number, name: string, url: string) {
  return {
    id,
    name,
    layerKind: 'cog',
    urlTemplate: url,
    wms: null,
    wmts: null,
    vector: null,
    cog: { bands: null, nodata: null, min: 0, max: 255, projection: 'EPSG:3857', projectionDefinition: null },
    attribution: '© E2E fixture',
    groupName: null,
    minZoom: 0,
    maxZoom: 19,
    isBase: true,
    isDefault: false,
    sortOrder: 9000 + id,
    inDocuments: false,
  };
}

/** A vector background over the shapes, in a style, standing in for itself in 3D by the street map. */
function vectorRow(id: number, name: string, style: string) {
  return {
    ...catalogueRow(id, name, SHAPES_URL),
    layerKind: 'vector',
    cog: null,
    vector: { format: 'geojson', style, styleSource: 'shapes', labelField: null, scene3dStandIn: 'OpenStreetMap' },
    attribution: '© E2E shapes',
  };
}

/** Serves bytes the way a web server serves a file: whole, or the range asked for. */
function serveRanged(bytes: Buffer) {
  return (route: Parameters<Parameters<BrowserContext['route']>[1]>[0]) => {
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
  };
}

/** Everything the scene reads from outside, answered locally, for every window of the context. */
async function serveBackgrounds(context: BrowserContext) {
  const street = tintedPng(256, STREET_BLUE);
  await context.route('**://tile.openstreetmap.org/**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'image/png',
      headers: { 'access-control-allow-origin': '*' },
      body: street,
    }),
  );
  await context.route(`**${GEOTIFF_URL}`, serveRanged(backgroundGeoTiff()));
  await context.route(`**${UNREADABLE_URL}`, (route) => route.fulfill({ status: 404, body: 'not here' }));
  await context.route(`**${SHAPES_URL}`, (route) => route.fulfill({ status: 200, json: SHAPES }));
  await context.route(`**${STYLE_URL}`, (route) => route.fulfill({ status: 200, json: STYLE }));
  await context.route(`**${MISSING_STYLE_URL}`, (route) => route.fulfill({ status: 404, body: 'not here' }));
  await context.route(/\/api\/v1\/map-layers(\?.*)?$/, async (route) => {
    const response = await route.fetch();
    const rows = (await response.json()) as unknown[];
    await route.fulfill({
      response,
      json: [
        ...rows,
        catalogueRow(GEOTIFF_ID, 'E2E GeoTIFF background', GEOTIFF_URL),
        catalogueRow(UNREADABLE_ID, 'E2E unreadable GeoTIFF', UNREADABLE_URL),
        vectorRow(VECTOR_ID, 'E2E vector background', STYLE_URL),
        vectorRow(UNSTYLED_ID, 'E2E vector without its style', MISSING_STYLE_URL),
      ],
    });
  });
}

/**
 * The share of the middle of the scene that is one colour, give or take a little: decoded in the
 * page, which has a PNG decoder, from a picture of what the browser composited. The middle only:
 * the corners hold the scene's controls and, when the layer list is open, the list.
 */
async function colourShare(page: Page, [red, green, blue]: readonly number[]): Promise<number> {
  const box = (await scene3dSurface(page).boundingBox())!;
  const picture = await page.screenshot({
    clip: { x: box.x + box.width * 0.3, y: box.y + box.height * 0.3, width: box.width * 0.4, height: box.height * 0.4 },
  });
  return page.evaluate(
    async ({ data, red, green, blue }) => {
      const bytes = Uint8Array.from(atob(data), (c) => c.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
      const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
      const context = canvas.getContext('2d')!;
      context.drawImage(bitmap, 0, 0);
      const pixels = context.getImageData(0, 0, bitmap.width, bitmap.height).data;
      let near = 0;
      for (let at = 0; at < pixels.length; at += 4) {
        if (
          Math.abs(pixels[at]! - red) <= 40 &&
          Math.abs(pixels[at + 1]! - green) <= 40 &&
          Math.abs(pixels[at + 2]! - blue) <= 40
        ) {
          near++;
        }
      }
      return near / (pixels.length / 4);
    },
    { data: picture.toString('base64'), red, green, blue },
  );
}

/** Polls the probe until most of the middle of the scene is the colour: the scene drew it. */
async function expectSceneMostly(page: Page, colour: readonly number[], what: string) {
  await expect
    .poll(() => colourShare(page, colour), { message: `the scene never showed ${what}`, timeout: 90_000, intervals: [1_000] })
    .toBeGreaterThan(0.6);
}

async function chooseBackground(page: Page, name: string) {
  await page.getByTestId('scene3d-layers-trigger').click();
  const panel = page.getByTestId('scene3d-layer-panel');
  await panel.getByRole('radio', { name }).check();
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();
}

test.describe('catalogue backgrounds the 3D scene draws itself', () => {
  test.slow();

  test('a GeoTIFF background replaces the street map on the globe', async ({ page }) => {
    await serveBackgrounds(page.context());
    await login(page);
    await page.goto('about:blank');
    await page.goto('/map3d#3d/45.53000/25.30000/30000/0.0/-90.0');
    await waitForScene3dReady(page, 60_000);

    // The street map first, so the probe is known to see the ground at all.
    await expectSceneMostly(page, STREET_BLUE, 'the street map');
    expect(await colourShare(page, GEOTIFF_MAGENTA)).toBeLessThan(0.05);

    const asked: string[] = [];
    page.on('request', (request) => {
      if (request.url().endsWith(GEOTIFF_URL)) asked.push(request.headers().range ?? 'whole file');
    });
    await chooseBackground(page, 'E2E GeoTIFF background');
    await expectSceneMostly(page, GEOTIFF_MAGENTA, 'the GeoTIFF');
    // Read by ranges, never fetched whole: what makes a county-sized file affordable.
    expect(asked.length).toBeGreaterThan(0);
    expect(asked.every((range) => range.startsWith('bytes='))).toBe(true);
    // Its credit is shown, as every background's is.
    await expect(page.locator('.cesium-widget-credits')).toContainText('© E2E fixture');
  });

  test('a GeoTIFF that cannot be read is said to be so, and the globe keeps a background', async ({
    page,
    consoleErrors,
  }) => {
    consoleErrors.allow(
      /Failed to load resource: .*404/,
      'the unreadable GeoTIFF this test adds to the catalogue is answered 404 on purpose',
    );
    await serveBackgrounds(page.context());
    await login(page);
    await page.goto('about:blank');
    await page.goto('/map3d#3d/45.53000/25.30000/30000/0.0/-90.0');
    await waitForScene3dReady(page, 60_000);
    await expectSceneMostly(page, STREET_BLUE, 'the street map');

    await page.getByTestId('scene3d-layers-trigger').click();
    const panel = page.getByTestId('scene3d-layer-panel');
    // Clicked rather than checked: the scene can find the file unreadable and fall back before a
    // check would have looked at the radio again.
    await panel.getByRole('radio', { name: 'E2E unreadable GeoTIFF' }).click();

    // Not left as a chosen background that draws nothing: the reason under it, the choice no
    // longer offered, and the scene back on the installation's default.
    await expect(page.getByTestId(`scene3d-layer-note-${UNREADABLE_ID}`)).toContainText(
      'Not drawn in 3D. This GeoTIFF could not be read',
      { timeout: 30_000 },
    );
    await expect(panel.getByRole('radio', { name: 'E2E unreadable GeoTIFF' })).toBeDisabled();
    await expect(panel.getByRole('radio', { name: 'OpenStreetMap', exact: true })).toBeChecked();
    await page.keyboard.press('Escape');
    await expectSceneMostly(page, STREET_BLUE, 'the street map again');
  });

  test('a vector background is drawn in its style on the globe', async ({ page }) => {
    await serveBackgrounds(page.context());
    await login(page);
    await page.goto('about:blank');
    await page.goto('/map3d#3d/45.53000/25.30000/30000/0.0/-90.0');
    await waitForScene3dReady(page, 60_000);
    await expectSceneMostly(page, STREET_BLUE, 'the street map');

    await chooseBackground(page, 'E2E vector background');
    // The square's fill, which only the style paints: the stand-in would have stayed blue, and the
    // plain default look is not green.
    await expectSceneMostly(page, VECTOR_GREEN, 'the vector background in its style');
    await expect(page.locator('.cesium-widget-credits')).toContainText('© E2E shapes');
    await page.getByTestId('scene3d-layers-trigger').click();
    await expect(page.getByTestId(`scene3d-layer-note-${VECTOR_ID}`)).toHaveCount(0);
  });

  test('a vector background whose style cannot be loaded is drawn by its stand-in, and says so', async ({
    page,
    consoleErrors,
  }) => {
    consoleErrors.allow(
      /Failed to load resource: .*404/,
      'the style this test names for one vector background is answered 404 on purpose',
    );
    await serveBackgrounds(page.context());
    await login(page);
    await page.goto('about:blank');
    await page.goto('/map3d#3d/45.53000/25.30000/30000/0.0/-90.0');
    await waitForScene3dReady(page, 60_000);
    await expectSceneMostly(page, STREET_BLUE, 'the street map');

    await chooseBackground(page, 'E2E vector without its style');
    await page.getByTestId('scene3d-layers-trigger').click();
    await expect(page.getByTestId(`scene3d-layer-note-${UNSTYLED_ID}`)).toHaveText('Drawn in 3D as OpenStreetMap.', {
      timeout: 30_000,
    });
    // Still the chosen background: the stand-in is drawn under its name.
    await expect(
      page.getByTestId('scene3d-layer-panel').getByRole('radio', { name: 'E2E vector without its style' }),
    ).toBeChecked();
    await page.keyboard.press('Escape');
    await expectSceneMostly(page, STREET_BLUE, 'the stand-in');
  });

  test('the popped-out 3D window draws the same GeoTIFF and vector backgrounds', async ({ page }) => {
    await serveBackgrounds(page.context());
    await login(page);
    await gotoRoute(page, '/map');
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });

    const opened = page.waitForEvent('popup');
    await page.getByTestId('map-popout-scene3d').click();
    const popout = await opened;
    await waitForScene3dReady(popout, 60_000);
    await popout.getByTestId('scene3d-preset-top').click();
    await expectSceneMostly(popout, STREET_BLUE, 'the street map in the pop-out');

    await chooseBackground(popout, 'E2E GeoTIFF background');
    await expectSceneMostly(popout, GEOTIFF_MAGENTA, 'the GeoTIFF in the pop-out');
    await chooseBackground(popout, 'E2E vector background');
    await expectSceneMostly(popout, VECTOR_GREEN, 'the vector background in the pop-out');
  });
});
