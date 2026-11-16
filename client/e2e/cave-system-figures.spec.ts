// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { login } from './helpers.ts';

/**
 * A cave system's page carries the depth and height of the whole, taken across the caves filed
 * under it.
 *
 * What this shows that the server tests and the card's unit tests cannot. The arithmetic and the
 * withholding are proved against the API; the card's drawing is proved against a hand-written
 * answer. What only a browser can show is that the page of a cave_system feature actually mounts
 * the card, that it reads the route for the system and not the single-cave one, and that figures
 * taken over two caves arrive on screen in metres with the sentence about who is counted.
 */
test('a cave system page shows the highest and lowest entrance across its member caves', async ({ page }) => {
  const stamp = Date.now();
  await login(page);

  const featureTypes = await asPerson<{ id: number; code: string }[]>(page, 'GET', '/api/v1/feature-types');
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const entranceTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/entrance-types');
  const systemType = featureTypes.find((t) => t.code === 'cave_system');
  expect(systemType, 'the cave_system feature type is seeded').toBeTruthy();

  const system = await asPerson<{ id: string }>(page, 'POST', '/api/v1/features', {
    kind: 'generic',
    name: `E2E System ${stamp}`,
    featureTypeId: systemType!.id,
    geometry: {
      type: 'Polygon',
      coordinates: [
        [
          [25.4, 45.5],
          [25.55, 45.5],
          [25.55, 45.62],
          [25.4, 45.62],
          [25.4, 45.5],
        ],
      ],
    },
    locationProtected: false,
    visibility: 'authenticated',
  });

  const caveIds: string[] = [];
  for (const [i, altitudes] of [[1200, 1100], [1300]].entries()) {
    const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
      name: `E2E System Cave ${stamp} ${i}`,
      caveTypeId: caveTypes[0]!.id,
      visibility: 'authenticated',
      locationProtected: false,
      parentId: system.id,
      explorationStatus: 'Unknown',
      isShowCave: false,
    });
    caveIds.push(cave.id);
    for (const [j, altitude] of altitudes!.entries()) {
      await asPerson(page, 'POST', `/api/v1/caves/${cave.id}/entrances`, {
        entranceTypeId: entranceTypes[0]!.id,
        isMain: j === 0,
        geom: { type: 'Point', coordinates: [25.45 + (i * 0.01) + (j * 0.001), 45.55] },
        altitude,
        positionQuality: 'Gps',
      });
    }
  }

  try {
    await page.goto(`/features/${system.id}`);
    const card = page.getByTestId('area-system-figures');
    await expect(card).toBeVisible({ timeout: 30_000 });
    await expect(card.getByText('Highest entrance of the system')).toBeVisible();
    await expect(card.getByText('1300.0 m')).toBeVisible();
    await expect(card.getByText('1100.0 m')).toBeVisible();
    // The sentence about who is counted is load-bearing: without it the figures read as the
    // system's own, when they are the system as far as this reader may place it.
    await expect(card.getByText(/closed to you adds nothing here/)).toBeVisible();
  } finally {
    for (const id of caveIds) {
      await asPerson(page, 'DELETE', `/api/v1/caves/${id}`);
    }
    await asPerson(page, 'DELETE', `/api/v1/features/${system.id}`);
  }
});
