// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount } from './arrange.ts';
import { gotoRoute, login, mapAddress, pixelOnMap } from './helpers.ts';

/**
 * The map's own filter written in the condition editor: opened from the map, offering the feature
 * world's fields, applied as a document the map's layers are asked with,
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

/** The first point the entrance layer answered with, as longitude and latitude. */
async function firstAnsweredPoint(page: Page, which: (address: URL) => boolean) {
  const response = await page.waitForResponse(
    (r) => r.ok() && new URL(r.url()).pathname === '/api/v1/map/cave-entrances' && which(new URL(r.url())),
    { timeout: 60_000 },
  );
  const body = (await response.json()) as {
    features: { geometry: { coordinates: [number, number] }; properties: { cluster?: boolean; count?: number } }[];
  };
  const count = body.features.reduce(
    (sum, f) => sum + (f.properties.cluster === true ? Number(f.properties.count ?? 0) : 1),
    0,
  );
  return { count, points: body.features.map((f) => f.geometry.coordinates) };
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

    // ---- the editor offers the feature fields, the type and the position among them ----
    await page.getByTestId('map-filter-edit').click();
    await expect(page.getByTestId('filter-editor')).toBeVisible();
    await page.getByTestId('filter-editor-add-row-root').click();
    await page.getByTestId('filter-editor-field-0').click();
    // The list is long and virtual, so each field is looked for by typing a part of its name.
    const options = page.locator('.ant-select-dropdown:visible .ant-select-item-option');
    await page.keyboard.type('Posit');
    await expect(options.filter({ hasText: /^Position$/ })).toHaveCount(1);
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

test('the map offers its own places to a spatial condition: its view, an outline drawn on it', async ({ page }) => {
  test.slow();
  const member = await registerAccount(page.request, 'mapspatial');
  await login(page, member.email, member.password);

  await page.goto('about:blank');
  const everything = firstAnsweredPoint(page, unfiltered);
  await gotoRoute(page, mapAddress(COUNTRY));
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
  const { count: all, points } = await everything;
  expect(all).toBeGreaterThan(0);

  // The outline is drawn around one entrance the layer answered with, so it holds at least that
  // one, and small enough to leave out its neighbours — an outline drawn at a fixed place could
  // hold none, and "fewer than all" would still pass. The entrance taken is the one farthest from
  // its nearest neighbour, and the map is brought close enough that the neighbour is at least
  // fifty pixels off: at the country's zoom the demonstration caves sit a few pixels apart.
  const onScreen = await Promise.all(points.map(([lon, lat]) => pixelOnMap(page, COUNTRY, lat, lon)));
  const apart = (a: { x: number; y: number }, b: { x: number; y: number }) =>
    Math.max(Math.abs(a.x - b.x), Math.abs(a.y - b.y));
  const nearestOther = onScreen.map((here, i) =>
    Math.min(...onScreen.filter((_, j) => j !== i).map((there) => apart(here, there)), Number.POSITIVE_INFINITY));
  const loneliest = nearestOther.indexOf(Math.max(...nearestOther));
  expect(points.length, 'the outline needs a neighbour to leave out').toBeGreaterThan(1);
  const closer = Math.min(6, Math.max(0, Math.ceil(Math.log2(50 / Math.max(1, nearestOther[loneliest])))));
  const [lon, lat] = points[loneliest];
  const CLOSE = { lat, lon, zoom: COUNTRY.zoom + closer };
  // The camera is in the address's fragment, so going straight from one map address to another
  // is a move within the same page that loads nothing; leaving the page first makes it a load.
  await page.goto('about:blank');
  const here = entrancesAnswered(page, unfiltered);
  await gotoRoute(page, mapAddress(CLOSE));
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
  const inView = await here;

  // ---- a position row, inside the map view ----
  await page.getByTestId('map-filter-edit').click();
  await page.getByTestId('filter-editor-add-row-root').click();
  await page.getByTestId('filter-editor-field-0').click();
  await page.keyboard.type('Posit');
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^Position$/ }).click();
  await page.getByTestId('filter-editor-op-0').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^inside$/ }).click();
  await page.getByTestId('filter-editor-value-0-outline-kind').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^the map view$/ }).click();
  await expect(page.getByTestId('filter-editor-value-0-outline-set')).toContainText('4');

  // ---- an outline drawn on the map replaces it: the editor steps aside for the drawing ----
  await page.getByTestId('filter-editor-value-0-outline-kind').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^an outline drawn on the map$/ }).click();
  await page.getByTestId('filter-editor-value-0-draw').click();
  await expect(page.getByTestId('filter-editor')).toBeHidden();
  const centre = await pixelOnMap(page, CLOSE, lat, lon);
  // Never wider than half the way to the neighbour, and never so small that a corner falls within
  // the drawing's own snapping distance of the first and closes the outline early.
  const half = Math.min(25, Math.floor((nearestOther[loneliest] * 2 ** closer) / 2));
  expect(half, 'the entrance cannot be outlined alone even this close').toBeGreaterThanOrEqual(10);
  const corners = [
    [centre.x - half, centre.y - half],
    [centre.x + half, centre.y - half],
    [centre.x + half, centre.y + half],
  ];
  for (const [x, y] of corners) {
    await page.mouse.click(x, y);
  }
  await page.mouse.dblclick(centre.x - half, centre.y + half);
  await expect(page.getByTestId('filter-editor')).toBeVisible();
  await expect(page.getByTestId('filter-editor-value-0-outline-set')).toBeVisible();

  // ---- applied: the layer is asked with the outline, keeps fewer entrances, and the filter is not
  // ---- one to store ----
  const narrowing = entrancesAnswered(page, withDocument);
  await page.getByTestId('filter-editor-apply').click();
  const inside = await narrowing;
  expect(inside, 'the outline was drawn around an entrance, so it holds at least that one').toBeGreaterThan(0);
  expect(inside, 'the outline leaves out the neighbour in view').toBeLessThan(inView);
  await page.getByTestId('map-filter-edit').click();
  await expect(page.getByTestId('map-filter-save')).toBeDisabled();
  await page.getByTestId('filter-editor-cancel').click();
  await expect(page.getByTestId('filter-editor')).toBeHidden();

  // Back to the whole country with no filter, so what the next condition keeps is its own doing
  // and is counted against everything the layer holds.
  await page.goto('about:blank');
  const unnarrowed = entrancesAnswered(page, unfiltered);
  await gotoRoute(page, mapAddress(COUNTRY));
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
  await unnarrowed;

  // ---- the selected object: picked in the map's selector, it is both what a distance is measured
  // ---- from and what the selector ranks nearest first from ----
  await page.getByTestId('map-object-selector').click();
  await page.getByTestId('map-object-selector-input').fill('Demo');
  await expect(page.getByTestId('map-object-selector-row').first()).toBeVisible({ timeout: 30_000 });
  await page.getByTestId('map-object-selector-row').first().click();
  // Nothing was selected before the pick, so nearest first appears only now.
  await page.getByTestId('map-object-selector').click();
  await page.getByTestId('map-object-selector-input').fill('Demo');
  // Nearest first is offered only where every kind searched can be placed, so narrow to caves.
  await page.getByTestId('map-object-selector-scope-caves').click();
  const nearest = page.getByTestId('map-object-selector-sort-proximity');
  await expect(nearest).toBeVisible({ timeout: 30_000 });
  const ranked = page.waitForRequest(
    (r) => r.url().includes('/api/v1/filters/query') && (r.postData() ?? '').includes('"anchor"'),
    { timeout: 30_000 },
  );
  await nearest.click();
  const asked = JSON.parse((await ranked).postData() ?? '{}') as {
    document?: { sort?: string; anchor?: { anchor?: string } };
  };
  expect(asked.document?.sort).toBe('proximity');
  expect(asked.document?.anchor?.anchor).toBe('object');
  // Escape would close the selector but also clear the map's selection, which the next step needs,
  // so the selector's own button closes it.
  await page.getByTestId('map-object-selector').click();
  await expect(nearest).toBeHidden();
  await page.getByTestId('map-filter-edit').click();
  await page.getByTestId('filter-editor-add-row-root').click();
  await page.getByTestId('filter-editor-field-0').click();
  await page.keyboard.type('Posit');
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^Position$/ }).click();
  await page.getByTestId('filter-editor-op-0').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^within$/ }).click();
  await page.getByTestId('filter-editor-value-0-radius').fill('1000');
  await page.getByTestId('filter-editor-value-0-anchor-kind').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^the selected object$/ }).click();
  await expect(page.getByTestId('filter-editor-value-0-selection')).toBeVisible();

  // ---- applied: a kilometre of the selected cave keeps its own entrance and not the country's ----
  // The address carries the document base64url-encoded; the answer waited for is the one asked with
  // the within condition, not a late answer to the earlier outline.
  const asksWithin = (address: URL) =>
    withDocument(address)
    && Buffer.from(address.searchParams.get('filter') ?? '', 'base64url').toString('utf8').includes('"within"');
  const near = entrancesAnswered(page, asksWithin);
  await page.getByTestId('filter-editor-apply').click();
  const kept = await near;
  expect(kept, 'the selected cave is within a kilometre of itself').toBeGreaterThan(0);
  expect(kept).toBeLessThan(all);
});
