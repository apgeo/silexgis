// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { ownContext, test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { login } from './helpers.ts';

/**
 * A shared map view, opened by somebody with no account: the background it was saved with, and the
 * public cave entrances it had switched on — drawn read-only, a click naming what was clicked.
 *
 * What is driven is the link's whole path: the anonymous page asks the link's own data route, the
 * route answers with a public entrance and nothing that identifies it, and clicking the marker
 * shows the name the server sent. A cave whose visibility is narrower than public is in the same
 * window and must not be drawn. Placed far from the demonstration data so the window holds only
 * these two caves.
 */
test('a shared view draws its public entrances for somebody with no account', async ({ page, browser }) => {
  test.setTimeout(180_000);
  await login(page);

  const stamp = Date.now();
  const lon = 12.4321;
  const lat = 61.2345;
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const entranceTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/entrance-types');
  const publicCave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E Public ${stamp}`,
    caveTypeId: caveTypes[0].id,
    visibility: 'public',
    locationProtected: false,
    explorationStatus: 'Unknown',
    isShowCave: false,
  });
  await asPerson(page, 'POST', `/api/v1/caves/${publicCave.id}/entrances`, {
    entranceTypeId: entranceTypes[0].id,
    isMain: true,
    geom: { type: 'Point', coordinates: [lon, lat] },
    positionQuality: 'Gps',
  });
  const accountsCave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E Accounts ${stamp}`,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'Unknown',
    isShowCave: false,
  });
  await asPerson(page, 'POST', `/api/v1/caves/${accountsCave.id}/entrances`, {
    entranceTypeId: entranceTypes[0].id,
    isMain: true,
    geom: { type: 'Point', coordinates: [lon + 0.002, lat + 0.001] },
    positionQuality: 'Gps',
  });

  const view = await asPerson<{ id: string }>(page, 'POST', '/api/v1/map-views/', {
    name: `E2E Shared ${stamp}`,
    description: null,
    config: {
      configVersion: 1,
      center: [lon, lat],
      zoom: 15,
      entrancesVisible: true,
      surfaceFeaturesVisible: false,
      geofileIds: [],
      rasters: [],
      tagFilter: null,
    },
    isHome: false,
    cavingGroupId: null,
    visibility: 'private',
  });
  const shared = await asPerson<{ shareToken: string }>(page, 'POST', `/api/v1/map-views/${view.id}/share`);

  const anonymous = await ownContext(browser);
  try {
    const visitor = await anonymous.newPage();
    const answered = visitor.waitForResponse(
      (response) => response.url().includes(`/shared/views/${shared.shareToken}/cave-entrances`) && response.ok(),
      { timeout: 60_000 },
    );
    await visitor.goto(`/shared/view/${shared.shareToken}`);
    await expect(visitor.getByRole('heading', { name: `E2E Shared ${stamp}` })).toBeVisible({ timeout: 30_000 });
    // Said on every shared page — this window holds no protected cave at all, and the sentence is
    // there all the same, so its presence tells a reader nothing about what lies in view.
    await expect(visitor.getByTestId('shared-view-protection-note')).toHaveText(
      'Protected caves are never shown on a shared map.',
    );

    const body = (await (await answered).json()) as {
      features: { geometry: { coordinates: number[] }; properties: Record<string, unknown> }[];
    };
    // Only the public cave, by its public name, with nothing that identifies the row.
    const names = body.features.map((feature) => feature.properties.caveName);
    expect(names).toContain(`E2E Public ${stamp}`);
    expect(names).not.toContain(`E2E Accounts ${stamp}`);
    for (const feature of body.features) {
      expect(feature.properties).not.toHaveProperty('id');
      expect(feature.properties).not.toHaveProperty('caveId');
    }

    // The marker sits where the view is centred; clicking it names the cave.
    const map = visitor.getByTestId('shared-view-map');
    await expect(map.locator('.ol-viewport')).toBeVisible();
    const box = (await map.boundingBox())!;
    await expect(async () => {
      await visitor.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
      await expect(visitor.getByTestId('shared-view-picked')).toContainText(`E2E Public ${stamp}`, {
        timeout: 2_000,
      });
    }).toPass({ timeout: 30_000 });
  } finally {
    await anonymous.close();
    await asPerson(page, 'DELETE', `/api/v1/map-views/${view.id}`);
    await asPerson(page, 'DELETE', `/api/v1/caves/${publicCave.id}`);
    await asPerson(page, 'DELETE', `/api/v1/caves/${accountsCave.id}`);
  }
});
