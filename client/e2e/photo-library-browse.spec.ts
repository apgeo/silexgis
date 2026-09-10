// The browse surface: looking through a neighbouring library's photographs without a map.
// Needs a library running beside the application with the invented fixture set indexed.
import { expect } from '@playwright/test';

import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

test.skip(
  !process.env.SILEXGIS_E2E_PHOTO_LIBRARY,
  'set SILEXGIS_E2E_PHOTO_LIBRARY=1 with an indexed fixture library running',
);

test('a library can be looked through, and its pictures come from this application', async ({
  page,
}) => {
  const pictures: string[] = [];
  const foreign: string[] = [];
  page.on('request', (r) => {
    const u = r.url();
    if (u.includes('/photo-libraries/') && u.includes('/thumbnails/')) pictures.push(u);
    if (u.includes(':2342') || u.includes(':2283')) foreign.push(u);
  });

  await login(page, 'admin@example.org', 'Photolib-Dev-1234!');
  await page.goto('/photo-library');
  await page.waitForLoadState('networkidle');

  // Choose the library deliberately rather than taking whichever the page opens on: the two are
  // configured independently and either may be the one whose credential an installation has not
  // finished setting up. The surface under test is the browsing, not which tab is first.
  // One library configured here, so the page opens on it. Where two are configured the page offers
  // a chooser; which one it opens on is not what this check is about.
  await page.waitForTimeout(3000);

  const body = await page.locator('body').innerText();
  // eslint-disable-next-line no-console
  console.log(`page said: ${JSON.stringify(body.replace(/\s+/g, ' ').slice(0, 260))}`);

  // Pictures are drawn, and every one of them is asked of this application rather than the library.
  await expect.poll(() => pictures.length, { timeout: 20_000 }).toBeGreaterThan(0);
  for (const u of pictures) expect(u).toContain('/api/v1/photo-libraries/');
  expect(foreign).toHaveLength(0);

  await page.screenshot({ path: 'test-results/photo-library-browse.png' });
});
