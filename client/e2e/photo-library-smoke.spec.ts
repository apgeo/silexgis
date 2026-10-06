// The photographs of two neighbouring libraries reach this application's map, one of them can be
// opened, and its picture is served by this application rather than by the library it came from.
// Runs through scripts/e2e-photo-libraries.mjs, against libraries of invented pictures.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { gotoRoute, login, mapAddress, overlayTreeNode } from './helpers.ts';
import {
  fixtureRectangle,
  loneliestPicture,
  mapOver,
  noLibrariesBecause,
  reachesALibrary,
  type LibrarySource,
} from './libraryPhotos.ts';

const LIBRARIES: { source: LibrarySource; layer: string }[] = [
  { source: 'photoprism', layer: 'Photo library (PhotoPrism)' },
  { source: 'immich', layer: 'Photo library (Immich)' },
];

test.skip(noLibrariesBecause !== null, noLibrariesBecause ?? '');

/** Switches both libraries' overlays on and returns once each has said what it shows. */
async function showBothLibraries(page: import('@playwright/test').Page) {
  for (const { source, layer } of LIBRARIES) {
    const row = overlayTreeNode(page, layer);
    await expect(row, `${layer} should be offered`).toBeVisible({ timeout: 20_000 });
    await row.getByRole('checkbox').check();
    await expect(page.getByTestId(`library-photos-status-${source}`)).toContainText(
      /\d+ photographs? shown/,
      { timeout: 30_000 },
    );
  }
}

test('two neighbouring libraries, their photographs, and one of them opened', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', (request) => requests.push(request.url()));
  const rectangle = fixtureRectangle();

  await login(page);
  await gotoRoute(page, mapOver(rectangle));
  await showBothLibraries(page);

  // Both libraries index the same directory, so both answer the same number for the same view —
  // which is the point: one filters at its own end, the other hands over everything it holds and
  // is filtered here. The number is the generator's. Its other rectangle holds the same pictures
  // with latitude and longitude exchanged, so a view that showed that many would have asked for
  // its rectangle the wrong way round, and one that showed both lots would not have filtered.
  for (const { source } of LIBRARIES) {
    await expect(page.getByTestId(`library-photos-status-${source}`)).toContainText(
      `${rectangle.count} photographs shown`,
    );
  }

  // One photograph, opened. The camera is put over the picture that stands furthest from the
  // others, close enough that nothing else is near it, which puts its pin in the middle of the
  // map. A hard navigation forgets which overlays were on, so they are switched on again.
  const picture = loneliestPicture(rectangle);
  await gotoRoute(page, mapAddress({ lat: picture.lat, lon: picture.lon, zoom: 16 }));
  await showBothLibraries(page);

  const map = (await page.locator('.ol-viewport').boundingBox())!;
  await page.mouse.click(map.x + map.width / 2, map.y + map.height / 2);

  const shown = page.locator('.map-library-photo-popup img').first();
  await expect(shown).toBeVisible({ timeout: 15_000 });
  await expect
    .poll(() => shown.evaluate((image) => (image as HTMLImageElement).naturalWidth), {
      timeout: 20_000,
    })
    .toBeGreaterThan(0);
  expect(await shown.getAttribute('src')).toContain('/photo-libraries/');

  // Nothing may reach either library directly. Their own picture addresses carry their own
  // credentials, and a browser that held one could go on using it.
  expect(requests.filter(reachesALibrary)).toHaveLength(0);
});
