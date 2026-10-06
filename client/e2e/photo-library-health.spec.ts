// Checks that the layer panel tells an operator the truth about a neighbouring library.
//
// The point of the health surface is that "the library holds nothing here", "the library is not
// answering" and "the credential has been refused" are three different facts that all look like an
// empty map. This drives one of them for real by stopping the container between runs.
//
// Runs through scripts/e2e-photo-libraries.mjs, against libraries of invented pictures.
// SILEXGIS_E2E_LIBRARY_STATE says which state is expected, so the same spec is run either side of
// that script stopping the library.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { gotoRoute, login, overlayTreeNode } from './helpers.ts';
import { fixtureRectangle, mapOver, noLibrariesBecause } from './libraryPhotos.ts';

test.skip(noLibrariesBecause !== null, noLibrariesBecause ?? '');

const expected = process.env.SILEXGIS_E2E_LIBRARY_STATE ?? 'healthy';

test(`the panel reports a library that is ${expected}`, async ({ page, consoleErrors }) => {
  if (expected !== 'healthy') {
    consoleErrors.allow(
      /the server responded with a status of 503/,
      'the library was stopped for this run, and the application answers a request it cannot ' +
        'pass on to it with 503 — which is what the panel then has to say in words',
    );
  }
  const rectangle = fixtureRectangle();
  await login(page);
  // Over the rectangle that holds pictures, so that an empty map can only mean the library.
  await gotoRoute(page, mapOver(rectangle));
  await expect(page.locator('.map-canvas')).toBeVisible({ timeout: 20_000 });

  const row = overlayTreeNode(page, 'Photo library (PhotoPrism)');
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole('checkbox').check();

  const status = page.getByTestId('library-photos-status-photoprism');
  await expect(status).toBeVisible({ timeout: 20_000 });

  // Ask again rather than waiting out the server's own window.
  const recheck = status.getByRole('button', { name: /check|recheck|again/i });
  if (await recheck.isVisible().catch(() => false)) {
    // Done when the panel has re-read the library's state after the library was asked again: the
    // status read that follows the recheck's answer, not one that happened to be in flight before it.
    let answered = false;
    const reread = page.waitForResponse(
      (response) =>
        answered && new URL(response.url()).pathname === '/api/v1/photo-libraries/status',
      { timeout: 30_000 },
    );
    const asked = page
      .waitForResponse(
        (response) =>
          /^\/api\/v1\/photo-libraries\/[^/]+\/recheck$/.test(new URL(response.url()).pathname),
        { timeout: 30_000 },
      )
      .then(() => {
        answered = true;
      });
    await recheck.click();
    await asked;
    await reread;
  }

  if (expected === 'healthy') {
    await expect(status).toContainText(`${rectangle.count} photographs shown`, { timeout: 30_000 });
    await expect(status).not.toContainText(/did not answer/i);
  } else {
    // The reason the panel says anything at all: a stopped library must not read as an empty one.
    await expect(status).toContainText(/did not answer|not answering|refused/i, { timeout: 30_000 });
  }
});
