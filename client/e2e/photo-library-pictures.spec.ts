// A photo-library overlay can draw the photographs themselves instead of pins, and asks this
// application for them, never the library.
// Runs through scripts/e2e-photo-libraries.mjs, against libraries of invented pictures.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { gotoRoute, login, overlayTreeNode } from './helpers.ts';
import { fixtureRectangle, mapOver, noLibrariesBecause, reachesALibrary } from './libraryPhotos.ts';

test.skip(noLibrariesBecause !== null, noLibrariesBecause ?? '');

test('a library overlay draws its photographs, and asks this application for them', async ({
  page,
}) => {
  const pictures: string[] = [];
  const requests: string[] = [];
  page.on('request', (request) => {
    const url = request.url();
    requests.push(url);
    if (url.includes('/photo-libraries/') && url.includes('/thumbnails/')) {
      pictures.push(url);
    }
  });
  const rectangle = fixtureRectangle();

  await login(page);
  await gotoRoute(page, mapOver(rectangle));

  const row = overlayTreeNode(page, 'Photo library (PhotoPrism)');
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole('checkbox').check();

  const status = page.getByTestId('library-photos-status-photoprism');
  await expect(status).toContainText(`${rectangle.count} photographs shown`, { timeout: 30_000 });

  // Pins until asked otherwise: nothing has been fetched for a picture yet.
  expect(pictures).toHaveLength(0);

  await status.getByRole('checkbox', { name: /photographs themselves/i }).check();
  // Switching on is answered by the map asking for the pictures of the pins already on it.
  await expect.poll(() => pictures.length, { timeout: 20_000 }).toBeGreaterThan(0);

  // Every one of them is asked of this application, in the small rendering a marker needs.
  for (const url of pictures) {
    expect(url).toContain('/api/v1/photo-libraries/');
    expect(url).toContain('size=small');
  }
  expect(requests.filter(reachesALibrary)).toHaveLength(0);
});
