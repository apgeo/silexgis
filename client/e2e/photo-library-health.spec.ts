// Checks that the layer panel tells an operator the truth about a neighbouring library.
//
// The point of the health surface is that "the library holds nothing here", "the library is not
// answering" and "the credential has been refused" are three different facts that all look like an
// empty map. This drives one of them for real by stopping the container between runs.
//
// Needs a library running beside the application. SILEXGIS_E2E_LIBRARY_STATE says which state is
// expected, so the same spec can be run either side of stopping it.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { login, overlayTreeNode } from './helpers.ts';

test.skip(
  !process.env.SILEXGIS_E2E_PHOTO_LIBRARY,
  'set SILEXGIS_E2E_PHOTO_LIBRARY=1 with a photo library configured',
);

const expected = process.env.SILEXGIS_E2E_LIBRARY_STATE ?? 'healthy';

test(`the panel reports a library that is ${expected}`, async ({ page }) => {
  await login(page, 'admin@example.org', 'Photolib-Dev-1234!');
  await page.goto('/map');
  await expect(page.locator('.map-canvas')).toBeVisible({ timeout: 20_000 });

  const row = overlayTreeNode(page, 'Photo library (PhotoPrism)');
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole('checkbox').check();

  const status = page.getByTestId('library-photos-status-photoprism');
  await expect(status).toBeVisible({ timeout: 20_000 });

  // Ask again rather than waiting out the server's own window.
  const recheck = status.getByRole('button', { name: /check|recheck|again/i });
  if (await recheck.isVisible().catch(() => false)) {
    await recheck.click();
    await page.waitForTimeout(4000);
  }

  const text = await status.innerText();
  // eslint-disable-next-line no-console
  console.log(`panel said: ${JSON.stringify(text.replace(/\s+/g, ' ').slice(0, 220))}`);

  if (expected === 'healthy') {
    expect(text).not.toMatch(/did not answer/i);
  } else {
    // The whole reason this batch exists: a stopped library must not read as an empty one.
    expect(text).toMatch(/did not answer|not answering|refused/i);
  }
  await page.screenshot({ path: `test-results/photo-library-health-${expected}.png` });
});
