// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount, signedInElsewhere } from './arrange.ts';
import { gotoRoute, login, mapAddress } from './helpers.ts';

/**
 * A list and the map asking the same question: the feature list's filter opened on the map draws
 * the rows the list holds, the list's whole result becomes the map's working set, the map's view
 * narrows the cave registry, and a search suggestion carries its context line.
 *
 * End to end because each step is a hand-off between two screens — a filter carried in an
 * address, a result carried in the session's working set, a window carried to the server — and
 * no unit test of either screen sees the other one receive it. The server's own tests prove what
 * each route returns and to whom; this proves the screens hand each other the same question.
 *
 * Three caves of the run's own, named with its stamp so nothing else answers the question: two a
 * hundred and fifty metres apart and one twenty kilometres off, all inside the map's opening view,
 * so a close view of the pair is a window that holds two of the three.
 *
 * As an account of its own, reading caves an administrator made: the working set and the panels
 * are this person's, and nothing this flow does should reach the administrator's workspace while
 * other specs sign in as the administrator.
 */
const NEAR = { lon: 25.3, lat: 45.7 };
const BESIDE = { lon: 25.302, lat: 45.701 };
const FAR = { lon: 25.55, lat: 45.82 };

/**
 * How many entrances the map's entrance route answered with, for the next filtered answer. A
 * counted cell adds its count, a point one — the layer draws exactly what it was answered.
 */
function filteredEntrancesAnswered(page: Page): Promise<number> {
  return page
    .waitForResponse(
      (response) =>
        response.ok() &&
        new URL(response.url()).pathname === '/api/v1/map/cave-entrances' &&
        new URL(response.url()).searchParams.has('filter'),
      { timeout: 60_000 },
    )
    .then(async (response) => {
      const body = (await response.json()) as { features: { properties: { cluster?: boolean; count?: number } }[] };
      return body.features.reduce((sum, f) => sum + (f.properties.cluster === true ? Number(f.properties.count ?? 0) : 1), 0);
    });
}

test('a list opens on the map with its rows, becomes the working set, and the map narrows the registry', async ({
  page,
  browser,
}) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const names = {
    near: `E2E TableMap near ${stamp}`,
    beside: `E2E TableMap beside ${stamp}`,
    far: `E2E TableMap far ${stamp}`,
  };
  const region = `E2E region ${stamp}`;

  // ---- the caves, made by the administrator in a browser of its own ----
  const admin = await signedInElsewhere(browser);
  const created: string[] = [];
  try {
    const caveTypes = await asPerson<{ id: number }[]>(admin.page, 'GET', '/api/v1/cave-types');
    const entranceTypes = await asPerson<{ id: number }[]>(admin.page, 'GET', '/api/v1/entrance-types');
    for (const [name, at] of [
      [names.near, NEAR],
      [names.beside, BESIDE],
      [names.far, FAR],
    ] as const) {
      const cave = await asPerson<{ id: string }>(admin.page, 'POST', '/api/v1/caves', {
        name,
        caveTypeId: caveTypes[0].id,
        region,
        visibility: 'authenticated',
        locationProtected: false,
        explorationStatus: 'Unknown',
        isShowCave: false,
      });
      created.push(cave.id);
      await asPerson(admin.page, 'POST', `/api/v1/caves/${cave.id}/entrances`, {
        entranceTypeId: entranceTypes[0].id,
        isMain: true,
        geom: { type: 'Point', coordinates: [at.lon, at.lat] },
        positionQuality: 'Gps',
      });
    }

    const member = await registerAccount(page.request, 'tablemap');
    await login(page, member.email, member.password);

    // ---- the feature list, filtered to the run's caves ----
    await gotoRoute(page, '/features');
    // The stamp is what keeps another run's caves, and the demonstration data, out.
    await page.getByPlaceholder('Search by name…').fill(stamp);
    await page.getByTestId('feature-list-kind').click();
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
      .filter({ hasText: /^Cave$/ })
      .click();
    await expect(page).toHaveURL(/[?&]filter=/);
    const rows = page.locator('.ant-table-tbody tr.ant-table-row');
    await expect(rows).toHaveCount(3, { timeout: 30_000 });
    for (const name of Object.values(names)) {
      await expect(page.getByRole('row', { name: new RegExp(name) })).toBeVisible();
    }
    const listAddress = new URL(page.url());
    const token = listAddress.searchParams.get('filter');
    expect(token).toBeTruthy();

    // ---- "Show these on the map": the map runs the list's document, and draws three ----
    const drawn = filteredEntrancesAnswered(page);
    await page.getByTestId('feature-list-show-on-map').click();
    await expect(page).toHaveURL(/\/map\?/);
    expect(new URL(page.url()).searchParams.get('filter')).toBe(token);
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    expect(await drawn).toBe(3);

    // ---- "Select all": the list's result becomes the working set, listed in the panel ----
    await page.goBack();
    await expect(rows).toHaveCount(3, { timeout: 30_000 });
    const selectAll = page.getByTestId('feature-list-select-all');
    await expect(selectAll).toContainText('(3)');
    await selectAll.click();
    await expect(page).toHaveURL(/\/map\?/);
    const dock = page.locator('.map-right-tabs');
    await dock.getByRole('tab', { name: 'Selection' }).click();
    const card = dock.getByTestId('multi-selection');
    await expect(card.getByRole('heading', { name: '3 selected' })).toBeVisible({ timeout: 30_000 });
    for (const name of Object.values(names)) {
      await expect(card.getByRole('button', { name, exact: true })).toBeVisible({ timeout: 30_000 });
    }
    // The whole result fitted under the cap, so nothing is said about a first part of it.
    await expect(card.getByTestId('multi-selection-capped')).toHaveCount(0);

    // ---- "List the caves in view" from a close view of the pair: the registry holds two ----
    // A fresh page, so the camera is the one the address names rather than the one left behind.
    await page.goto('about:blank');
    const between = { lat: (NEAR.lat + BESIDE.lat) / 2, lon: (NEAR.lon + BESIDE.lon) / 2, zoom: 15 };
    const camera = mapAddress(between);
    await gotoRoute(page, `/map?filter=${encodeURIComponent(token!)}${camera.slice('/map'.length)}`);
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    await page.getByTestId('map-list-in-view').click();
    await expect(page).toHaveURL(/\/caves\?.*bbox=/);
    await expect(page.getByText("Only caves in the map's view")).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('cell', { name: names.near, exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('cell', { name: names.beside, exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: names.far, exact: true })).toHaveCount(0);

    // ---- and with the window taken away, the third comes back: it was the window, not the filter ----
    await page.getByTestId('cave-registry-clear-window').click();
    await expect(page.getByRole('cell', { name: names.far, exact: true })).toBeVisible({ timeout: 30_000 });

    // ---- a search suggestion names its cave's region: the context line ----
    await gotoRoute(page, mapAddress({ ...NEAR, zoom: 8 }));
    await page.getByLabel('Search features, trips or places…').fill(stamp);
    const suggestion = page.locator('.ant-select-dropdown:visible .ant-select-item-option')
      .filter({ hasText: names.far });
    await expect(suggestion).toBeVisible({ timeout: 30_000 });
    await expect(suggestion).toContainText(region);
  } finally {
    for (const id of created) {
      await asPerson(admin.page, 'DELETE', `/api/v1/caves/${id}`);
    }
    await admin.context.close();
  }
});
