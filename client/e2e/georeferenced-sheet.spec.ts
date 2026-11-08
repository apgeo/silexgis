// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHash } from 'node:crypto';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { confirmOk, login } from './helpers.ts';

/**
 * Map sheets drawn from a PDF. A sheet that carries no place of its own is drawn, waits as
 * "Needs placement", is placed by typing its four corners, and is then published like any other
 * map; a sheet whose document says where it lies (a geospatial PDF) is ready without help.
 *
 * The same sheet is then placed again by control points, and its placement leaves as a
 * georeference annotation file and comes back in from it.
 *
 * Both documents are generated for this suite: a plain page of lines, and the same kind of page
 * with a WGS 84 frame near 24.0 E 45.5 N written into it. Neither is a map of anything.
 */

const SHEET = 'e2e-sheet';
const GEO_SHEET = 'e2e-geo-sheet';
const SCAN = 'e2e-scan';

async function openRasterTab(page: Page) {
  await page.goto('/geodata');
  await page.getByRole('tab', { name: 'Raster maps' }).click();
}

const upload = (page: Page, file: string) =>
  page.locator('input[type=file][accept=".tif,.tiff,.pdf,.png,.jpg,.jpeg"]').setInputFiles(`e2e/fixtures/${file}`);

/** Every row of a name, removed — earlier aborted runs may have left some behind. */
async function deleteAll(page: Page, name: string) {
  const rows = page.getByRole('row', { name: new RegExp(`^${name}\\b`) });
  for (let remaining = await rows.count(); remaining > 0; remaining--) {
    await rows.first().getByRole('button', { name: 'delete' }).click();
    await confirmOk(page, { exact: true });
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
}

/** A picture of the middle of the map, as a short fingerprint to compare with another. */
async function middleOfTheMap(page: Page): Promise<string> {
  const box = (await page.locator('.ol-viewport').boundingBox())!;
  const picture = await page.screenshot({
    clip: { x: box.x + box.width / 2 - 100, y: box.y + box.height / 2 - 100, width: 200, height: 200 },
  });
  return createHash('sha256').update(picture).digest('hex').slice(0, 16);
}

/** The middle of the map once two pictures a second apart agree — nothing is still arriving. */
async function mapAtRest(page: Page): Promise<string> {
  let last = await middleOfTheMap(page);
  for (let tries = 0; tries < 30; tries++) {
    await page.waitForTimeout(1_000);
    const now = await middleOfTheMap(page);
    if (now === last) {
      return now;
    }
    last = now;
  }
  throw new Error('the map never came to rest');
}

const CORNERS: Record<'nw' | 'ne' | 'se' | 'sw', [number, number]> = {
  nw: [24.0, 45.502],
  ne: [24.004, 45.502],
  se: [24.004, 45.5],
  sw: [24.0, 45.5],
};

/** Three points pressed on the sheet (as fractions of its pane) and the ground each one is typed at. */
const CONTROL_POINTS: { at: [number, number]; lonLat: [number, number] }[] = [
  { at: [0.35, 0.38], lonLat: [24.0012, 45.5014] },
  { at: [0.65, 0.38], lonLat: [24.0028, 45.5014] },
  { at: [0.5, 0.62], lonLat: [24.002, 45.5006] },
];

test('a PDF sheet is placed by its corners; a geospatial PDF places itself', async ({ page, consoleErrors }) => {
  test.setTimeout(360_000);
  await login(page);
  await openRasterTab(page);
  await deleteAll(page, SHEET);
  await deleteAll(page, GEO_SHEET);
  await deleteAll(page, SCAN);

  // A file named as a PDF that is not one is refused, and the drop zone says why on its own row.
  // The refusal is the answer under test, so the browser's line about the failed request is too.
  consoleErrors.allow(/status of 400/, 'the drop zone is shown a file the server refuses on purpose');
  await page
    .locator('input[type=file][accept=".tif,.tiff,.pdf,.png,.jpg,.jpeg"]')
    .setInputFiles({ name: 'not-a-sheet.pdf', mimeType: 'application/pdf', buffer: Buffer.from('plain words') });
  const refused = page.locator('[data-testid=upload-item][data-name="not-a-sheet.pdf"]');
  await expect(refused).toHaveAttribute('data-state', 'failed', { timeout: 15_000 });
  await expect(refused).toContainText('This kind of file is not accepted here.');
  await refused.getByRole('button', { name: 'Dismiss not-a-sheet.pdf' }).click();
  await expect(refused).toHaveCount(0);

  // A sheet without a place of its own is drawn and then waits for one. The drop zone follows the
  // file until the server has it.
  await upload(page, `${SHEET}.pdf`);
  const sent = page.locator(`[data-testid=upload-item][data-name="${SHEET}.pdf"]`);
  await expect(sent).toHaveAttribute('data-state', 'done', { timeout: 15_000 });
  await expect(sent).toContainText(`${SHEET} uploaded`);
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
  // The dialog waits for the publishing, then offers to show the result.
  await expect(editor.getByTestId('placement-show-on-map')).toBeVisible({ timeout: 60_000 });

  // Published, it is a map like any other.
  await editor.getByTestId('placement-show-on-map').click();
  await expect(editor).toBeHidden({ timeout: 15_000 });
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 15_000 });
  const sheetLayer = page.getByRole('checkbox', { name: SHEET }).first();
  await expect(sheetLayer).toBeChecked();

  // And it is drawn there. The published file has an alpha band (the outside of the sheet is
  // see-through), which a reader asking for red, green and blue only gets three bands of while the
  // layer expects four — the layer then draws nothing and logs an error for every tile. So the map
  // is pictured without the sheet, at rest, and must look different with it.
  await sheetLayer.uncheck();
  const without = await mapAtRest(page);
  await sheetLayer.check();
  await expect.poll(() => middleOfTheMap(page), { timeout: 30_000 }).not.toBe(without);

  // A geospatial PDF says where it lies, so it is ready without being placed.
  await openRasterTab(page);
  await upload(page, `${GEO_SHEET}.pdf`);
  const geoRow = page.getByRole('row', { name: new RegExp(`^${GEO_SHEET}\\b`) }).first();
  await expect(geoRow).toBeVisible({ timeout: 15_000 });
  await expect(geoRow.getByText('Ready')).toBeVisible({ timeout: 60_000 });
  await expect(geoRow.getByText('Needs placement')).toHaveCount(0);

  await deleteAll(page, GEO_SHEET);

  // The same kind of sheet again, placed this time by three control points with an affine fit.
  await deleteAll(page, SHEET);
  await upload(page, `${SHEET}.pdf`);
  const again = page.getByRole('row', { name: new RegExp(`^${SHEET}\\b`) }).first();
  await expect(again.getByText('Needs placement')).toBeVisible({ timeout: 60_000 });
  await again.getByRole('button', { name: 'Place on map…' }).click();
  await editor.getByText('Control points').click();
  const sheetPane = editor.getByTestId('placement-sheet');
  await expect(sheetPane).toHaveAttribute('data-ready', 'yes', { timeout: 15_000 });
  await editor.getByTestId('placement-add-point').click();
  const box = (await sheetPane.boundingBox())!;
  for (const [index, point] of CONTROL_POINTS.entries()) {
    // A press in the middle part of the pane lands on the picture whatever the pane's proportions.
    await sheetPane.click({ position: { x: box.width * point.at[0], y: box.height * point.at[1] } });
    const lon = editor.getByTestId(`placement-point-${index}-lon`);
    await expect(lon).toBeVisible({ timeout: 15_000 });
    await lon.fill(point.lonLat[0].toFixed(6));
    await lon.press('Tab');
    const lat = editor.getByTestId(`placement-point-${index}-lat`);
    await lat.fill(point.lonLat[1].toFixed(6));
    await lat.press('Tab');
  }
  await expect(editor.getByTestId('placement-preview-state')).toHaveAttribute('data-state', 'ready', {
    timeout: 30_000,
  });
  await expect(editor.getByTestId('placement-map')).toHaveAttribute('data-preview', 'loaded', { timeout: 15_000 });
  await editor.getByTestId('placement-save').click();
  await editor.getByTestId('placement-close').click({ timeout: 60_000 });
  await expect(editor).toBeHidden({ timeout: 15_000 });
  await expect(again.getByText('Ready')).toBeVisible({ timeout: 60_000 });

  // Its placement leaves as a georeference annotation and comes back in from the same file.
  await again.getByRole('button', { name: 'Adjust placement…' }).click();
  const downloading = page.waitForEvent('download');
  await editor.getByTestId('placement-export').click();
  const download = await downloading;
  expect(download.suggestedFilename()).toBe(`${SHEET}.georef.json`);
  const saved = test.info().outputPath(`${SHEET}.georef.json`);
  await download.saveAs(saved);
  const importing = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith('/placement/annotation'),
  );
  await editor.locator('input[type=file][accept=".json,application/json"]').setInputFiles(saved);
  const imported = await importing;
  expect(imported.status()).toBe(200);
  expect(((await imported.json()) as { placementSource: string }).placementSource).toBe('manual');
  await editor.getByTestId('placement-close').click({ timeout: 60_000 });
  await expect(editor).toBeHidden({ timeout: 15_000 });
  await expect(again.getByText('Ready')).toBeVisible({ timeout: 60_000 });

  await deleteAll(page, SHEET);

  // A scanned picture with no place of its own waits to be placed, like a plain sheet.
  await deleteAll(page, SCAN);
  await upload(page, `${SCAN}.png`);
  const scan = page.getByRole('row', { name: new RegExp(`^${SCAN}\\b`) }).first();
  await expect(scan.getByText('Needs placement')).toBeVisible({ timeout: 60_000 });
  await deleteAll(page, SCAN);
});
