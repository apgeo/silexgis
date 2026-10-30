// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';
import { emptyDocument, withWhere } from '../src/filters/document.ts';
import { encodeFilter } from '../src/filters/url.ts';
import type { FilterNode } from '../src/filters/types.ts';

/**
 * The geodata screens' conditions: written in the condition editor on the vector list, kept in the
 * address, saved as a filter another visit applies — and, on the map-sheet tab, the same editor
 * and saved filters for the georeferenced maps, with a link about the other list's world refused
 * as unreadable rather than followed.
 *
 * The files are two generated GPX tracks named for this run, so other runs' files cannot drift
 * into the counts. The server's own tests prove what each list returns and to whom.
 */
test.describe.configure({ timeout: 180_000 });

function gpxOf(label: string): string {
  return (
    '<?xml version="1.0" encoding="UTF-8"?>' +
    '<gpx version="1.1" creator="e2e" xmlns="http://www.topografix.com/GPX/1/1">' +
    `<wpt lat="45.4" lon="22.4"><name>${label}</name></wpt></gpx>`
  );
}

async function uploadGpx(page: Page, fileName: string, label: string): Promise<string> {
  const token = await bearerToken(page);
  const auth = { Authorization: `Bearer ${token}` };
  const upload = await page.request.post('/api/v1/geofiles', {
    headers: auth,
    multipart: { file: { name: fileName, mimeType: 'application/octet-stream', buffer: Buffer.from(gpxOf(label)) } },
  });
  expect(upload.status(), await upload.text()).toBe(201);
  const { id } = (await upload.json()) as { id: string };
  await expect
    .poll(
      async () => ((await (await page.request.get(`/api/v1/geofiles/${id}/status`, { headers: auth })).json()) as { importStatus: string }).importStatus,
      { timeout: 60_000 },
    )
    .toBe('imported');
  return id;
}

const mapKindIs = (value: string): FilterNode => ({
  node: 'condition', field: 'mapKind', op: 'equals', values: [{ type: 'id', value }],
});

test('geodata conditions are written in the editor, kept in the address and saved as a filter', async ({ page }) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const filterName = `E2E geodata ${stamp}`;
  const ids: string[] = [];
  let savedId: string | undefined;
  await login(page);

  try {
    await page.goto('about:blank');
    await gotoRoute(page, '/geodata');
    ids.push(await uploadGpx(page, `e2e-geo-${stamp}-alpha.gpx`, `Alpha ${stamp}`));
    ids.push(await uploadGpx(page, `e2e-geo-${stamp}-beta.gpx`, `Beta ${stamp}`));
    await gotoRoute(page, '/geodata');
    await page.getByPlaceholder('Search by name…').fill(`e2e-geo-${stamp}`);
    const rows = page.locator('.ant-table-tbody tr.ant-table-row');
    await expect(rows).toHaveCount(2, { timeout: 30_000 });

    // ---- the conditions written in the editor narrow the list and are in the address ----
    await page.getByTestId('geodata-advanced').click();
    await expect(page.getByTestId('filter-editor')).toBeVisible();
    await page.getByTestId('filter-editor-add-row-root').click();
    await page.getByTestId('filter-editor-field-0').click();
    await page.keyboard.type('Name');
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^Name$/ }).click();
    await page.getByTestId('filter-editor-op-0').click();
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^contains$/ }).click();
    await page.getByTestId('filter-editor-value-0').fill(`${stamp}-alpha`);
    await page.getByTestId('filter-editor-apply').click();

    await expect(page).toHaveURL(/[?&]filter=/);
    await expect(rows).toHaveCount(1, { timeout: 30_000 });
    await expect(rows.first()).toContainText(`${stamp}-alpha`);
    const token = new URL(page.url()).searchParams.get('filter')!;

    // ---- the address reproduces the list ----
    await gotoRoute(page, `/geodata?filter=${token}`);
    await page.getByPlaceholder('Search by name…').fill(`e2e-geo-${stamp}`);
    await expect(rows).toHaveCount(1, { timeout: 30_000 });
    await expect(rows.first()).toContainText(`${stamp}-alpha`);

    // ---- the export of the list carries the same question ----
    const exported = page.waitForResponse(
      (r) =>
        new URL(r.url()).pathname === '/api/v1/geofiles/export' &&
        new URL(r.url()).searchParams.get('filter') !== null,
      { timeout: 60_000 },
    );
    await page.getByTestId('geodata-export-list').click();
    await page.locator('.ant-dropdown:visible .ant-dropdown-menu-item').filter({ hasText: /^GEOJSON$/ }).click();
    const exportResponse = await exported;
    expect(exportResponse.status()).toBe(200);
    // The page consumes its own download as a blob, so the body is read by asking for the same
    // address again with the same credentials.
    const again = await page.request.get(exportResponse.url(), {
      headers: { Authorization: `Bearer ${await bearerToken(page)}` },
    });
    expect(again.status()).toBe(200);
    const exportedText = await again.text();
    expect(exportedText).toContain(`${stamp}-alpha`);
    expect(exportedText).not.toContain(`${stamp}-beta`);

    // ---- saved, then applied on a fresh visit ----
    await page.getByTestId('geodata-save-filter').click();
    await page.getByTestId('geodata-saved-filter-name').fill(filterName);
    await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByTestId('geodata-active-filter')).toContainText(filterName);
    await expect(page).toHaveURL(/[?&]saved=/);
    savedId = new URL(page.url()).searchParams.get('saved')!.toLowerCase();

    await gotoRoute(page, '/geodata');
    await page.getByPlaceholder('Search by name…').fill(`e2e-geo-${stamp}`);
    await expect(rows).toHaveCount(2, { timeout: 30_000 });
    await page.getByTestId('geodata-apply-saved').click();
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: filterName }).click();
    await expect(page.getByTestId('geodata-active-filter')).toContainText(filterName);
    await expect(rows).toHaveCount(1, { timeout: 30_000 });

    // ---- the map-sheet tab has the editor and the saved filters too ----
    await page.getByRole('tab', { name: 'Raster maps' }).click();
    await expect(page).toHaveURL(/tab=raster/);
    await expect(page).not.toHaveURL(/[?&](filter|saved)=/);
    await expect(page.getByTestId('raster-advanced')).toBeVisible();
    await expect(page.getByTestId('raster-apply-saved')).toBeVisible();
    // The saved filter is about geodata files, so this list does not offer it.
    await page.getByTestId('raster-apply-saved').click();
    const offered = page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: filterName });
    await expect(offered).toHaveAttribute('aria-disabled', 'true');
    await page.keyboard.press('Escape');

    // ---- a link carrying maps' conditions asks the maps list with them ----
    const mapsToken = encodeFilter(withWhere(emptyDocument(['georeferencedMap']), 'georeferencedMap', mapKindIs('caveMap')));
    const asked = page.waitForResponse(
      (r) => new URL(r.url()).pathname === '/api/v1/georeferenced-maps' && new URL(r.url()).searchParams.get('filter') === mapsToken,
      { timeout: 60_000 },
    );
    await gotoRoute(page, `/geodata?tab=raster&filter=${mapsToken}`);
    expect((await asked).status()).toBe(200);
    await expect(page.getByTestId('raster-advanced')).toContainText('on');

    // ---- and a link about the other list's world is left out, with the reason ----
    const geofileToken = encodeFilter(withWhere(emptyDocument(['geofile']), 'geofile', mapKindIs('gpx')));
    await gotoRoute(page, `/geodata?tab=raster&filter=${geofileToken}`);
    await expect(page.getByTestId('raster-unreadable-link')).toBeVisible();
  } finally {
    if (savedId !== undefined) {
      await asPerson(page, 'DELETE', `/api/v1/saved-filters/${savedId}`).catch(() => undefined);
    }
    for (const id of ids) {
      await asPerson(page, 'DELETE', `/api/v1/geofiles/${id}`).catch(() => undefined);
    }
  }
});
