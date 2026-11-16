// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import {
  entranceToPick,
  login,
  scene3dSurface,
  waitForScene3dQuiet,
  waitForScene3dReady,
  watchScene3dDrawing,
} from './helpers.ts';
import { tintedPng } from './png.ts';

// The controls laid over the 3D view, pressed the way a person presses them: a real click at the
// point where the control is drawn. The unit tests press them with a synthetic event, which reaches
// the button whatever is painted over it — so a control covered by the tab column, the callout or
// the attribution passes every one of them and cannot be pressed by anybody. A Playwright click
// refuses an element another one covers, which is the whole of what this file adds.
//
// Everything runs under software drawing on this machine, so the waits are generous and nothing is
// measured against a frame rate.

/** The street map every scene here is drawn on, served locally so its colour is known. */
const STREET_BLUE = [118, 168, 208] as const;

async function serveStreetMap(page: Page) {
  const street = tintedPng(256, [...STREET_BLUE]);
  await page.context().route('**://tile.openstreetmap.org/**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'image/png',
      headers: { 'access-control-allow-origin': '*' },
      body: street,
    }),
  );
}

/** Opens the scene looking straight down at a place, from high enough to see ground around it. */
async function openSceneAt(page: Page, lat: number, lon: number, height = 900) {
  await page.goto('about:blank');
  await page.goto(`/map3d#3d/${lat.toFixed(5)}/${lon.toFixed(5)}/${height}/0.0/-90.0`);
  await waitForScene3dReady(page, 60_000);
}

interface CenterlineQuery {
  west: number;
  south: number;
  east: number;
  north: number;
  zoom: number;
}

/**
 * Every request the scene makes for survey lines, in order. Read only once the scene has gone quiet:
 * the first request of a page goes out from the opening view, before the camera named in the
 * address is applied, so "the next request" is not the one about where the camera ends up.
 */
function recordCenterlineQueries(page: Page): CenterlineQuery[] {
  const queries: CenterlineQuery[] = [];
  page.on('response', (response) => {
    if (response.url().includes('/api/v1/map/cave-centerlines') && response.ok()) {
      const query = new URL(response.url()).searchParams;
      const [west, south, east, north] = query.get('bbox')!.split(',').map(Number);
      queries.push({ west, south, east, north, zoom: Number(query.get('zoom')) });
    }
  });
  return queries;
}

/** The share of a PNG's pixels within a little of one colour, decoded by the page's own decoder. */
async function shareOfColour(page: Page, png: Buffer, [red, green, blue]: readonly number[]) {
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
          pixels[at + 3]! > 200 &&
          Math.abs(pixels[at]! - red) <= 40 &&
          Math.abs(pixels[at + 1]! - green) <= 40 &&
          Math.abs(pixels[at + 2]! - blue) <= 40
        ) {
          near++;
        }
      }
      return near / (pixels.length / 4);
    },
    { data: png.toString('base64'), red, green, blue },
  );
}

test('the camera controls answer a real click at desktop width, and keep asking about what is in view', async ({
  page,
}) => {
  await serveStreetMap(page);
  await watchScene3dDrawing(page);
  await login(page);
  const entrance = await entranceToPick(page);
  const queries = recordCenterlineQueries(page);
  await openSceneAt(page, entrance.lat, entrance.lon);
  await waitForScene3dQuiet(page);
  const straightDown = queries.at(-1)!;

  // The north elevation: the camera swings round to look south at the same place from ten degrees
  // below the horizon. What the scene then asks the server about has to still be that place, at
  // about the same detail — the box is worked out from a tilted camera, which is arithmetic the
  // unit tests can only check against a stand-in that never tilts.
  const north = page.getByTestId('scene3d-preset-north');
  const asked = queries.length;
  await north.click();
  await expect(north).toHaveAttribute('aria-pressed', 'true', { timeout: 30_000 });
  await expect.poll(() => queries.length, { timeout: 60_000 }).toBeGreaterThan(asked);
  await waitForScene3dQuiet(page);
  const fromNorth = queries.at(-1)!;
  expect(fromNorth.west).toBeLessThanOrEqual(entrance.lon);
  expect(fromNorth.east).toBeGreaterThanOrEqual(entrance.lon);
  expect(fromNorth.south).toBeLessThanOrEqual(entrance.lat);
  expect(fromNorth.north).toBeGreaterThanOrEqual(entrance.lat);
  // Never coarser than the plan view of the same place. Finer it is, by design: a zoom is read
  // from the camera's height over the ground, and the elevation stands at the same distance from
  // the place but much lower — measured here at two levels finer than the plan view.
  expect(fromNorth.zoom).toBeGreaterThanOrEqual(straightDown.zoom - 1);

  // Back to the plan view, and the perspective taken out of it: the box frustum is sized by the
  // engine from where the camera stands, so the zoom it reports is the real library's to answer.
  await page.getByTestId('scene3d-preset-top').click();
  await expect(page.getByTestId('scene3d-preset-top')).toHaveAttribute('aria-pressed', 'true', {
    timeout: 30_000,
  });
  await waitForScene3dQuiet(page);
  const projection = page.getByTestId('scene3d-projection-toggle');
  const beforeFlattening = queries.length;
  await projection.click();
  await expect(projection).toHaveAttribute('aria-pressed', 'true');
  // A change of projection is not a camera move by itself; a small drag makes the scene ask again.
  const box = (await scene3dSurface(page).boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width / 2 + 4, box.y + box.height / 2 + 4, { steps: 2 });
  await page.mouse.up();
  await expect.poll(() => queries.length, { timeout: 60_000 }).toBeGreaterThan(beforeFlattening);
  await waitForScene3dQuiet(page);
  const orthographic = queries.at(-1)!;
  expect(Math.abs(orthographic.zoom - straightDown.zoom)).toBeLessThanOrEqual(1);
  await projection.click();
  await expect(projection).toHaveAttribute('aria-pressed', 'false');

  // Following the flat map: off and back on, each a press of the control where it is drawn.
  const coupling = page.getByTestId('scene3d-coupling-toggle');
  const coupled = await coupling.getAttribute('aria-pressed');
  await coupling.click();
  await expect(coupling).toHaveAttribute('aria-pressed', coupled === 'true' ? 'false' : 'true');
  await coupling.click();
  await expect(coupling).toHaveAttribute('aria-pressed', coupled ?? 'true');

  // The layer list opens from its own corner and closes again.
  await page.getByTestId('scene3d-layers-trigger').click();
  await expect(page.getByTestId('scene3d-layer-panel')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('scene3d-layer-panel')).toBeHidden();
});

test('a callout open over the scene leaves the controls beside it pressable', async ({ page }) => {
  await serveStreetMap(page);
  await login(page);
  const entrance = await entranceToPick(page, { nothingDrawnOver: true });
  await openSceneAt(page, entrance.lat, entrance.lon);

  const box = (await scene3dSurface(page).boundingBox())!;
  await expect(async () => {
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    await expect(page.getByTestId('scene3d-callout')).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });

  // The callout is wide and takes pointer events; it is placed off the control column, and a click
  // on each control proves that it is.
  for (const control of ['scene3d-projection-toggle', 'scene3d-coupling-toggle', 'scene3d-preset-east']) {
    await page.getByTestId(control).click();
  }
  await expect(page.getByTestId('scene3d-projection-toggle')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('scene3d-callout-close')).toBeVisible();
  await page.getByTestId('scene3d-callout-close').click();
  await expect(page.getByTestId('scene3d-callout')).toHaveCount(0);
});

test('saving the view as an image saves what the scene drew, not a cleared buffer', async ({ page }) => {
  await serveStreetMap(page);
  await watchScene3dDrawing(page);
  await login(page);
  await openSceneAt(page, 45.53, 25.3, 30_000);
  await waitForScene3dQuiet(page);

  // Read back from the real drawing buffer, which the browser is free to clear once a frame has
  // been shown: read at the wrong moment it is a transparent rectangle with every test green.
  const saving = page.waitForEvent('download', { timeout: 60_000 });
  await page.getByTestId('scene3d-save-image').click();
  const download = await saving;
  expect(download.suggestedFilename()).toMatch(/\.png$/);
  const stream = await download.createReadStream();
  const chunks: Buffer[] = [];
  for await (const chunk of stream) {
    chunks.push(chunk as Buffer);
  }
  const png = Buffer.concat(chunks);

  // Most of the picture is the street map the scene was drawing. The credits are written along
  // its foot, so not all of it.
  expect(await shareOfColour(page, png, STREET_BLUE)).toBeGreaterThan(0.5);
});

test('a context the browser takes away is rebuilt once, and a second loss offers a reload that works', async ({
  page,
}) => {
  await serveStreetMap(page);
  await login(page);
  await openSceneAt(page, 45.53, 25.3, 30_000);

  /** Takes the drawing context away the way a backgrounded phone or a driver reset does. */
  const loseContext = () =>
    page.evaluate(() => {
      const canvas = document.querySelector('[data-testid="scene3d-container"] canvas') as HTMLCanvasElement;
      const context = canvas.getContext('webgl2');
      context?.getExtension('WEBGL_lose_context')?.loseContext();
      return canvas;
    });

  const first = await page.locator('[data-testid="scene3d-container"] canvas').first().elementHandle();
  await loseContext();
  // A first loss is rebuilt from, without a word: a new drawing surface, and the caves asked for again.
  await expect
    .poll(async () => {
      const now = await page.locator('[data-testid="scene3d-container"] canvas').first().elementHandle();
      return now !== null && first !== null && !(await now.evaluate((element, old) => element === old, first));
    }, { timeout: 60_000 })
    .toBe(true);
  await expect(page.getByTestId('scene3d-context-lost')).toHaveCount(0);
  await expect(page.getByTestId('scene3d-data')).toHaveAttribute('data-loading', 'false', { timeout: 60_000 });

  // Lost again at once: a rebuild loop would look like a frozen page and empty a phone's battery,
  // so the view stops and offers to reload.
  await loseContext();
  const lost = page.getByTestId('scene3d-context-lost');
  await expect(lost).toBeVisible({ timeout: 30_000 });
  await lost.getByRole('button', { name: 'Reload' }).click();
  await waitForScene3dReady(page, 60_000);
  await expect(page.getByTestId('scene3d-context-lost')).toHaveCount(0);
});
