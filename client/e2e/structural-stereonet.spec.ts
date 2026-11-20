// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { asPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { chooseOption, gotoRoute, login, pickCaveType } from './helpers.ts';

/** A spot no other flow draws at, so the readings within reach of this cave are this flow's alone. */
const START = { lon: 23.95, lat: 47.8 };
const PASSAGE_BEARING = 65;

/** A dead straight drawn centreline of eleven 40 m legs at the passage bearing. */
function straightCentreline(): Buffer {
  const coordinates: number[][] = [];
  let { lon, lat } = START;
  for (let i = 0; i < 12; i++) {
    coordinates.push([Number(lon.toFixed(7)), Number(lat.toFixed(7)), 900 - i]);
    const radians = (PASSAGE_BEARING * Math.PI) / 180;
    lon += (Math.sin(radians) * 40) / (111_320 * Math.cos((lat * Math.PI) / 180));
    lat += (Math.cos(radians) * 40) / 110_540;
  }
  return Buffer.from(
    JSON.stringify({
      type: 'FeatureCollection',
      features: [{ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates } }],
    }),
  );
}

async function createCave(page: Page, name: string): Promise<string> {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await pickCaveType(page);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
  return new URL(page.url()).pathname.split('/').pop()!;
}

/**
 * Structural readings around a cave reach a reader as poles on a stereonet and as a test of the
 * passage against their strikes.
 *
 * The arithmetic is proved by the unit tests and the gating by the integration tests; what only a
 * browser shows is that the poles the server placed are the dots drawn, that the kind of plane a
 * reader picks changes both answers together, and that the test's verdict arrives as a sentence
 * with its caveat rather than as a bare number.
 */
test('readings around a cave are plotted on a stereonet and tested against the passage', async ({ page }) => {
  await login(page);
  const cave = await createCave(page, `E2E Stereonet Cave ${Date.now()}`);

  await page
    .locator('.ant-card')
    .filter({ hasText: 'Centerlines' })
    .locator('input[type="file"]')
    .setInputFiles({ name: 'e2e-straight.geojson', mimeType: 'application/geo+json', buffer: straightCentreline() });
  await expect(page.getByText('e2e-straight')).toBeVisible({ timeout: 20_000 });

  const types = await asPerson<{ id: number; code: string }[]>(page, 'GET', '/api/v1/feature-types');
  const readingType = types.find((t) => t.code === 'structural_measurement');
  expect(readingType, 'the structural reading kind is seeded').toBeTruthy();

  // Six joints striking across the passage, and one bedding plane.
  const readings = [
    ...[243, 244, 245, 246, 247, 245].map((dipDirection) => ({ type: 'joint', dipDirection, dip: 60 })),
    { type: 'bedding', dipDirection: 10, dip: 15 },
  ];
  for (const [i, reading] of readings.entries()) {
    await asPerson(page, 'POST', '/api/v1/features', {
      kind: 'generic',
      name: `E2E reading ${Date.now()}-${i}`,
      featureTypeId: readingType!.id,
      geometry: { type: 'Point', coordinates: [START.lon + 0.0005, START.lat + i * 0.0002] },
      properties: { measurement_type: reading.type, dip_direction: reading.dipDirection, dip: reading.dip },
      visibility: 'authenticated',
      locationProtected: false,
    });
  }

  await gotoRoute(page, `/caves/${cave}`);
  const card = page.locator('.ant-card').filter({ hasText: 'Passage against the mapped structure' });
  await expect(card).toBeVisible({ timeout: 30_000 });

  // Every kind at first: seven poles, and the joints across the passage tell the strikes apart.
  const net = card.getByTestId('cave-stereonet');
  await expect(net.getByTestId('stereonet-pole')).toHaveCount(7, { timeout: 30_000 });
  await expect(net.getByTestId('stereonet-density-legend')).toContainText('Kamb density');

  // Only the joints: both the net and the test follow the choice.
  await chooseOption(page, card.getByTestId('cave-structure-type'), 'Joint');
  await expect(net.getByTestId('stereonet-pole')).toHaveCount(6, { timeout: 30_000 });
  const comparison = card.getByTestId('strike-comparison');
  await expect(comparison.getByTestId('strike-comparison-verdict')).toContainText(
    'unlikely to come from one distribution',
  );
  await expect(comparison).toContainText('11 survey legs against 6 strikes');
  await expect(comparison).toContainText('not independent');

  // The note under the line comparison now says where the readings went.
  await expect(card.getByText(/plotted on a stereonet/)).toBeVisible();
});
