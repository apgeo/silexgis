// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type BrowserContext, type Page } from '@playwright/test';
import { writeCogFixture } from '../src/scene3d/cogFixture.ts';
import { toMercator } from '../src/scene3d/renderedTiles3d.ts';
import { test } from './consoleGuard.ts';
import { entranceToPick, login, openScene3dAt, scene3dSurface } from './helpers.ts';
import { tintedPng } from './png.ts';

// A flyby that changes how the scene looks, in the real scene. The unit tests put a flyby's look on
// a scene made of plain objects; only the engine itself can show that a background really crosses
// into another on the ground, that the survey lines really take another colour, that dragging the
// position back really takes both back, and that stopping really leaves the scene as the viewer had
// it. The picture is probed for colours that nothing else on the screen is.
//
// Nothing leaves the machine: the street map is answered with a flat tile of its own colour, and
// the second background is a GeoTIFF generated here and served by the page's own route.

/** The street map, as served here. */
const STREET_BLUE: [number, number, number] = [118, 168, 208];
/** The GeoTIFF's one colour. */
const GEOTIFF_MAGENTA: [number, number, number] = [220, 30, 200];
/** The survey lines' colour once the flyby colours the caves. Nothing else on the screen is this green. */
const LINE_GREEN: [number, number, number] = [0, 255, 0];

const GEOTIFF_URL = '/e2e-flyby-look/background.tif';
/** Far from any id the installation's catalogue holds. */
const GEOTIFF_ID = 990_061;
const GEOTIFF_NAME = 'E2E flyby GeoTIFF';

function geoTiff(): Buffer {
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

async function serveBackgrounds(context: BrowserContext) {
  const street = tintedPng(256, STREET_BLUE);
  await context.route('**://tile.openstreetmap.org/**', (route) =>
    route.fulfill({ status: 200, contentType: 'image/png', headers: { 'access-control-allow-origin': '*' }, body: street }),
  );
  const bytes = geoTiff();
  await context.route(`**${GEOTIFF_URL}`, (route) => {
    const range = /bytes=(\d+)-(\d*)/.exec(route.request().headers().range ?? '');
    const from = range ? Number(range[1]) : 0;
    const to = range && range[2] !== '' ? Math.min(Number(range[2]), bytes.length - 1) : bytes.length - 1;
    return route.fulfill({
      status: range ? 206 : 200,
      contentType: 'image/tiff',
      headers: {
        'accept-ranges': 'bytes',
        'content-length': String(to - from + 1),
        ...(range ? { 'content-range': `bytes ${from}-${to}/${bytes.length}` } : {}),
      },
      body: bytes.subarray(from, to + 1),
    });
  });
  await context.route(/\/api\/v1\/map-layers(\?.*)?$/, async (route) => {
    const response = await route.fetch();
    const rows = (await response.json()) as unknown[];
    await route.fulfill({
      response,
      json: [
        ...rows,
        {
          id: GEOTIFF_ID,
          name: GEOTIFF_NAME,
          layerKind: 'cog',
          urlTemplate: GEOTIFF_URL,
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
          sortOrder: 9000,
          inDocuments: false,
        },
      ],
    });
  });
}

/**
 * Counts the pixels of the scene near one colour: in its middle as a share, or as a number across
 * all of it the flyby's panel and bar leave uncovered — the panel has a colour well of the same
 * green in it.
 */
async function colourIn(page: Page, [red, green, blue]: readonly number[], where: 'middle' | 'whole', tolerance = 40) {
  const box = (await scene3dSurface(page).boundingBox())!;
  // Asked only when it is there: a box asked of an element that is not waits for it to appear.
  const panelShown = (await page.getByTestId('flyby-panel').count()) > 0;
  const panel = panelShown ? await page.getByTestId('flyby-panel').boundingBox() : null;
  const left = panel ? Math.max(box.x, panel.x + panel.width + 4) : box.x;
  const clip =
    where === 'middle'
      ? { x: box.x + box.width * 0.35, y: box.y + box.height * 0.35, width: box.width * 0.3, height: box.height * 0.3 }
      : { x: left, y: box.y, width: box.x + box.width - left, height: box.height - 90 };
  const picture = await page.screenshot({ clip });
  return page.evaluate(
    async ({ data, red, green, blue, tolerance, share }) => {
      const bytes = Uint8Array.from(atob(data), (c) => c.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
      const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
      const context = canvas.getContext('2d')!;
      context.drawImage(bitmap, 0, 0);
      const pixels = context.getImageData(0, 0, bitmap.width, bitmap.height).data;
      let near = 0;
      for (let at = 0; at < pixels.length; at += 4) {
        if (
          Math.abs(pixels[at]! - red) <= tolerance &&
          Math.abs(pixels[at + 1]! - green) <= tolerance &&
          Math.abs(pixels[at + 2]! - blue) <= tolerance
        ) {
          near++;
        }
      }
      return share ? near / (pixels.length / 4) : near;
    },
    { data: picture.toString('base64'), red, green, blue, tolerance, share: where === 'middle' },
  );
}

async function expectMostly(page: Page, colour: readonly number[], what: string) {
  await expect
    .poll(() => colourIn(page, colour, 'middle'), { message: `the scene never showed ${what}`, timeout: 90_000, intervals: [1_000] })
    .toBeGreaterThan(0.6);
}

/**
 * Picks options of a drop-down list by their titles, then closes it. A list that takes several is
 * searched by each title first: it draws only the options that fit on screen, and the background
 * this spec adds is last in a long catalogue.
 */
async function pickFrom(page: Page, select: string, titles: string[], search = false) {
  await page.getByTestId(select).click();
  const list = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)');
  for (const title of titles) {
    if (search) {
      await page.keyboard.type(title);
    }
    await list.getByTitle(title, { exact: true }).click();
  }
  await page.keyboard.press('Escape');
  // Gone before the next list opens, so the next titles are looked for in that one alone.
  await expect(list).toHaveCount(0);
}

const status = (page: Page) => page.getByTestId('flyby-time');

test('a flyby crosses into another background and colours the caves, scrubs back, and leaves the scene as it was', async ({ page }) => {
  test.slow();
  await serveBackgrounds(page.context());
  await login(page);
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);
  await expectMostly(page, STREET_BLUE, 'the street map');

  await page.getByTestId('scene3d-flyby').click();
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
  // Around what the view shows, a quarter of a turn: the approach, then 1.5 s of circling.
  await page.getByTestId('flyby-load-route').uncheck();
  await page.getByTestId('flyby-turns').fill('0.25');
  await page.getByTestId('flyby-turns').blur();
  await page.getByTestId('flyby-seconds-per-turn').fill('6');
  await page.getByTestId('flyby-seconds-per-turn').blur();

  // The opening look: the street map, the soft sky every preset opens with, no sky in the end.
  await page.getByText('Look at the start').click();
  await expect(page.getByTestId('flyby-look-sky-on')).toBeChecked();
  await pickFrom(page, 'flyby-look-background', ['OpenStreetMap']);

  // Two ready-made sets: the backgrounds cross every two seconds, ending on the GeoTIFF; the caves
  // take one colour from the circle on.
  await page.getByText('Ready-made changes').click();
  await pickFrom(page, 'flyby-recipe-cycle-backgrounds', ['OpenStreetMap', GEOTIFF_NAME], true);
  await page.getByTestId('flyby-recipe-cycle-seconds').fill('2');
  await page.getByTestId('flyby-recipe-cycle-seconds').blur();
  await expect(page.getByTestId('flyby-recipe-cycle-fade')).toBeChecked();
  await page.getByTestId('flyby-recipe-cycle-add').click();
  await page.getByTestId('flyby-recipe-colour-colour').fill('#00ff00');
  await pickFrom(page, 'flyby-recipe-colour-section', ['2. Circle']);
  await page.getByTestId('flyby-recipe-colour-add').click();
  await page.getByText('5 changes').click();
  await expect(page.getByTestId('flyby-changes')).toContainText(`Background: ${GEOTIFF_NAME}`);
  await expect(page.getByTestId('flyby-changes')).toContainText('Survey lines in #00ff00');

  // A title over the opening.
  await page.getByText('Texts over the picture').click();
  await page.getByTestId('flyby-text-title').fill('E2E flyby');

  const before = await colourIn(page, LINE_GREEN, 'whole', 60);
  await page.getByTestId('flyby-play').click();
  await expect(status(page)).toHaveAttribute('data-status', 'playing', { timeout: 180_000 });
  // The title lasts a few real seconds, and a busy machine can take longer than that to report
  // that the flyby is playing. So the title is looked for at a moment the test puts the flyby at,
  // not at whichever moment the clock has reached.
  await page.getByTestId('flyby-pause').click();
  await page.getByTestId('flyby-position').getByRole('slider').focus();
  await page.keyboard.press('Home');
  for (let step = 0; step < 8; step += 1) {
    await page.keyboard.press('ArrowRight');
  }
  await expect(page.getByTestId('flyby-texts')).toHaveAttribute('data-shown', /title/);
  await page.getByTestId('flyby-play').click();
  await expect(status(page)).toHaveAttribute('data-status', 'playing');
  // The look is the flyby's while it flies; at its end, the GeoTIFF and green survey lines.
  await expect(status(page)).toHaveAttribute('data-status', 'ended', { timeout: 120_000 });
  await expectMostly(page, GEOTIFF_MAGENTA, 'the GeoTIFF the flyby crossed into');
  await expect.poll(() => colourIn(page, LINE_GREEN, 'whole', 60), { timeout: 60_000 }).toBeGreaterThan(before + 20);

  // Dragged back to the start: the street map again, the lines in their own colours, the title on.
  await page.getByTestId('flyby-position').getByRole('slider').focus();
  await page.keyboard.press('Home');
  await expect(status(page)).toHaveAttribute('data-status', 'paused');
  await expectMostly(page, STREET_BLUE, 'the street map after scrubbing back');
  await expect.poll(() => colourIn(page, LINE_GREEN, 'whole', 60), { timeout: 60_000 }).toBeLessThanOrEqual(before + 5);
  await expect(page.getByTestId('flyby-texts')).toHaveAttribute('data-shown', /title/);

  // Forward again, and stopped there: the viewer's own scene comes back from the flyby's.
  await page.keyboard.press('End');
  await expectMostly(page, GEOTIFF_MAGENTA, 'the GeoTIFF again at the end');
  await page.getByTestId('flyby-stop').click();
  await expect(status(page)).toHaveAttribute('data-status', 'idle');
  await expect(page.getByTestId('flyby-texts')).toHaveAttribute('data-shown', '');
  await expectMostly(page, STREET_BLUE, 'the viewer\'s own street map after the flyby stopped');
  await expect.poll(() => colourIn(page, LINE_GREEN, 'whole', 60), { timeout: 60_000 }).toBeLessThanOrEqual(before + 5);
  // And the viewer's own choice in the layer list was never touched.
  await page.getByTestId('scene3d-layers-trigger').click();
  await expect(page.getByTestId('scene3d-layer-panel').getByRole('radio', { name: 'OpenStreetMap', exact: true })).toBeChecked();
});
