// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The cave registry's controls as one filter, kept in the address, carried by the export, and kept
 * as a saved filter that another visit can apply.
 *
 * What only a browser can show here is the road between them: that what was set on screen is what
 * the address holds, so a reload — a fresh sign-in round trip with nothing in memory — draws the
 * same rows; that the file asked for from the export menu is asked with the very filter the list
 * was; and that a filter saved on one visit fills the controls on the next and is named while it
 * is in force. The server's own tests prove what the filter returns and to whom.
 *
 * The demonstration data fixes the answer: of its caves, only "Peștera Demo Mare" is recorded in
 * Brașov with at least 1000 m surveyed. "Peștera Demo Mică" is in Brașov and short, so a dropped
 * length condition would show it; "Peștera Demo Ursului" is long and in Bihor, so a dropped region
 * condition would show it.
 */
const FOUND = 'Peștera Demo Mare';
const SHORT = 'Peștera Demo Mică';
const ELSEWHERE = 'Peștera Demo Ursului';

/** Picks an option of a select by its visible text, typing first where the list is long and searchable. */
async function choose(page: Page, testId: string, option: RegExp, search?: string) {
  await page.getByTestId(testId).click();
  if (search !== undefined) {
    await page.keyboard.type(search);
  }
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: option }).first().click();
}

/** The live count the editor shows before anything is applied. */
async function editorCount(page: Page): Promise<number> {
  const count = page.getByTestId('filter-editor-count');
  await expect(count).toHaveText(/Matches: \d+/, { timeout: 30_000 });
  return Number((await count.textContent())!.replace(/\D/g, ''));
}

/** The filter document a token in the address carries, as the client wrote it. */
function documentIn(url: string): string {
  const token = new URL(url).searchParams.get('filter');
  expect(token).toBeTruthy();
  return Buffer.from(token!, 'base64url').toString('utf8');
}

async function expectTheNarrowedRows(page: Page) {
  await expect(page.getByRole('cell', { name: FOUND, exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('cell', { name: SHORT, exact: true })).toHaveCount(0);
  await expect(page.getByRole('cell', { name: ELSEWHERE, exact: true })).toHaveCount(0);
}

test('the cave registry keeps its filter in the address, exports it and saves it', async ({ page }) => {
  test.slow();
  const stamp = Date.now();
  const filterName = `E2E registry filter ${stamp}`;

  await login(page);
  await gotoRoute(page, '/caves');

  // ---- set the controls ----
  await page.getByPlaceholder('Search name or toponym…').fill('Demo');
  const region = page.getByTestId('cave-registry-region');
  await region.fill('Brașov');
  await region.press('Enter');
  const length = page.getByTestId('cave-registry-min-length');
  await length.fill('1000');
  await length.press('Enter');

  await expect(page).toHaveURL(/[?&]filter=/);
  await expectTheNarrowedRows(page);

  // ---- the address reproduces the list ----
  const address = new URL(page.url());
  const token = address.searchParams.get('filter');
  expect(token).toBeTruthy();
  await gotoRoute(page, `${address.pathname}${address.search}`);
  await expect(page.getByTestId('cave-registry-region')).toHaveValue('Brașov');
  await expect(page.getByPlaceholder('Search name or toponym…')).toHaveValue('Demo');
  await expectTheNarrowedRows(page);

  // ---- the export is asked with the list's own filter ----
  const exportRequest = page.waitForRequest((request) => request.url().includes('/api/v1/export/caves?'));
  await page.getByRole('button', { name: 'Export' }).hover();
  await page.getByRole('menuitem', { name: 'CSV' }).click();
  const asked = new URL((await exportRequest).url());
  expect(asked.searchParams.get('filter')).toBe(token);

  // ---- save it ----
  await page.getByTestId('cave-registry-save-filter').click();
  await page.getByTestId('saved-filter-name').fill(filterName);
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByTestId('cave-registry-active-filter')).toContainText(filterName);
  await expect(page).toHaveURL(/[?&]saved=/);

  // ---- apply it on a fresh visit ----
  await gotoRoute(page, '/caves');
  await expect(page.getByRole('cell', { name: SHORT, exact: true })).toBeVisible({ timeout: 30_000 });
  // Chosen by a press on the option rather than read back off the control: applying is an action,
  // so the control holds no value afterwards, and what was applied is read from the page instead.
  await page.getByTestId('cave-registry-apply-saved').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: filterName })
    .click();
  await expect(page.getByTestId('cave-registry-active-filter')).toContainText(filterName);
  await expect(page.getByTestId('cave-registry-region')).toHaveValue('Brașov');
  await expectTheNarrowedRows(page);
  expect(new URL(page.url()).searchParams.get('filter')).toBe(token);

  // ---- back goes to the list before it was applied ----
  await page.goBack();
  await expect(page.getByRole('cell', { name: SHORT, exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('cave-registry-active-filter')).toHaveCount(0);

  // ---- and it is managed from its own screen, where this run removes it ----
  await page.getByTestId('cave-registry-manage-filters').click();
  await expect(page).toHaveURL(/\/saved-filters$/);
  const row = page.getByRole('row').filter({ hasText: filterName });
  await expect(row).toContainText('Region contains “Brașov”');
  await expect(row).toContainText('At least 1000 m long');
  await row.getByRole('button', { name: 'Delete' }).click();
  await page.locator('.ant-popconfirm').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByRole('row').filter({ hasText: filterName })).toHaveCount(0);
});

/**
 * The advanced filter writes into the question the controls compose: a region set by its control,
 * then — in the editor — an any-of holding an at-least length and a group that fixes a feature type
 * and asks one of that type's own typed properties. The count is read before applying; once applied
 * the list narrows, the address holds the whole of it so a reload draws the same rows, the export is
 * asked with it, and the editor reopens on the rows that were written.
 *
 * No demonstration cave carries a feature type, so the property branch matches none of them here and
 * the length decides which caves are listed: what the browser proves is the road (offered only beside
 * its type, accepted by the server, kept through the address), while the server's tests prove what a
 * typed property matches.
 */
test('the advanced filter edits the registry’s own question and keeps it in the address', async ({ page }) => {
  test.slow();

  await login(page);
  await gotoRoute(page, '/caves');
  const region = page.getByTestId('cave-registry-region');
  await region.fill('Brașov');
  await region.press('Enter');
  await expect(page).toHaveURL(/[?&]filter=/);
  await expect(page.getByRole('cell', { name: SHORT, exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('cell', { name: ELSEWHERE, exact: true })).toHaveCount(0);

  // ---- the editor opens on the region the control set ----
  const opener = page.getByTestId('cave-registry-advanced');
  await opener.click();
  await expect(page.getByTestId('filter-editor')).toBeVisible();
  await expect(page.getByTestId('filter-editor-field-0')).toContainText('Region');
  const before = await editorCount(page);

  // ---- any of: at least 1000 m, or a sinkhole at least 1 m deep ----
  await page.getByTestId('filter-editor-add-group-root').click();
  await choose(page, 'filter-editor-field-1.0', /^Surveyed length \(m\)$/, 'Surveyed');
  await choose(page, 'filter-editor-op-1.0', /^at least$/);
  await page.getByTestId('filter-editor-value-1.0').fill('1000');

  await page.getByTestId('filter-editor-add-group-1').click();
  await choose(page, 'filter-editor-field-1.1.0', /^Type$/, 'Type');
  await choose(page, 'filter-editor-value-1.1.0', /^Sinkhole/, 'Sinkhole');
  await page.getByTestId('filter-editor-add-row-1.1').click();
  await choose(page, 'filter-editor-field-1.1.1', /^Property: Depth \(m\)$/, 'Property');
  await choose(page, 'filter-editor-op-1.1.1', /^at least$/);
  await page.getByTestId('filter-editor-value-1.1.1').fill('1');

  // The count follows the draft before anything is applied.
  await expect.poll(() => editorCount(page), { timeout: 30_000 }).toBeLessThan(before);
  expect(await editorCount(page)).toBeGreaterThan(0);

  await page.getByTestId('filter-editor-apply').click();
  await expect(page.getByTestId('filter-editor')).toHaveCount(0);
  await expect(opener).toBeFocused();

  // ---- the list, and the address that carries all of it ----
  await expectTheNarrowedRows(page);
  await expect(page.getByTestId('cave-registry-region')).toHaveValue('Brașov');
  await expect(page.getByTestId('cave-registry-extra')).toBeVisible();
  const written = documentIn(page.url());
  expect(written).toContain('"op":"greaterOrEqual"');
  expect(written).toContain('"field":"property:sinkhole:depth_m"');

  const address = new URL(page.url());
  await gotoRoute(page, `${address.pathname}${address.search}`);
  await expectTheNarrowedRows(page);
  expect(documentIn(page.url())).toBe(written);

  // ---- the export is asked with that same filter ----
  const exportRequest = page.waitForRequest((request) => request.url().includes('/api/v1/export/caves?'));
  await page.getByRole('button', { name: 'Export' }).hover();
  await page.getByRole('menuitem', { name: 'CSV' }).click();
  expect(new URL((await exportRequest).url()).searchParams.get('filter')).toBe(address.searchParams.get('filter'));

  // ---- and the editor reopens on what was written ----
  await page.getByTestId('cave-registry-edit-extra').click();
  await expect(page.getByTestId('filter-editor-field-1.1.1')).toContainText('Property: Depth (m)');
  await expect(page.getByTestId('filter-editor-value-1.0')).toHaveValue('1000');
  await page.getByTestId('filter-editor-cancel').click();
  await expect(page.getByTestId('cave-registry-edit-extra')).toBeFocused();
  expect(documentIn(page.url())).toBe(written);
});

test('the length box reads a comparison typed with signs and the editor opens on it', async ({ page }) => {
  test.slow();

  await login(page);
  await gotoRoute(page, '/caves');
  const region = page.getByTestId('cave-registry-region');
  await region.fill('Brașov');
  await region.press('Enter');

  // A range written with a dash and a unit: the short cave is inside it, the long one is not.
  const length = page.getByTestId('cave-registry-min-length');
  await length.fill('100-500 m');
  await expect(page.getByTestId('cave-registry-length-understood')).toHaveText('Read as: between 100 m and 500 m');
  await length.press('Enter');
  await expect(page.getByRole('cell', { name: SHORT, exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('cell', { name: FOUND, exact: true })).toHaveCount(0);
  expect(documentIn(page.url())).toContain('"op":"between"');
  await expect(length).toHaveValue('100..500');

  // Text it cannot read changes nothing and says so.
  const before = page.url();
  await length.fill('100 km');
  await length.press('Enter');
  await expect(page.getByTestId('cave-registry-length-unreadable')).toBeVisible();
  expect(page.url()).toBe(before);

  // In the editor, a sign typed into the row's value becomes the row's comparison.
  await length.fill('100..500');
  await page.getByTestId('cave-registry-advanced').click();
  await expect(page.getByTestId('filter-editor')).toBeVisible();
  await expect(page.getByTestId('filter-editor-op-1')).toContainText('between');
  await page.getByTestId('filter-editor-value-1').fill('>=1000');
  await expect(page.getByTestId('filter-editor-op-1')).toContainText('at least');
  await expect(page.getByTestId('filter-editor-value-1-understood')).toHaveText('Read as: at least 1000');
  await page.getByTestId('filter-editor-apply').click();
  await expectTheNarrowedRows(page);
  await expect(length).toHaveValue('1000');
});
