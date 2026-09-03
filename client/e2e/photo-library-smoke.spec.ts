// A one-off check that a neighbouring photo library's photographs reach this application's map,
// and that their pictures are served by this application rather than by the library.
// Not part of the suite: it needs a running PhotoPrism with an indexed fixture library beside it.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { login, overlayTreeNode } from './helpers.ts';

const FIXTURE_COUNT = 40;

// Needs a photo library running beside the application with the invented fixture set indexed:
// deploy/photo-fixtures/make-fixtures.py generates it, and the compose overlay starts the library.
// Skipped unless the environment says one is there, so an ordinary run is unaffected.
test.skip(
  !process.env.SILEXGIS_E2E_PHOTO_LIBRARY,
  'set SILEXGIS_E2E_PHOTO_LIBRARY=1 with an indexed fixture library running',
);

test('a neighbouring library, its photographs, and one of them opened', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', (r) => requests.push(r.url()));

  await login(page, 'admin@example.org', 'Photolib-Dev-1234!');
  await page.goto('/map');
  await expect(page.locator('.map-canvas')).toBeVisible({ timeout: 20_000 });

  const row = overlayTreeNode(page, 'Photo library (PhotoPrism)');
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole('checkbox').check();

  const canvas = page.locator('.map-canvas');
  const box = (await canvas.boundingBox())!;
  const centre = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  let shown = 0;

  for (let step = 0; step < 9; step += 1) {
    const match = /(\d+) photographs shown/.exec(await page.locator('body').innerText());
    if (match && Number(match[1]) > 0) {
      shown = Number(match[1]);
      break;
    }
    await page.mouse.move(centre.x, centre.y);
    await page.mouse.wheel(0, 400);
    await page.waitForTimeout(1200);
  }
  expect(shown).toBe(FIXTURE_COUNT);

  await page.screenshot({ path: 'test-results/photo-library-map.png' });

  // Zoom in on the cluster until single photographs are separable, then open one. The cluster
  // sits where the fixtures were generated; its position on screen is read from the drawn layer
  // rather than assumed, because the starting view is whatever the installation's home view is.
  // Page coordinates, read off the screenshot taken a moment ago -- the canvas offset is
  // already included in them, so adding it again lands a hundred kilometres away.
  const clusterAt = { x: 413, y: 408 };
  for (let step = 0; step < 7; step += 1) {
    await page.mouse.move(clusterAt.x, clusterAt.y);
    await page.mouse.wheel(0, -400);
    await page.waitForTimeout(900);
  }
  await page.waitForTimeout(1500);
  await page.screenshot({ path: 'test-results/photo-library-zoomed.png' });

  // Click a photograph. The pin's position is known because the zoom sequence above is
  // deterministic; it was read off the screenshot rather than guessed. The map canvas is WebGL,
  // so it cannot be sampled from inside the page -- getImageData has no 2D context to read.
  const pins: Array<[number, number]> = [[546, 334], [616, 48]];
  let opened = false;
  for (const [x, y] of pins) {
    await page.mouse.move(x, y);
    await page.mouse.click(x, y);
    await page.waitForTimeout(2500);
    opened = await page.locator('.map-library-photo-popup img').first().isVisible().catch(() => false);
    if (opened) break;
  }
  await page.screenshot({ path: 'test-results/photo-library-popup.png' });

  expect(opened).toBe(true);

  const picture = page.locator('.map-library-photo-popup img').first();
  await expect(picture).toBeVisible();
  const src = await picture.getAttribute('src');
  const loaded = await picture.evaluate((el) => (el as HTMLImageElement).naturalWidth);
  // eslint-disable-next-line no-console
  console.log(`popup opened, picture ${loaded}px wide, served from ${src?.slice(0, 60)}`);
  expect(loaded).toBeGreaterThan(0);
  expect(src).toContain('/photo-libraries/');

  // The bytes must come from this application's own origin. A request straight to the library
  // would mean a credential in the browser and a picture nobody here authorised.
  const thumbs = requests.filter((u) => u.includes('/photo-libraries/') && u.includes('/thumbnails/'));
  const foreign = requests.filter((u) => u.includes(':2342'));
  // eslint-disable-next-line no-console
  console.log(`shown=${shown} thumbnailRequests=${thumbs.length} requestsToLibrary=${foreign.length}`);
  expect(foreign).toHaveLength(0);
});
