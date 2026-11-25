// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { asPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

interface FeatureType {
  id: number;
  code: string;
}

/** A square of the given half-width in degrees around a point, as GeoJSON. */
function square(lon: number, lat: number, half: number) {
  return {
    type: 'Polygon',
    coordinates: [[
      [lon - half, lat - half],
      [lon + half, lat - half],
      [lon + half, lat + half],
      [lon - half, lat + half],
      [lon - half, lat - half],
    ]],
  };
}

/**
 * The work-area overview opened by its address on a cold load: its own map is built once the
 * areas have arrived, laid out in its container, and draws their shapes — shown by clicking the
 * middle of the map, framed on one area, and being taken to that area on the main map. Unit tests
 * replace the map with a stub, so this is the only check that a real one draws anything.
 *
 * A parent of its own with one area inside it: opening the parent frames the map on that one area
 * alone, so its middle is under the middle of the map whatever other areas the database holds.
 */
test('the work-area overview draws the areas on a map of its own', async ({ page }) => {
  await login(page);
  const types = await asPerson<FeatureType[]>(page, 'GET', '/api/v1/feature-types');
  const workArea = types.find((type) => type.code === 'work_area');
  expect(workArea, 'the catalogue has the work-area kind').toBeDefined();

  const stamp = Date.now();
  const area = (name: string, geometry: unknown, parentId?: string) =>
    asPerson<{ id: string }>(page, 'POST', '/api/v1/features', {
      kind: 'generic',
      name,
      featureTypeId: workArea!.id,
      geometry,
      description: null,
      properties: null,
      parents: parentId ? [{ parentId, isPrimary: true }] : null,
      locationProtected: false,
      cavingGroupId: null,
      visibility: 'authenticated',
    });

  const created: string[] = [];
  try {
    const parentName = `E2E work area ${stamp}`;
    const parent = await area(parentName, square(25.5, 45.5, 0.2));
    created.push(parent.id);
    const inner = await area(`E2E inner area ${stamp}`, square(25.5, 45.5, 0.1), parent.id);
    created.push(inner.id);

    // By address, not through the menu: the areas are not in any cache yet.
    await gotoRoute(page, '/work-areas');
    const map = page.getByTestId('work-areas-map');
    await expect(map.locator('canvas').first()).toBeVisible({ timeout: 30_000 });

    await page.getByText(parentName, { exact: true }).click();
    await expect(page.getByText(`E2E inner area ${stamp}`, { exact: true })).toBeVisible();
    // The camera eases onto the level, and a press on the map while it moves stops it where it is
    // — OpenLayers cancels an animation on any pointer interaction — so the click waits for the
    // camera to come to rest on the area: the scale bar, redrawn every frame, at a local scale and
    // no longer changing.
    const scale = map.locator('.ol-scale-line-inner');
    await expect(async () => {
      const before = await scale.textContent();
      await page.waitForTimeout(400);
      const after = await scale.textContent();
      expect(after).toBe(before);
      expect(after).toMatch(/^(\d+ m|([1-9]|1\d|20) km)$/);
    }).toPass({ timeout: 20_000 });

    const box = (await map.boundingBox())!;
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    await page.waitForURL((url) => url.pathname === '/map' && url.searchParams.get('area') === inner.id, {
      timeout: 15_000,
    });
  } finally {
    for (const id of created.reverse()) {
      await asPerson(page, 'DELETE', `/api/v1/features/${id}`).catch(() => {});
    }
  }
});
