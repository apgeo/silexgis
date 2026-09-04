// Checks that a photo-library overlay can draw the photographs themselves instead of pins.
// Needs a library running beside the application with the invented fixture set indexed.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { login, overlayTreeNode } from './helpers.ts';

test.skip(
  !process.env.SILEXGIS_E2E_PHOTO_LIBRARY,
  'set SILEXGIS_E2E_PHOTO_LIBRARY=1 with an indexed fixture library running',
);

test('a library overlay draws its photographs, and asks this application for them', async ({
  page,
}) => {
  const pictures: string[] = [];
  page.on('request', (r) => {
    if (r.url().includes('/photo-libraries/') && r.url().includes('/thumbnails/')) {
      pictures.push(r.url());
    }
  });

  await login(page, 'admin@example.org', 'Photolib-Dev-1234!');
  await page.goto('/map');
  await expect(page.locator('.map-canvas')).toBeVisible({ timeout: 20_000 });

  const row = overlayTreeNode(page, 'Photo library (PhotoPrism)');
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole('checkbox').check();

  const centre = { x: 640, y: 380 };
  for (let step = 0; step < 9; step += 1) {
    if (/\d+ photographs shown/.test(await page.locator('body').innerText())) break;
    await page.mouse.move(centre.x, centre.y);
    await page.mouse.wheel(0, 400);
    await page.waitForTimeout(1200);
  }

  const status = page.getByTestId('library-photos-status-photoprism');
  await expect(status).toBeVisible();

  // Pins until asked otherwise: nothing has been fetched for a picture yet.
  expect(pictures).toHaveLength(0);

  await status.getByRole('checkbox', { name: /photographs themselves/i }).check();
  await page.waitForTimeout(3000);

  // eslint-disable-next-line no-console
  console.log(`picture requests after switching on: ${pictures.length}`);
  expect(pictures.length).toBeGreaterThan(0);
  // Every one of them is asked of this application, never of the library.
  for (const url of pictures) expect(url).toContain('/api/v1/photo-libraries/');
  expect(pictures.every((u) => u.includes('size=small'))).toBe(true);

  await page.screenshot({ path: 'test-results/photo-library-pictures.png' });
});
