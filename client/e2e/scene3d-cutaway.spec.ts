// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { login, scene3dSurface, waitForScene3dReady } from './helpers.ts';
import { tintedPng } from './png.ts';

// The ground cut away above a cave, in the real engine. The unit tests build the opening — its
// walls, its floor, the clipping of the ground around it — against a stand-in for the engine that
// accepts any option it is handed. A geometry the library rejects, or a clipping it does not
// apply, shows only here: as a render error that takes the scene away, or as a hillside cut open
// with nothing drawn inside the hole. So the hole is looked into, and the colours its walls and
// floor are painted in are counted in the picture the browser composited.
//
// Nothing leaves the machine: the street map under the scene is answered with a flat tile of one
// colour, so that the opening is a change of colour that can be measured.

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');

/** The street map, as this spec serves it: a flat blue the excavation is not. */
const STREET_BLUE: [number, number, number] = [118, 168, 208];
/** What the opening's walls and floor are painted in, as the scene draws them. */
const EXCAVATION_WALL: [number, number, number] = [0x7a, 0x6a, 0x58];
const EXCAVATION_FLOOR: [number, number, number] = [0x4a, 0x40, 0x38];

/** The caves this file made, removed again when the test that made them ends — passed or failed. */
const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

/**
 * The share of the middle of the scene drawn in any of the colours, give or take a little. The
 * middle only: the corners hold the scene's controls.
 */
async function colourShare(page: Page, colours: readonly (readonly number[])[]): Promise<number> {
  const box = (await scene3dSurface(page).boundingBox())!;
  const picture = await page.screenshot({
    clip: { x: box.x + box.width * 0.3, y: box.y + box.height * 0.3, width: box.width * 0.4, height: box.height * 0.4 },
  });
  return page.evaluate(
    async ({ data, colours }) => {
      const bytes = Uint8Array.from(atob(data), (c) => c.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
      const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
      const context = canvas.getContext('2d')!;
      context.drawImage(bitmap, 0, 0);
      const pixels = context.getImageData(0, 0, bitmap.width, bitmap.height).data;
      let near = 0;
      for (let at = 0; at < pixels.length; at += 4) {
        if (
          colours.some(
            ([red, green, blue]) =>
              Math.abs(pixels[at]! - red!) <= 24 &&
              Math.abs(pixels[at + 1]! - green!) <= 24 &&
              Math.abs(pixels[at + 2]! - blue!) <= 24,
          )
        ) {
          near++;
        }
      }
      return near / (pixels.length / 4);
    },
    { data: picture.toString('base64'), colours: colours.map((colour) => [...colour]) },
  );
}

const excavation = [EXCAVATION_WALL, EXCAVATION_FLOOR] as const;

test('the ground above a surveyed cave is cut away in the real engine, and put back', async ({ page }) => {
  test.slow();
  const street = tintedPng(256, STREET_BLUE);
  await page.context().route('**://tile.openstreetmap.org/**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'image/png',
      headers: { 'access-control-allow-origin': '*' },
      body: street,
    }),
  );
  await login(page);

  // A cave of this run's own, with a centerline that has depths: the opening is sized to the
  // survey it reveals, and a plan laid flat on the ground has no depth to cut down to.
  const caveName = `E2E Cutaway ${Date.now()}`;
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  madeCaves.push(/\/caves\/([0-9a-f-]+)/.exec(page.url())![1]!);
  const centerlines = page.locator('.ant-card').filter({ hasText: 'Centerlines' });
  await centerlines.locator('input[type="file"]').setInputFiles(path.join(fixtures, 'e2e-centerline-3d.geojson'));
  await expect(centerlines.getByText('e2e-centerline-3d')).toBeVisible({ timeout: 20_000 });

  // Straight down over the middle of the survey, close enough for survey lines to be drawn: the
  // view a person looks into an opening from.
  await page.goto('about:blank');
  await page.goto('/map3d#3d/46.50180/22.70120/1500/0.0/-90.0');
  await waitForScene3dReady(page, 60_000);

  // The street map first, so the probe is known to see the ground, and no excavation in it.
  await expect
    .poll(() => colourShare(page, [STREET_BLUE]), {
      message: 'the scene never showed the street map',
      timeout: 90_000,
      intervals: [1_000],
    })
    .toBeGreaterThan(0.3);
  expect(await colourShare(page, excavation)).toBeLessThan(0.01);

  await page.getByTestId('scene3d-layers-trigger').click();
  const panel = page.getByTestId('scene3d-layer-panel');
  await panel.getByRole('radio', { name: 'Cut it away' }).check();
  // Drawn, not refused: neither "no survey to cut around", nor "this browser cannot", nor paused.
  await expect(panel.locator('.scene3d-surface-hint').last()).toHaveText(
    'The ground above the cave is cut away, so the survey is seen down an opening in the surface.',
    { timeout: 30_000 },
  );
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();

  // Something is painted inside the hole: its walls and its floor, in their own colours.
  await expect
    .poll(() => colourShare(page, excavation), {
      message: 'the opening was never drawn with its walls and floor',
      timeout: 90_000,
      intervals: [1_000],
    })
    .toBeGreaterThan(0.02);
  await expect(page.getByText(/^Drawing stopped:/)).toHaveCount(0);

  // And put back: the ground closes over the cave again.
  await page.getByTestId('scene3d-layers-trigger').click();
  await panel.getByRole('radio', { name: 'Show the cave through it' }).check();
  await page.keyboard.press('Escape');
  await expect
    .poll(() => colourShare(page, excavation), {
      message: 'the opening stayed after the ground was put back',
      timeout: 60_000,
      intervals: [1_000],
    })
    .toBeLessThan(0.01);
  await expect(page.getByText(/^Drawing stopped:/)).toHaveCount(0);
});
