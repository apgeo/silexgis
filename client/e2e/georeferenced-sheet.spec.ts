// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { confirmOk, login } from './helpers.ts';

/**
 * Map sheets drawn from a PDF. A sheet that carries no place of its own is drawn, waits as
 * "Needs placement", is placed by typing its four corners, and is then published like any other
 * map; a sheet whose document says where it lies (a geospatial PDF) is ready without help.
 *
 * Both documents are generated for this suite: a plain page of lines, and the same kind of page
 * with a WGS 84 frame near 24.0 E 45.5 N written into it. Neither is a map of anything.
 */

const SHEET = 'e2e-sheet';
const GEO_SHEET = 'e2e-geo-sheet';

async function openRasterTab(page: Page) {
  await page.goto('/geodata');
  await page.getByRole('tab', { name: 'Raster maps' }).click();
}

const upload = (page: Page, file: string) =>
  page.locator('input[type=file][accept=".tif,.tiff,.pdf"]').setInputFiles(`e2e/fixtures/${file}`);

/** Every row of a name, removed — earlier aborted runs may have left some behind. */
async function deleteAll(page: Page, name: string) {
  const rows = page.getByRole('row', { name: new RegExp(`^${name}\\b`) });
  for (let remaining = await rows.count(); remaining > 0; remaining--) {
    await rows.first().getByRole('button', { name: 'delete' }).click();
    await confirmOk(page, { exact: true });
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
}

const CORNERS: Record<'nw' | 'ne' | 'se' | 'sw', [number, number]> = {
  nw: [24.0, 45.502],
  ne: [24.004, 45.502],
  se: [24.004, 45.5],
  sw: [24.0, 45.5],
};

test('a PDF sheet is placed by its corners; a geospatial PDF places itself', async ({ page }) => {
  test.setTimeout(240_000);
  await login(page);
  await openRasterTab(page);
  await deleteAll(page, SHEET);
  await deleteAll(page, GEO_SHEET);

  // A sheet without a place of its own is drawn and then waits for one.
  await upload(page, `${SHEET}.pdf`);
  const row = page.getByRole('row', { name: new RegExp(`^${SHEET}\\b`) }).first();
  await expect(row).toBeVisible({ timeout: 15_000 });
  await expect(row.getByText('Needs placement')).toBeVisible({ timeout: 60_000 });
  await expect(row.getByRole('button', { name: 'Show on map' })).toBeDisabled();

  // Place it by typing the four corners.
  await row.getByRole('button', { name: 'Place on map…' }).click();
  const editor = page.getByTestId('placement-editor');
  await expect(editor.getByTestId('placement-map')).toBeVisible({ timeout: 15_000 });
  await editor.getByText('Type the corners').click();
  for (const [corner, [lon, lat]] of Object.entries(CORNERS)) {
    await editor.getByTestId(`placement-corner-${corner}-lon`).fill(lon.toFixed(6));
    await editor.getByTestId(`placement-corner-${corner}-lon`).press('Tab');
    await editor.getByTestId(`placement-corner-${corner}-lat`).fill(lat.toFixed(6));
    await editor.getByTestId(`placement-corner-${corner}-lat`).press('Tab');
  }
  // The preview of exactly these corners has come back and its picture is drawn on the map.
  await expect(editor.getByTestId('placement-preview-state')).toHaveAttribute('data-state', 'ready', {
    timeout: 30_000,
  });
  await expect(editor.getByTestId('placement-map')).toHaveAttribute('data-preview', 'loaded', { timeout: 15_000 });
  await expect(editor.getByTestId('placement-preview-state')).toContainText('Fit:');

  await editor.getByTestId('placement-save').click();
  await expect(editor).toBeHidden({ timeout: 15_000 });
  await expect(row.getByText('Ready')).toBeVisible({ timeout: 60_000 });

  // Published, it is a map like any other.
  await row.getByRole('button', { name: 'Show on map' }).click();
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('checkbox', { name: SHEET }).first()).toBeChecked();

  // A geospatial PDF says where it lies, so it is ready without being placed.
  await openRasterTab(page);
  await upload(page, `${GEO_SHEET}.pdf`);
  const geoRow = page.getByRole('row', { name: new RegExp(`^${GEO_SHEET}\\b`) }).first();
  await expect(geoRow).toBeVisible({ timeout: 15_000 });
  await expect(geoRow.getByText('Ready')).toBeVisible({ timeout: 60_000 });
  await expect(geoRow.getByText('Needs placement')).toHaveCount(0);

  await deleteAll(page, SHEET);
  await deleteAll(page, GEO_SHEET);
});
