// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount } from './arrange.ts';
import { gotoRoute, login, mapAddress } from './helpers.ts';

/**
 * The map's own filter written in the condition editor: opened from the map, offering the feature
 * world's fields except the spatial one, applied as a document the map's layers are asked with,
 * kept in the address (a reload draws the same entrances), reopened on the conditions in force,
 * saved as a filter the map then follows by id, and lifted again.
 *
 * The demonstration data fixes the answer: of its caves only "Peștera Demo Ursului" is recorded in
 * Bihor, so a region condition keeps fewer entrances than the whole country holds and more than
 * none. The server's own tests prove what a filter keeps and for whom.
 */
const COUNTRY = { lat: 45.9, lon: 24.9, zoom: 6.5 };

async function entrancesAnswered(page: Page, which: (address: URL) => boolean): Promise<number> {
  const response = await page.waitForResponse(
    (r) => r.ok() && new URL(r.url()).pathname === '/api/v1/map/cave-entrances' && which(new URL(r.url())),
    { timeout: 60_000 },
  );
  const body = (await response.json()) as { features: { properties: { cluster?: boolean; count?: number } }[] };
  return body.features.reduce((sum, f) => sum + (f.properties.cluster === true ? Number(f.properties.count ?? 0) : 1), 0);
}

const unfiltered = (address: URL) => !address.searchParams.has('savedFilterId') && !address.searchParams.has('filter');
const withDocument = (address: URL) => address.searchParams.has('filter');

test('the map is filtered by conditions written in the editor, and the filter survives a reload and a save', async ({
  page,
}) => {
  test.slow();
  const name = `E2E map conditions ${Date.now()}`;
  let savedId: string | undefined;
  const member = await registerAccount(page.request, 'mapcond');
  await login(page, member.email, member.password);

  try {
    // Signing in lands on the map, which asks for every entrance as well; leave it before watching
    // for answers, so the one counted is the map opened below and not a landing answer whose body
    // went with the page.
    await page.goto('about:blank');
    const everything = entrancesAnswered(page, unfiltered);
    await gotoRoute(page, mapAddress(COUNTRY));
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    const all = await everything;
    expect(all).toBeGreaterThan(0);

    // ---- the editor offers the feature fields, the type among them, and not the spatial one ----
    await page.getByTestId('map-filter-edit').click();
    await expect(page.getByTestId('filter-editor')).toBeVisible();
    await page.getByTestId('filter-editor-add-row-root').click();
    await page.getByTestId('filter-editor-field-0').click();
    // The list is long and virtual, so each field is looked for by typing a part of its name.
    const options = page.locator('.ant-select-dropdown:visible .ant-select-item-option');
    await page.keyboard.type('Posit');
    await expect(page.locator('.ant-select-dropdown:visible .ant-empty, .ant-select-dropdown:visible .ant-select-item-empty').first()).toBeVisible();
    await expect(options.filter({ hasText: /^Position$/ })).toHaveCount(0);
    await page.keyboard.press('Control+A');
    await page.keyboard.type('Typ');
    await expect(options.filter({ hasText: /^Type$/ })).toHaveCount(1);
    await page.keyboard.press('Control+A');
    await page.keyboard.type('Regi');
    await options.filter({ hasText: /^Region$/ }).click();
    await page.getByTestId('filter-editor-op-0').click();
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^contains$/ }).click();
    await page.getByTestId('filter-editor-value-0').fill('Bihor');

    // ---- applied: the layer is asked with the document, and the address carries it ----
    const narrowing = entrancesAnswered(page, withDocument);
    await page.getByTestId('filter-editor-apply').click();
    const narrowed = await narrowing;
    expect(narrowed).toBeGreaterThan(0);
    expect(narrowed).toBeLessThan(all);
    await expect(page).toHaveURL(/[?&]filter=/);
    await expect(page.getByTestId('map-filter-active')).toBeVisible();

    // ---- a reload opens on the same filter: the layer is asked with the address's own token ----
    const token = new URL(page.url()).searchParams.get('filter')!;
    const asked = page.waitForRequest(
      (r) => new URL(r.url()).pathname === '/api/v1/map/cave-entrances' && new URL(r.url()).searchParams.get('filter') === token,
      { timeout: 60_000 },
    );
    await page.reload();
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    await asked;
    await expect(page.getByTestId('map-filter-active')).toBeVisible();

    // ---- the editor reopens on the conditions in force ----
    await page.getByTestId('map-filter-edit').click();
    await expect(page.getByTestId('filter-editor-field-0')).toContainText('Region');
    await expect(page.getByTestId('filter-editor-value-0')).toHaveValue('Bihor');

    // ---- saved as a filter: the map follows it by id from then on ----
    await page.getByTestId('map-filter-save').click();
    await page.getByTestId('map-saved-filter-name').fill(name);
    const followed = entrancesAnswered(page, (address) => address.searchParams.has('savedFilterId'));
    await page.locator('.ant-modal').getByRole('button', { name: 'Save', exact: true }).click();
    // The window is not the one counted before the reload, so the number is not compared: the
    // layer being asked by the saved filter's id, and answered, is what is proved.
    await followed;
    await expect(page).toHaveURL(/[?&]saved=/);
    savedId = new URL(page.url()).searchParams.get('saved')!.toLowerCase();
    await expect(page.getByTestId('map-filter-active')).toContainText(name);

    // ---- lifted ----
    const whole = entrancesAnswered(page, unfiltered);
    await page.getByTestId('map-filter-clear').click();
    await whole;
    await expect(page).not.toHaveURL(/[?&](saved|filter)=/);
  } finally {
    if (savedId !== undefined) {
      await asPerson(page, 'DELETE', `/api/v1/saved-filters/${savedId}`).catch(() => undefined);
    }
  }
});
