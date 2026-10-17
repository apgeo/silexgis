// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * A position condition written in the cave registry's advanced filter: "within a distance of an
 * object" and "inside a pasted polygon", each narrowing the list, kept in the address, and — only
 * where it names an object rather than a place — kept as a saved filter that another visit
 * applies again.
 *
 * What only a browser can show is the road: that the object is picked through the selector and
 * sent as an object rather than as its position, that the server accepts what the editor writes,
 * that the list narrows on it, and that a polygon typed by hand is never offered for saving. Who
 * may be measured, and by which position, is the server's to prove and its tests prove it — a
 * protected cave is tested by the point it is shown at, never by its true one.
 *
 * Nearest first is chosen in the same editor's order control, measured from a typed point, and
 * orders the registry's own list; turning a page keeps that order.
 *
 * The demonstration data fixes the answer. "Peștera Demo Mică" and "Peștera Demo Mare" are less
 * than a kilometre apart near Brașov; "Peștera Demo Ursului" is in Bihor, hundreds of kilometres
 * away. A search for "Demo" lists all three, so a dropped spatial condition would show Ursului.
 */
const ANCHOR = 'Peștera Demo Mică';
const NEAR = 'Peștera Demo Mare';
const FAR = 'Peștera Demo Ursului';

/**
 * Around the two Brașov demonstration caves and nowhere near Bihor. Invented, like the data it is
 * drawn around.
 */
const OUTLINE = JSON.stringify({
  type: 'Polygon',
  coordinates: [[[25.43, 45.52], [25.46, 45.52], [25.46, 45.54], [25.43, 45.54], [25.43, 45.52]]],
});

async function choose(page: Page, testId: string, option: RegExp, search?: string) {
  await page.getByTestId(testId).click();
  if (search !== undefined) {
    await page.keyboard.type(search);
  }
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: option }).first().click();
}

async function editorCount(page: Page): Promise<number> {
  const count = page.getByTestId('filter-editor-count');
  await expect(count).toHaveText(/Matches: \d+/, { timeout: 30_000 });
  return Number((await count.textContent())!.replace(/\D/g, ''));
}

function documentIn(url: string): string {
  const token = new URL(url).searchParams.get('filter');
  expect(token).toBeTruthy();
  return Buffer.from(token!, 'base64url').toString('utf8');
}

/** The registry narrowed to the demonstration caves by its own search box. */
async function demoCaves(page: Page) {
  await gotoRoute(page, '/caves');
  await page.getByPlaceholder('Search name or toponym…').fill('Demo');
  await expect(page).toHaveURL(/[?&]filter=/);
  await expect(page.getByRole('cell', { name: FAR, exact: true })).toBeVisible({ timeout: 30_000 });
}

/**
 * Opens the advanced filter over the search the registry already holds and starts a position row
 * beside it. The search is the editor's first entry, so the new row is the second.
 */
async function startPositionRow(page: Page, op: RegExp): Promise<number> {
  await page.getByTestId('cave-registry-advanced').click();
  await expect(page.getByTestId('filter-editor')).toBeVisible();
  const before = await editorCount(page);
  await page.getByTestId('filter-editor-add-row-root').click();
  await choose(page, 'filter-editor-field-1', /^Position$/, 'Position');
  await choose(page, 'filter-editor-op-1', op);
  return before;
}

async function expectTheNearRows(page: Page) {
  await expect(page.getByRole('cell', { name: NEAR, exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('cell', { name: ANCHOR, exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: FAR, exact: true })).toHaveCount(0);
}

test('a distance from a picked cave narrows the registry, and is saved and applied again', async ({ page }) => {
  test.slow();
  const filterName = `E2E spatial filter ${Date.now()}`;

  await login(page);
  await demoCaves(page);
  const before = await startPositionRow(page, /^within$/);

  // ---- within 2 km of a cave picked through the selector ----
  await page.getByTestId('filter-editor-value-1-radius').fill('2000');
  await page.getByTestId('filter-editor-value-1-object').click();
  await page.getByTestId('filter-editor-value-1-object-input').fill('Demo Mică');
  const row = page.getByTestId('filter-editor-value-1-object-row').filter({ hasText: ANCHOR });
  await expect(row.first()).toBeVisible({ timeout: 15_000 });
  await row.first().click();
  await expect(page.getByTestId('filter-editor-value-1-object')).toContainText(ANCHOR);

  await expect.poll(() => editorCount(page), { timeout: 30_000 }).toBeLessThan(before);
  expect(await editorCount(page)).toBeGreaterThanOrEqual(2);
  await page.getByTestId('filter-editor-apply').click();
  await expect(page.getByTestId('filter-editor')).toHaveCount(0);

  // ---- the list, and an address that names the object rather than where it is ----
  await expectTheNearRows(page);
  const written = documentIn(page.url());
  expect(written).toContain('"field":"position"');
  expect(written).toContain('"op":"within"');
  expect(written).toContain('"type":"object"');
  expect(written).not.toContain('"type":"point"');

  const address = new URL(page.url());
  await gotoRoute(page, `${address.pathname}${address.search}`);
  await expectTheNearRows(page);

  // ---- an object is something a saved filter may hold ----
  const save = page.getByTestId('cave-registry-save-filter');
  await expect(save).toBeEnabled();
  await save.click();
  await page.getByTestId('saved-filter-name').fill(filterName);
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByTestId('cave-registry-active-filter')).toContainText(filterName);

  // ---- applied on a fresh visit, it narrows the same way ----
  await gotoRoute(page, '/caves');
  await page.getByTestId('cave-registry-apply-saved').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: filterName })
    .click();
  await expect(page.getByTestId('cave-registry-active-filter')).toContainText(filterName);
  await expectTheNearRows(page);

  // ---- this run removes what it made ----
  await page.getByTestId('cave-registry-manage-filters').click();
  await expect(page).toHaveURL(/\/saved-filters$/);
  const saved = page.getByRole('row').filter({ hasText: filterName });
  await saved.getByRole('button', { name: 'Delete' }).click();
  await page.locator('.ant-popconfirm').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByRole('row').filter({ hasText: filterName })).toHaveCount(0);
});

test('a pasted polygon narrows the registry and is never offered for saving', async ({ page }) => {
  test.slow();

  await login(page);
  await demoCaves(page);
  const before = await startPositionRow(page, /^inside$/);

  await choose(page, 'filter-editor-value-1-outline-kind', /^a pasted polygon$/);
  const pasted = page.getByTestId('filter-editor-value-1-polygon');
  // Something that is not an outline is named as such before anything is sent.
  await pasted.fill('{"type":"Point","coordinates":[25.44,45.53]}');
  await expect(page.getByTestId('filter-editor-value-1-polygon-problem')).toHaveText('This is not a single polygon.');
  await pasted.fill(OUTLINE);
  await expect(page.getByTestId('filter-editor-value-1-polygon-problem')).toHaveCount(0);

  await expect.poll(() => editorCount(page), { timeout: 30_000 }).toBeLessThan(before);
  await page.getByTestId('filter-editor-apply').click();
  await expect(page.getByTestId('filter-editor')).toHaveCount(0);

  await expectTheNearRows(page);
  const written = documentIn(page.url());
  expect(written).toContain('"op":"inside"');
  expect(written).toContain('"type":"polygon"');

  // A polygon typed by hand is a place of its own, and a saved filter never stores one: the
  // button says so instead of letting a name be typed for a save the server would refuse.
  const save = page.getByTestId('cave-registry-save-filter');
  await expect(save).toBeDisabled();
  await save.hover({ force: true });
  await expect(page.getByRole('tooltip')).toContainText(
    'A typed point, a pasted polygon or a place to measure nearest first from is never saved.');
});

/** The names in the registry's table, top to bottom. */
async function listedNames(page: Page): Promise<string[]> {
  return page.locator('.ant-table-tbody tr.ant-table-row td:first-child').allTextContents();
}

async function nearestFrom(page: Page, lon: number, lat: number) {
  await page.getByTestId('cave-registry-advanced').click();
  await expect(page.getByTestId('filter-editor')).toBeVisible();
  await choose(page, 'filter-sort-key', /^Nearest$/);
  await choose(page, 'filter-sort-anchor-kind', /^a point$/);
  await page.getByTestId('filter-sort-anchor-lon').fill(String(lon));
  await page.getByTestId('filter-sort-anchor-lat').fill(String(lat));
  await expect(page.getByTestId('filter-editor-needs-anchor')).toHaveCount(0);
  await page.getByTestId('filter-editor-apply').click();
  await expect(page.getByTestId('filter-editor')).toHaveCount(0);
}

test('nearest first from a typed point orders the registry, and the address keeps the place', async ({ page }) => {
  test.slow();

  await login(page);
  await demoCaves(page);

  // From near Brașov the two caves there come first and the one in Bihor last...
  await nearestFrom(page, 25.445, 45.53);
  await expect(page).toHaveURL(/[?&]sort=proximity/);
  await expect(page).toHaveURL(/[?&]near=25\.445%2C45\.53/);
  await expect.poll(async () => (await listedNames(page)).at(-1), { timeout: 30_000 }).toBe(FAR);
  expect((await listedNames(page)).slice(0, 2).sort()).toEqual([ANCHOR, NEAR].sort());

  // ...and from the far west of the country the Bihor cave comes first.
  await nearestFrom(page, 21.9, 46.5);
  await expect.poll(async () => (await listedNames(page))[0], { timeout: 30_000 }).toBe(FAR);

  // The place is part of the request, never of the question a saved filter would keep.
  expect(documentIn(page.url())).not.toContain('"anchor"');

  // The address alone reproduces the order.
  const address = new URL(page.url());
  await gotoRoute(page, `${address.pathname}${address.search}`);
  await expect.poll(async () => (await listedNames(page))[0], { timeout: 30_000 }).toBe(FAR);
});
