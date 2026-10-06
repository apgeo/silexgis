// The browse surface: looking through a neighbouring library's photographs without a map.
// Runs through scripts/e2e-photo-libraries.mjs, against libraries of invented pictures.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { noLibrariesBecause, reachesALibrary } from './libraryPhotos.ts';

test.skip(noLibrariesBecause !== null, noLibrariesBecause ?? '');

test('a library can be looked through, and its pictures come from this application', async ({
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

  await login(page);
  await gotoRoute(page, '/photo-library');

  // Two libraries are connected, so the page offers a choice and opens on one of them. Which one
  // is not what this is about: either holds the same pictures, and the surface under test is the
  // looking through.
  await expect(page.getByTestId('library-photo-grid')).toBeVisible({ timeout: 30_000 });

  // Pictures are drawn, and every one of them is asked of this application rather than the library.
  await expect.poll(() => pictures.length, { timeout: 20_000 }).toBeGreaterThan(0);
  for (const url of pictures) {
    expect(url).toContain('/api/v1/photo-libraries/');
  }
  expect(requests.filter(reachesALibrary)).toHaveLength(0);
});
