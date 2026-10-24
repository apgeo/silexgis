// SPDX-License-Identifier: AGPL-3.0-or-later
import { AxeBuilder } from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount } from './arrange.ts';
import { gotoRoute, login, mapAddress } from './helpers.ts';

/**
 * The map drawn through saved filters: a filter saved in the cave registry, pinned as a quick chip,
 * narrowing the map's entrance layer with one click; a second filter drawn as a layer of its own;
 * both kept by a saved view; and the map's address carrying its filter to a fresh page.
 *
 * End to end because the road between the pieces is the point — a filter saved on one screen, a
 * pin kept with the account, the address the map writes and a view that restores it — and none of
 * it is visible to a unit test of any one of them. The server's own tests prove what a filter
 * keeps and that it keeps it only among what the reader may see.
 *
 * As an account of its own: the pins are an account preference, and pinning on the administrator
 * would put chips in front of every other spec signing in as the administrator while this one runs.
 *
 * The demonstration data fixes the answers: of its caves only "Peștera Demo Mare" is recorded in
 * Brașov with at least 1000 m surveyed, and "Peștera Demo Ursului" is in Bihor — so the pinned
 * filter keeps fewer entrances than the whole country holds, and the layer's filter keeps others.
 */
const FOUND = 'Peștera Demo Mare';
const WCAG_AA_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/** The whole country, wide enough that the server answers in counted cells. */
const COUNTRY = { lat: 45.9, lon: 24.9, zoom: 6.5 };

/**
 * How many entrances the map's entrance route answered with, for the next answer whose address
 * passes `which`. Read off the application's own request: the route wants the session's token,
 * and the layer draws exactly what it was answered. A counted cell adds its count, a point one.
 */
function entrancesAnswered(page: Page, which: (address: URL) => boolean): Promise<number> {
  return page
    .waitForResponse(
      (response) =>
        response.ok() &&
        new URL(response.url()).pathname === '/api/v1/map/cave-entrances' &&
        which(new URL(response.url())),
      { timeout: 60_000 },
    )
    .then(async (response) => {
      const body = (await response.json()) as { features: { properties: { cluster?: boolean; count?: number } }[] };
      return body.features.reduce((sum, f) => sum + (f.properties.cluster === true ? Number(f.properties.count ?? 0) : 1), 0);
    });
}

const unfilteredAddress = (address: URL) =>
  !address.searchParams.has('savedFilterId') && !address.searchParams.has('filter');

test('a pinned saved filter narrows the map, a filter layer and the filter come back with a view and a link', async ({
  page,
}) => {
  test.slow();
  const stamp = Date.now();
  const pinnedName = `E2E map chip ${stamp}`;
  const layerName = `E2E map layer ${stamp}`;
  const viewName = `E2E filtered view ${stamp}`;
  const created: string[] = [];

  const member = await registerAccount(page.request, 'mapfilters');
  await login(page, member.email, member.password);

  try {
    // ---- a filter saved in the cave registry ----
    await gotoRoute(page, '/caves');
    const region = page.getByTestId('cave-registry-region');
    await region.fill('Brașov');
    await region.press('Enter');
    const length = page.getByTestId('cave-registry-min-length');
    await length.fill('1000');
    await length.press('Enter');
    await expect(page.getByRole('cell', { name: FOUND, exact: true })).toBeVisible({ timeout: 30_000 });
    await page.getByTestId('cave-registry-save-filter').click();
    await page.getByTestId('saved-filter-name').fill(pinnedName);
    await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByTestId('cave-registry-active-filter')).toContainText(pinnedName);
    await expect(page).toHaveURL(/[?&]saved=/);
    const pinnedId = new URL(page.url()).searchParams.get('saved')!.toLowerCase();
    created.push(pinnedId);

    // ---- and a second one, for a layer of its own ----
    const layerFilter = await asPerson<{ id: string }>(page, 'POST', '/api/v1/saved-filters', {
      name: layerName,
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
                { node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: 'Bihor' }] },
              ],
            },
          },
        ],
        sort: 'updated',
        descending: true,
      },
    });
    const layerId = layerFilter.id.toLowerCase();
    created.push(layerId);

    // ---- pinned from the saved-filters screen, and kept with the account ----
    await gotoRoute(page, '/saved-filters');
    const pin = page.getByTestId(`saved-filter-pin-${pinnedId}`);
    await expect(pin).toHaveAccessibleName('Pin as a quick filter');
    await pin.click();
    await expect(pin).toHaveAttribute('aria-pressed', 'true');
    await expect
      .poll(async () => JSON.stringify(await asPerson(page, 'GET', '/api/v1/me/preferences')), { timeout: 20_000 })
      .toContain(pinnedId);

    // ---- the chip is in the cave registry's toolbar too, and applies the filter there ----
    await gotoRoute(page, '/caves');
    const registryChip = page.getByTestId('cave-registry-quick-filters').getByRole('button', { name: pinnedName, exact: true });
    await registryChip.click();
    await expect(page.getByTestId('cave-registry-active-filter')).toContainText(pinnedName);
    await expect(registryChip).toHaveAttribute('aria-pressed', 'true');

    // ---- on the map, the chip narrows the entrance layer ----
    const everything = entrancesAnswered(page, unfilteredAddress);
    await gotoRoute(page, mapAddress(COUNTRY));
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    const all = await everything;

    const chip = page.getByTestId('map-quick-filters').getByRole('button', { name: pinnedName, exact: true });
    await expect(chip).toHaveAttribute('aria-pressed', 'false');
    const narrowing = entrancesAnswered(page, (address) => address.searchParams.get('savedFilterId') === pinnedId);
    await chip.click();
    const narrowed = await narrowing;
    expect(narrowed).toBeGreaterThan(0);
    expect(narrowed).toBeLessThan(all);
    await expect(chip).toHaveAttribute('aria-pressed', 'true');
    await expect(page).toHaveURL(new RegExp(`[?&]saved=${pinnedId}`, 'i'));
    await expect(page.getByTestId('map-filter-active')).toContainText(pinnedName);

    // ---- a layer drawn from the other saved filter ----
    const layerAsked = entrancesAnswered(page, (address) => address.searchParams.get('savedFilterId') === layerId);
    await page.getByTestId('add-filter-layer').click();
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: layerName }).click();
    await layerAsked;
    await expect(page.getByTestId(`filter-layer-${layerId}`)).toBeVisible();

    // The new controls are named and clean for the scanner. Only rule ids and selectors are
    // reported, never the markup the scanner quotes.
    const scan = await new AxeBuilder({ page })
      .include('[data-testid="map-quick-filters"]')
      .include('.layer-composer')
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(scan.violations.flatMap((v) => v.nodes.map((n) => `${v.id} at ${n.target.join(' ')}`))).toEqual([]);

    // ---- saved as a view ----
    await page.getByLabel('Name of the view to save').fill(viewName);
    await page.getByRole('button', { name: 'Save current view' }).click();
    const viewButton = page.getByRole('button', { name: viewName, exact: true });
    await expect(viewButton).toBeVisible({ timeout: 15_000 });
    const link = page.url();

    // ---- lifted, then the page reloaded: the chip stays (the account's), the filter does not ----
    await chip.click();
    await expect(chip).toHaveAttribute('aria-pressed', 'false');
    await expect(page).not.toHaveURL(/[?&]saved=/);
    await page.getByRole('button', { name: `Remove the layer ${layerName}` }).click();
    await expect(page.getByTestId(`filter-layer-${layerId}`)).toHaveCount(0);
    await page.reload();
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    await expect(chip).toHaveAttribute('aria-pressed', 'false', { timeout: 30_000 });
    await expect(page.getByTestId(`filter-layer-${layerId}`)).toHaveCount(0);

    // ---- the view brings back the filter, its chip and the layer ----
    await page.getByRole('button', { name: viewName, exact: true }).click();
    await expect(chip).toHaveAttribute('aria-pressed', 'true');
    await expect(page).toHaveURL(new RegExp(`[?&]saved=${pinnedId}`, 'i'));
    await expect(page.getByTestId(`filter-layer-${layerId}`)).toBeVisible();

    // ---- the map's address reproduces the filter in a fresh page ----
    const fresh = await page.context().newPage();
    try {
      const freshAnswer = entrancesAnswered(fresh, (address) => address.searchParams.get('savedFilterId') === pinnedId);
      const address = new URL(link);
      await gotoRoute(fresh, `${address.pathname}${address.search}${address.hash}`);
      expect(await freshAnswer).toBeGreaterThan(0);
      await expect(fresh.getByTestId('map-filter-active')).toContainText(pinnedName);
      await expect(fresh.getByTestId('map-quick-filters').getByRole('button', { name: pinnedName, exact: true }))
        .toHaveAttribute('aria-pressed', 'true');
    } finally {
      await fresh.close();
    }

    // ---- unpinned from the chip: it leaves the row, focus stays in it ----
    await page.getByRole('button', { name: `Unpin ${pinnedName}` }).click();
    await expect(chip).toHaveCount(0);
    await expect(page.getByTestId('map-quick-filters')).toContainText('No pinned filters');
    await page.getByRole('button', { name: `Delete the view ${viewName}` }).click();
  } finally {
    for (const id of created) {
      await asPerson(page, 'DELETE', `/api/v1/saved-filters/${id}`).catch(() => undefined);
    }
  }
});

test('a link whose saved filter the recipient cannot run opens the map unfiltered and says so', async ({
  page,
  consoleErrors,
}) => {
  // The refusal this test drives on purpose: the map first asks under the link's filter and is
  // answered 404, which the browser reports to its console as a failed resource.
  consoleErrors.allow(
    /Failed to load resource: .*404/,
    'the saved filter named by the link does not exist for this reader, which the route answers 404',
  );
  await login(page);
  // Signing in lands on the map, which asks for every entrance unfiltered as well. The page is
  // left before the answers are watched for, so the one counted below is the link's own map: an
  // answer of the landing map arriving as the link opened was taken instead, and its body was gone
  // with the page by the time it was read.
  await page.goto('about:blank');

  // A saved filter id no reader has: what a clubmate's private filter looks like to a recipient.
  const missing = '00000000-0000-4000-8000-000000000001';
  const refused = page.waitForResponse(
    (response) =>
      response.status() === 404 &&
      new URL(response.url()).pathname === '/api/v1/map/cave-entrances' &&
      new URL(response.url()).searchParams.get('savedFilterId') === missing,
    { timeout: 60_000 },
  );
  const everything = entrancesAnswered(page, unfilteredAddress);
  const address = new URL(mapAddress(COUNTRY), 'http://placeholder');
  address.searchParams.set('saved', missing);
  await gotoRoute(page, `${address.pathname}${address.search}${address.hash}`);
  await refused;

  // The map draws everything the reader may see, the link's filter is gone from the address, and
  // the notice is the unreadable link's — not the refusal's, which would leave the map empty.
  expect(await everything).toBeGreaterThan(0);
  await expect(page.getByTestId('map-filter-unreadable')).toBeVisible();
  await expect(page.getByTestId('map-filter-refused')).toHaveCount(0);
  await expect(page).not.toHaveURL(/[?&]saved=/);
});
