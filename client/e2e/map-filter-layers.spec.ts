// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount, signedInElsewhere } from './arrange.ts';
import { gotoRoute, login, mapAddress, overlayTreeNode, pixelOnMap, scene3dSurface, waitForScene3dReady } from './helpers.ts';

/**
 * What a layer drawn from a saved filter does beyond being looked at, what the registry calls "in
 * view", and how many quick filters the chip row holds.
 *
 * Each test arranges caves of its own, made by the administrator in a browser of their own, in a
 * patch of ground nothing else uses, so the only marker under the click is the one the test made —
 * and the only way to hit it is through the layer under test, because the main entrance layer is
 * switched off first.
 */

/** Far from the demonstration data and from every other spec's caves. */
const HERE = { lon: 27.81, lat: 47.31 };
const OUT_OF_VIEW = { lon: 27.9, lat: 47.31 };

interface Arranged {
  caveName: string;
  region: string;
  caveIds: string[];
}

async function arrangeCave(
  admin: Page,
  stamp: string,
  entrances: { lon: number; lat: number; isMain: boolean }[],
): Promise<Arranged> {
  const caveTypes = await asPerson<{ id: number }[]>(admin, 'GET', '/api/v1/cave-types');
  const entranceTypes = await asPerson<{ id: number }[]>(admin, 'GET', '/api/v1/entrance-types');
  const caveName = `E2E layer cave ${stamp}`;
  const region = `E2E layer region ${stamp}`;
  const cave = await asPerson<{ id: string }>(admin, 'POST', '/api/v1/caves', {
    name: caveName,
    caveTypeId: caveTypes[0].id,
    region,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'Unknown',
    isShowCave: false,
  });
  for (const at of entrances) {
    await asPerson(admin, 'POST', `/api/v1/caves/${cave.id}/entrances`, {
      entranceTypeId: entranceTypes[0].id,
      isMain: at.isMain,
      geom: { type: 'Point', coordinates: [at.lon, at.lat] },
      positionQuality: 'Gps',
    });
  }
  return { caveName, region, caveIds: [cave.id] };
}

/** A saved filter keeping the caves of one region, made by the member through the API. */
async function regionFilter(page: Page, name: string, region: string): Promise<string> {
  const created = await asPerson<{ id: string }>(page, 'POST', '/api/v1/saved-filters', {
    name,
    description: null,
    visibility: 'private',
    document: {
      version: 1,
      scope: [
        {
          world: 'feature',
          where: {
            node: 'allOf',
            of: [
              { node: 'condition', field: 'kind', op: 'equals', values: [{ type: 'id', value: 'cave' }] },
              { node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: region }] },
            ],
          },
        },
      ],
      sort: 'updated',
      descending: true,
    },
  });
  return created.id.toLowerCase();
}

test('a marker on a saved-filter layer selects its cave, on the map and in the 3D view beside it', async ({
  page,
  browser,
}) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const admin = await signedInElsewhere(browser);
  const created: string[] = [];
  let filterId: string | undefined;
  try {
    const cave = await arrangeCave(admin.page, stamp, [{ ...HERE, isMain: true }]);
    created.push(...cave.caveIds);

    const member = await registerAccount(page.request, 'filterlayers');
    await login(page, member.email, member.password);
    const layerName = `E2E layer ${stamp}`;
    filterId = await regionFilter(page, layerName, cave.region);

    const camera = { ...HERE, zoom: 16 };
    await gotoRoute(page, mapAddress(camera));
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });

    // ---- the layer, and the main entrance layer switched off: only the layer can be hit ----
    const layerAnswered = page.waitForResponse(
      (response) =>
        response.ok() &&
        new URL(response.url()).pathname === '/api/v1/map/cave-entrances' &&
        new URL(response.url()).searchParams.get('savedFilterId')?.toLowerCase() === filterId,
      { timeout: 60_000 },
    );
    await page.getByTestId('add-filter-layer').click();
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: layerName }).click();
    const answer = (await (await layerAnswered).json()) as { features: unknown[] };
    expect(answer.features).toHaveLength(1);
    const entrances = overlayTreeNode(page, 'Cave entrances');
    await entrances.locator('.ant-tree-checkbox').click();
    await expect(entrances.locator('.ant-tree-checkbox-checked')).toHaveCount(0);

    // ---- a click on its marker selects the cave, as on the main layer ----
    const at = await pixelOnMap(page, camera, HERE.lat, HERE.lon);
    const dock = page.locator('.map-right-tabs');
    await expect(async () => {
      await page.mouse.click(at.x, at.y);
      await expect(dock.getByText(cave.caveName).first()).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 30_000 });

    // ---- the 3D view beside the map draws the same layer and asks for it with its filter ----
    const sceneAsked = page.waitForResponse(
      (response) =>
        response.ok() &&
        new URL(response.url()).pathname === '/api/v1/map/features' &&
        new URL(response.url()).searchParams.get('savedFilterId')?.toLowerCase() === filterId,
      { timeout: 60_000 },
    );
    await page.getByTestId('map-scene3d-toggle').click();
    await waitForScene3dReady(page, 60_000);
    await sceneAsked;

    // The scene's own entrance layer off as well, so a pick can only be the filter layer's.
    await page.getByTestId('scene3d-layers-trigger').click();
    const sceneEntrances = page.getByTestId('scene3d-layer-panel').getByRole('checkbox', { name: 'Cave entrances' });
    await sceneEntrances.uncheck();
    await expect(sceneEntrances).not.toBeChecked();
    await page.keyboard.press('Escape');
    await page.getByTestId('scene3d-preset-top').click();

    const box = (await scene3dSurface(page).boundingBox())!;
    await expect(async () => {
      await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
      await expect(page.getByTestId('scene3d-callout')).toContainText(cave.caveName, { timeout: 2_000 });
    }).toPass({ timeout: 90_000 });
  } finally {
    if (filterId) {
      await asPerson(page, 'DELETE', `/api/v1/saved-filters/${filterId}`).catch(() => undefined);
    }
    for (const id of created) {
      await asPerson(admin.page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
    }
    await admin.context.close();
  }
});

test('"List the caves in view" lists a cave whose main entrance is outside the view and a second one inside', async ({
  page,
  browser,
}) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const admin = await signedInElsewhere(browser);
  const created: string[] = [];
  try {
    const cave = await arrangeCave(admin.page, stamp, [
      { ...OUT_OF_VIEW, isMain: true },
      { ...HERE, isMain: false },
    ]);
    created.push(...cave.caveIds);

    const member = await registerAccount(page.request, 'inview');
    await login(page, member.email, member.password);
    await gotoRoute(page, mapAddress({ ...HERE, zoom: 15 }));
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    await page.getByTestId('map-list-in-view').click();
    await expect(page).toHaveURL(/\/caves\?.*bbox=/);
    await expect(page.getByText("Only caves in the map's view")).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('cell', { name: cave.caveName, exact: true })).toBeVisible({ timeout: 30_000 });
  } finally {
    for (const id of created) {
      await asPerson(admin.page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
    }
    await admin.context.close();
  }
});

test('more than six saved filters can be pinned, and the chip row still sits on the map', async ({ page }) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const member = await registerAccount(page.request, 'manychips');
  await login(page, member.email, member.password);
  const ids: string[] = [];
  try {
    for (let n = 1; n <= 8; n++) {
      ids.push(await regionFilter(page, `E2E chip ${n} with a rather long name ${stamp}`, `chip ${stamp}`));
    }

    await gotoRoute(page, '/saved-filters');
    for (const id of ids) {
      const pin = page.getByTestId(`saved-filter-pin-${id}`);
      await expect(pin).toBeEnabled();
      await pin.click();
      await expect(pin).toHaveAttribute('aria-pressed', 'true');
    }
    await expect
      .poll(async () => JSON.stringify(await asPerson(page, 'GET', '/api/v1/me/preferences')), { timeout: 20_000 })
      .toContain(ids[7]);

    await gotoRoute(page, mapAddress({ ...HERE, zoom: 10 }));
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    const row = page.getByTestId('map-quick-filters');
    const chips = row.locator('.quick-filter-chip');
    await expect(chips).toHaveCount(8, { timeout: 30_000 });
    // The chips keep the whole name as their accessible name, while what is drawn is cut short.
    await expect(row.getByRole('button', { name: `E2E chip 8 with a rather long name ${stamp}`, exact: true })).toBeVisible();
    const rowBox = (await row.boundingBox())!;
    const mapBox = (await page.locator('.ol-viewport').boundingBox())!;
    // Wrapped into a few lines inside the map, never a column down it.
    expect(rowBox.height).toBeLessThan(mapBox.height / 3);
    expect(rowBox.x + rowBox.width).toBeLessThanOrEqual(mapBox.x + mapBox.width);
  } finally {
    for (const id of ids) {
      await asPerson(page, 'DELETE', `/api/v1/saved-filters/${id}`).catch(() => undefined);
    }
  }
});
