// SPDX-License-Identifier: AGPL-3.0-or-later
import { randomUUID } from 'node:crypto';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { uniquePng } from './png.ts';

/**
 * The gallery's conditions, kept in the address and kept as a saved filter another visit applies.
 *
 * What only a browser can show is the road between them: that a condition written in the gallery's
 * editor is what the address holds, so a reload — a fresh sign-in round trip with nothing in memory
 * — draws the same pictures; that the gallery's own controls survive beside it; and that the
 * conditions saved on one visit narrow the gallery again on the next and are named while they are in
 * force. The server's tests prove what the photograph world returns and to whom.
 *
 * The gallery's search box writes its condition into the same document as the editor's, so a saved
 * view keeps it: a visit that has searched for nothing applies the saved view and is narrowed to this
 * run's pictures again, with the search box showing the search it brought back.
 *
 * Every picture is generated with pixels and names unique to the run, so other runs' pictures
 * cannot drift into the counts and the archive never refuses a repeat upload.
 */
test.describe.configure({ timeout: 180_000 });

/** Uploads into the inbox and waits for the archive to have taken every file. */
async function upload(page: Page, files: { name: string; mimeType: string; buffer: Buffer }[]) {
  await gotoRoute(page, '/cabinets');
  const inbox = page.locator('.ant-tree-title').filter({ hasText: 'Not filed' }).first();
  await expect(inbox).toBeVisible({ timeout: 45_000 });
  await inbox.click();
  const uploadButton = page.getByRole('button', { name: 'Upload', exact: true });
  await expect(uploadButton).toBeVisible({ timeout: 30_000 });
  await uploadButton.click();
  const drawer = page.getByRole('dialog').last();
  await drawer.getByTestId('upload-files').locator('input[type=file]').setInputFiles(files);
  await expect(drawer.getByText(`${files.length} of ${files.length} stored`, { exact: false }))
    .toBeVisible({ timeout: 90_000 });
  await drawer.getByTestId('upload-done').click();
}

/** Searches the gallery from its search box, which writes the search into the gallery's filter. */
async function search(page: Page, text: string) {
  const box = page.getByPlaceholder('Search captions and places');
  await box.fill(text);
  await box.press('Enter');
  await expect(page).toHaveURL(/[?&]filter=/);
}

/** Opens a gallery address and waits past the sign-in round trip for the page itself. */
async function openGallery(page: Page, address: string) {
  await gotoRoute(page, address);
  await expect(page.getByRole('heading', { name: 'Photographs' })).toBeVisible({ timeout: 45_000 });
}

/** The names of the pictures drawn, sorted, once the grid holds exactly `count` of them. */
async function shownNames(page: Page, count: number): Promise<string[]> {
  const tiles = page.getByTestId('photo-tile');
  await expect(tiles).toHaveCount(count, { timeout: 30_000 });
  const names = await tiles.locator('img').evaluateAll((images) =>
    images.map((image) => image.getAttribute('alt') ?? ''));
  return names.sort();
}

test('the gallery keeps its conditions in the address and saves them as a filter', async ({ page }) => {
  test.slow();
  const run = randomUUID().slice(0, 8);
  const chosen = [`gfl-${run}-cave-1.png`, `gfl-${run}-cave-2.png`];
  const other = `gfl-${run}-camp-1.png`;
  const filterName = `E2E gallery filter ${run}`;

  await login(page);
  await upload(page, [...chosen, other].map((name) => ({ name, mimeType: 'image/png', buffer: uniquePng() })));

  // ---- the gallery's own control: this run's pictures, all three ----
  await openGallery(page, '/gallery');
  await search(page, run);
  expect(await shownNames(page, 3)).toEqual([...chosen, other].sort());

  // ---- a condition from the editor: title contains "-cave-" ----
  const opener = page.getByTestId('gallery-advanced');
  await opener.click();
  await expect(page.getByTestId('filter-editor')).toBeVisible();
  // The search box's condition is the first row group; the new condition goes beside it.
  await page.getByTestId('filter-editor-add-row-root').click();
  await page.getByTestId('filter-editor-field-1').click();
  await page.keyboard.type('Title');
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: /^Title$/ }).first().click();
  await page.getByTestId('filter-editor-op-1').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: /^contains$/ }).first().click();
  await page.getByTestId('filter-editor-value-1').fill(`${run}-cave-`);
  await expect(page.getByTestId('filter-editor-count')).toHaveText(/Matches: 2\b/, { timeout: 30_000 });
  await page.getByTestId('filter-editor-apply').click();
  await expect(page.getByTestId('filter-editor')).toHaveCount(0);

  await expect(page).toHaveURL(/[?&]filter=/);
  expect(await shownNames(page, 2)).toEqual([...chosen].sort());
  const token = new URL(page.url()).searchParams.get('filter');
  expect(token).toBeTruthy();

  // ---- the address reproduces the gallery, control and condition alike ----
  const address = new URL(page.url());
  await openGallery(page, `${address.pathname}${address.search}`);
  await expect(page.getByPlaceholder('Search captions and places')).toHaveValue(run);
  expect(await shownNames(page, 2)).toEqual([...chosen].sort());
  expect(new URL(page.url()).searchParams.get('filter')).toBe(token);

  // ---- save the conditions ----
  await page.getByTestId('gallery-save-filter').click();
  await page.getByTestId('gallery-saved-filter-name').fill(filterName);
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByTestId('gallery-active-filter')).toContainText(filterName);
  await expect(page).toHaveURL(/[?&]saved=/);

  // ---- apply them on a fresh visit that has searched for nothing ----
  await openGallery(page, '/gallery');
  await expect(page.getByPlaceholder('Search captions and places')).toHaveValue('');
  await page.getByTestId('gallery-apply-saved').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: filterName })
    .click();
  await expect(page.getByTestId('gallery-active-filter')).toContainText(filterName);
  // The saved view brought the search back with the editor's condition.
  await expect(page.getByPlaceholder('Search captions and places')).toHaveValue(run);
  expect(await shownNames(page, 2)).toEqual([...chosen].sort());
  expect(new URL(page.url()).searchParams.get('filter')).toBe(token);

  // ---- and it is managed from the saved-filter screen, where this run removes it ----
  await page.getByTestId('gallery-manage-filters').click();
  await expect(page).toHaveURL(/\/saved-filters$/);
  const row = page.getByRole('row').filter({ hasText: filterName });
  await expect(row).toBeVisible({ timeout: 30_000 });
  await row.getByRole('button', { name: 'Delete' }).click();
  await page.locator('.ant-popconfirm').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByRole('row').filter({ hasText: filterName })).toHaveCount(0);
});
