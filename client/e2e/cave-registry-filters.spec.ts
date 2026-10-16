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
