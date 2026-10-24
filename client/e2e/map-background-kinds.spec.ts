// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page, type Route } from '@playwright/test';
import { writeArrayBuffer } from 'geotiff';
import { PMTiles, type RangeResponse, type Source } from 'pmtiles';
import proj4 from 'proj4';
import { test } from './consoleGuard.ts';
import { gotoRoute, login, mapAddress, pixelOnMap } from './helpers.ts';

// The two new kinds of map background, drawn by the real client from catalogue entries that point
// at a fixture server of the test's own — no request leaves the machine. The catalogue answer is
// the server's own, with these entries added to it, so everything else on the page is as shipped.
//
// The administration page's download is driven against answers the test gives too: the job the
// server would run is the subject of the API's own tests; this is what an administrator sees.

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');
const FIXTURE_HOST = 'https://e2e-fixtures.silexgis.invalid';

// The installation's stated Stereo 70 definition, as the server sends it with a GeoTIFF entry.
const STEREO_70 =
  '+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +x_0=500000 +y_0=500000 +ellps=krass ' +
  '+towgs84=2.329,-147.042,-92.08,-0.309,0.325,0.497,5.69 +units=m +no_defs';

const CAMERA = { lat: 46.57, lon: 22.7, zoom: 12 };

/** A picture of solid red, 40 pixels of 50 m in Stereo 70, its top-left corner at a point given in degrees. */
function stereo70Square(corner: { lon: number; lat: number }): Buffer {
  const [east, north] = proj4('EPSG:4326', STEREO_70, [corner.lon, corner.lat]);
  const band = (value: number) => Array.from({ length: 40 }, () => Array.from({ length: 40 }, () => value));
  // The writer's type names fewer GeoKeys than it writes; the linear unit is one it leaves out.
  const metadata = {
    width: 40,
    height: 40,
    ModelPixelScale: [50, 50, 0],
    ModelTiepoint: [0, 0, 0, east, north, 0],
    GTModelTypeGeoKey: 1,
    GTRasterTypeGeoKey: 1,
    ProjectedCSTypeGeoKey: 3844,
    ProjLinearUnitsGeoKey: 9001,
  } as Parameters<typeof writeArrayBuffer>[1];
  return Buffer.from(writeArrayBuffer([band(220), band(30), band(30)], metadata));
}

/** Answers a request for a file the way a static server does: whole, or the byte range asked for. */
async function serveBytes(route: Route, bytes: Buffer, contentType: string) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Expose-Headers': 'Content-Range, Content-Length, ETag',
    'Accept-Ranges': 'bytes',
    'Content-Type': contentType,
  };
  const range = /bytes=(\d+)-(\d*)/.exec(route.request().headers()['range'] ?? '');
  if (!range) {
    await route.fulfill({ status: 200, headers, body: bytes });
    return;
  }
  const start = Number(range[1]);
  const end = Math.min(range[2] ? Number(range[2]) : bytes.length - 1, bytes.length - 1);
  await route.fulfill({
    status: 206,
    headers: { ...headers, 'Content-Range': `bytes ${start}-${end}/${bytes.length}` },
    body: bytes.subarray(start, end + 1),
  });
}

/** The archive, read from disk by the same library the client uses — what serves its tiles as z/x/y. */
class FileSource implements Source {
  private readonly bytes: Buffer;
  constructor(bytes: Buffer) {
    this.bytes = bytes;
  }
  getKey() {
    return 'osm-apuseni-basemap.pmtiles';
  }
  async getBytes(offset: number, length: number): Promise<RangeResponse> {
    const slice = this.bytes.subarray(offset, offset + length);
    return { data: slice.buffer.slice(slice.byteOffset, slice.byteOffset + slice.byteLength) as ArrayBuffer };
  }
}

/** Starts the fixture server and records every request it answers, by path. */
async function serveFixtures(page: Page): Promise<string[]> {
  const served: string[] = [];
  const archive = readFileSync(path.join(fixtures, 'osm-apuseni-basemap.pmtiles'));
  const tiles = new PMTiles(new FileSource(archive));
  const square = stereo70Square({ lon: 22.69, lat: 46.58 });
  const boundary = readFileSync(path.join(fixtures, 'e2e-natura.geojson'));
  await page.route(`${FIXTURE_HOST}/**`, async (route) => {
    const url = new URL(route.request().url());
    served.push(url.pathname);
    const tile = /^\/tiles\/(\d+)\/(\d+)\/(\d+)\.pbf$/.exec(url.pathname);
    if (tile) {
      const found = await tiles.getZxy(Number(tile[1]), Number(tile[2]), Number(tile[3]));
      await route.fulfill({
        status: found ? 200 : 204,
        headers: { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/x-protobuf' },
        body: found ? Buffer.from(found.data) : '',
      });
    } else if (url.pathname === '/basemap.pmtiles') {
      await serveBytes(route, archive, 'application/octet-stream');
    } else if (url.pathname === '/stereo70.tif') {
      await serveBytes(route, square, 'image/tiff');
    } else if (url.pathname === '/natura.geojson') {
      await serveBytes(route, boundary, 'application/geo+json');
    } else {
      await route.fulfill({ status: 404, headers: { 'Access-Control-Allow-Origin': '*' }, body: '' });
    }
  });
  return served;
}

const VECTOR_STYLE = {
  style: '/map-styles/protomaps/light.json',
  styleSource: 'protomaps',
  labelField: 'name:{lang}',
  scene3dStandIn: null,
};

/** The catalogue's own answer, with the fixture entries added to it. */
async function addEntries(page: Page) {
  await page.route('**/api/v1/map-layers', async (route) => {
    const response = await route.fetch();
    const shipped = (await response.json()) as Record<string, unknown>[];
    const entry = (id: number, name: string, over: Record<string, unknown>) => ({
      id,
      name,
      layerKind: 'xyz',
      urlTemplate: '',
      wms: null,
      wmts: null,
      cog: null,
      vector: null,
      attribution: 'E2E fixture',
      groupName: null,
      minZoom: 0,
      maxZoom: 13,
      isBase: true,
      isDefault: false,
      sortOrder: 1,
      inDocuments: false,
      ...over,
    });
    await route.fulfill({
      response,
      json: [
        entry(990001, 'E2E vector tiles', {
          layerKind: 'vector',
          urlTemplate: `${FIXTURE_HOST}/tiles/{z}/{x}/{y}.pbf`,
          vector: { format: 'mvt', ...VECTOR_STYLE },
          attribution: '© OpenStreetMap contributors',
        }),
        entry(990002, 'E2E PMTiles archive', {
          layerKind: 'vector',
          urlTemplate: `${FIXTURE_HOST}/basemap.pmtiles`,
          vector: { format: 'pmtiles', ...VECTOR_STYLE },
          attribution: '© OpenStreetMap contributors',
        }),
        entry(990003, 'E2E Stereo 70 GeoTIFF', {
          layerKind: 'cog',
          urlTemplate: `${FIXTURE_HOST}/stereo70.tif`,
          cog: { bands: null, nodata: null, min: null, max: null, projection: 'EPSG:3844', projectionDefinition: STEREO_70 },
          maxZoom: 19,
        }),
        entry(990004, 'E2E boundary', {
          isBase: false,
          layerKind: 'vector',
          urlTemplate: `${FIXTURE_HOST}/natura.geojson`,
          vector: { format: 'geojson', style: null, styleSource: null, labelField: null, scene3dStandIn: null },
        }),
        ...shipped,
      ],
    });
  });
}

/** The colour the map shows at a place, read off a picture of the page. */
async function colourAt(page: Page, lat: number, lon: number): Promise<number[]> {
  const point = await pixelOnMap(page, CAMERA, lat, lon);
  const picture = await page.screenshot({ clip: { x: point.x - 2, y: point.y - 2, width: 5, height: 5 } });
  return page.evaluate(async (base64: string) => {
    const bitmap = await createImageBitmap(await (await fetch(`data:image/png;base64,${base64}`)).blob());
    const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    const context = canvas.getContext('2d')!;
    context.drawImage(bitmap, 0, 0);
    const colour = [...context.getImageData(2, 2, 1, 1).data.subarray(0, 3)];
    bitmap.close();
    return colour;
  }, picture.toString('base64'));
}

/** How many different colours the map's middle shows: a drawn background has many, a blank one few. */
async function coloursInTheMiddle(page: Page): Promise<number> {
  const box = (await page.locator('.ol-viewport').boundingBox())!;
  const picture = await page.screenshot({
    clip: { x: box.x + box.width / 2 - 100, y: box.y + box.height / 2 - 100, width: 200, height: 200 },
  });
  return page.evaluate(async (base64: string) => {
    const bitmap = await createImageBitmap(await (await fetch(`data:image/png;base64,${base64}`)).blob());
    const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    const context = canvas.getContext('2d')!;
    context.drawImage(bitmap, 0, 0);
    const pixels = new Uint32Array(context.getImageData(0, 0, bitmap.width, bitmap.height).data.buffer);
    bitmap.close();
    return new Set(pixels).size;
  }, picture.toString('base64'));
}

async function chooseBackground(page: Page, name: string) {
  await page.getByRole('radio', { name, exact: true }).check();
}

test.describe('GeoTIFF and vector backgrounds on the map', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('draws vector tiles, a PMTiles archive, a GeoJSON overlay and a Stereo 70 GeoTIFF where it belongs', async ({
    page,
  }) => {
    const served = await serveFixtures(page);
    await addEntries(page);
    await page.goto(mapAddress(CAMERA));
    await expect(page.getByText('Base layers')).toBeVisible({ timeout: 30_000 });

    // Nothing is asked of a background nobody has chosen.
    expect(served).toEqual([]);

    // Vector tiles on a z/x/y template, drawn in the shipped light style: the tiles of the view
    // are asked for — 512-pixel tiles, one level below the map's zoom — and the map shows a
    // drawing rather than a blank.
    await chooseBackground(page, 'E2E vector tiles');
    await expect.poll(() => served.filter((p) => p.startsWith('/tiles/11/')).length, { timeout: 30_000 }).toBeGreaterThan(0);
    await expect.poll(() => coloursInTheMiddle(page), { timeout: 30_000 }).toBeGreaterThan(20);

    // The same tiles out of one archive, read with range requests.
    await chooseBackground(page, 'E2E PMTiles archive');
    await expect.poll(() => served.filter((p) => p === '/basemap.pmtiles').length, { timeout: 30_000 }).toBeGreaterThan(1);
    await expect.poll(() => coloursInTheMiddle(page), { timeout: 30_000 }).toBeGreaterThan(20);

    // A GeoTIFF in Romania's national grid, placed by the definition the catalogue sends with it:
    // its red square is drawn over the square's own ground and nowhere else.
    await chooseBackground(page, 'E2E Stereo 70 GeoTIFF');
    await expect.poll(() => served.includes('/stereo70.tif'), { timeout: 30_000 }).toBe(true);
    await expect
      .poll(async () => {
        const [red, green, blue] = await colourAt(page, 46.571, 22.703);
        return red > 180 && green < 90 && blue < 90;
      }, { timeout: 30_000 })
      .toBe(true);
    const [, outsideGreen] = await colourAt(page, 46.55, 22.66);
    expect(outsideGreen, 'red was drawn outside the GeoTIFF’s own square').toBeGreaterThan(90);

    // A GeoJSON overlay over it.
    await page.getByRole('checkbox', { name: 'E2E boundary' }).check();
    await expect.poll(() => served.includes('/natura.geojson'), { timeout: 30_000 }).toBe(true);
  });
});

test.describe('the installation’s own basemap, downloaded by an administrator', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('weighs a region, downloads it and shows it served — a download the test plays out', async ({ page }) => {
    const region = { west: 20.2, south: 43.6, east: 29.8, north: 48.3 };
    const presets = [
      { key: 'romania', region },
      { key: 'apuseni', region: { west: 22.2, south: 46.2, east: 23.4, north: 47 } },
    ];
    const job = {
      id: 41,
      region,
      maxZoom: 15,
      build: null as string | null,
      errorCode: null,
      error: null,
      createdAt: '2026-10-24T10:00:00Z',
      startedAt: null as string | null,
      completedAt: null as string | null,
    };
    // What the server answers, in the order a real download goes through it.
    const states = [
      { state: 'queued', percent: 0 },
      { state: 'running', percent: 45, build: '20261024', startedAt: '2026-10-24T10:00:02Z' },
      { state: 'succeeded', percent: 100, build: '20261024', startedAt: '2026-10-24T10:00:02Z', completedAt: '2026-10-24T10:02:10Z' },
    ];
    let started = false;
    let polls = 0;
    const served = { sourceId: 'basemap-20261024-20261024100210', build: '20261024', extractedAt: '2026-10-24T10:02:10Z', sizeBytes: 1_300_000_000, region, maxZoom: 15 };
    const status = () => {
      const stage = started ? states[Math.min(polls++, states.length - 1)] : null;
      return {
        selfHosted: true,
        current: stage?.state === 'succeeded' ? served : null,
        download: stage ? { ...job, ...stage } : null,
        entries: ['Self-hosted basemap (light)'],
        presets,
        maxZoom: 15,
        maxBytes: 5_000_000_000,
        source: 'https://build.protomaps.com/{build}.pmtiles',
      };
    };
    await page.route('**/api/v1/admin/basemap', (route) => route.fulfill({ json: status() }));
    await page.route('**/api/v1/admin/basemap/builds', (route) =>
      route.fulfill({ json: [{ build: '20261024', sizeBytes: 139_134_051_681, uploaded: '2026-10-24T09:06:14Z', version: '4.15.2' }] }),
    );
    await page.route('**/api/v1/admin/basemap/estimate', (route) =>
      route.fulfill({ json: { build: '20261024', bytes: 1_300_000_000, freeBytes: 90_000_000_000, refusalCode: null } }),
    );
    await page.route('**/api/v1/admin/basemap/downloads', (route) => {
      started = true;
      return route.fulfill({ status: 202, json: { ...job, state: 'queued', percent: 0 } });
    });

    await gotoRoute(page, '/admin/messaging');
    await page.getByRole('tab', { name: /Own basemap/ }).click();
    await expect(page.getByTestId('basemap-none')).toContainText('Self-hosted basemap (light)');

    await page.getByTestId('basemap-region').getByText('Romania', { exact: true }).click();
    await page.getByTestId('basemap-estimate').click();
    await expect(page.getByTestId('basemap-estimate-result')).toHaveText('About 1.3 GB, from build 20261024.');

    await page.getByTestId('basemap-download').click();
    await expect(page.getByTestId('basemap-download-tag')).toBeVisible({ timeout: 15_000 });
    // The page asks again while the download is underway, and shows it served once it is done.
    await expect(page.getByTestId('basemap-current')).toContainText('1.3 GB', { timeout: 30_000 });
    await expect(page.getByTestId('basemap-download-tag')).toHaveText('finished');
  });
});
