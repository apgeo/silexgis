// A one-off check that neighbouring photo libraries' photographs reach this application's map,
// and that their pictures are served by this application rather than by the libraries.
// Not part of the suite: it needs PhotoPrism and Immich running beside it with the invented
// fixture set indexed. deploy/photo-fixtures/make-fixtures.py generates that set.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { login, overlayTreeNode } from './helpers.ts';

// What the fixture manifest declares inside the rectangle it generated into. Both libraries index
// the same directory, so both must answer the same number for the same viewport — which is the
// point: one filters at its own end, the other returns its whole library and is filtered here.
const FIXTURE_COUNT = 40;

test.skip(
  !process.env.SILEXGIS_E2E_PHOTO_LIBRARY,
  'set SILEXGIS_E2E_PHOTO_LIBRARY=1 with indexed fixture libraries running',
);

test('two neighbouring libraries, their photographs, and one of them opened', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', (r) => requests.push(r.url()));

  await login(page, 'admin@example.org', 'Photolib-Dev-1234!');
  await page.goto('/map');
  await expect(page.locator('.map-canvas')).toBeVisible({ timeout: 20_000 });

  for (const name of ['Photo library (PhotoPrism)', 'Photo library (Immich)']) {
    const row = overlayTreeNode(page, name);
    await expect(row, `${name} should be offered`).toBeVisible({ timeout: 20_000 });
    await row.getByRole('checkbox').check();
  }

  // Widen the view until the fixture rectangle is inside it. Zooming out is the only lever the
  // application offers from outside: it deliberately keeps its map objects to itself.
  const box = (await page.locator('.map-canvas').boundingBox())!;
  const centre = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  let counts: number[] = [];

  for (let step = 0; step < 9; step += 1) {
    counts = [...(await page.locator('body').innerText()).matchAll(/(\d+) photographs shown/g)].map(
      (m) => Number(m[1]),
    );
    if (counts.length >= 2 && counts.every((n) => n > 0)) break;
    await page.mouse.move(centre.x, centre.y);
    await page.mouse.wheel(0, 400);
    await page.waitForTimeout(1200);
  }
  // eslint-disable-next-line no-console
  console.log(`counts reported by the two layers: ${counts.join(', ')}`);
  expect(counts).toHaveLength(2);
  // At least the fixtures, not exactly them. A library is somebody's own archive and may hold
  // photographs this project did not put there — one of the two here does. Asserting an exact total
  // would be asserting over the operator's own collection, and would break the day they add a
  // picture. The fixture manifest is the only population whose count this check knows.
  for (const n of counts) expect(n).toBeGreaterThanOrEqual(FIXTURE_COUNT);

  await page.screenshot({ path: 'test-results/photo-library-both.png' });

  // Open one photograph. The pin position is known because the zoom sequence is deterministic.
  for (let step = 0; step < 7; step += 1) {
    await page.mouse.move(413, 408);
    await page.mouse.wheel(0, -400);
    await page.waitForTimeout(900);
  }
  await page.waitForTimeout(1500);

  let opened = false;
  for (const [x, y] of [[546, 334], [616, 48], [560, 350]] as Array<[number, number]>) {
    await page.mouse.move(x, y);
    await page.mouse.click(x, y);
    await page.waitForTimeout(2500);
    opened = await page.locator('.map-library-photo-popup img').first().isVisible().catch(() => false);
    if (opened) break;
  }
  await page.screenshot({ path: 'test-results/photo-library-popup.png' });
  expect(opened).toBe(true);

  const picture = page.locator('.map-library-photo-popup img').first();
  const src = await picture.getAttribute('src');
  const loaded = await picture.evaluate((el) => (el as HTMLImageElement).naturalWidth);
  expect(loaded).toBeGreaterThan(0);
  expect(src).toContain('/photo-libraries/');

  // Nothing may reach either library directly. Their picture URLs carry their own credentials.
  const foreign = requests.filter((u) => u.includes(':2342') || u.includes(':2283'));
  // eslint-disable-next-line no-console
  console.log(`picture ${loaded}px from ${src?.slice(0, 55)} · requests to libraries: ${foreign.length}`);
  expect(foreign).toHaveLength(0);
});
