// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * A camp's own page, driven the way somebody arrives at one: by address.
 *
 * The tab is in the address on purpose — so a section of a camp is a place somebody can link to
 * and one that survives a reload — and the map is the tab that has to be told it became visible,
 * because a map built against a pane nobody is looking at measures nothing and draws a blank tile
 * grid that never repairs itself. Both are checked here rather than argued about in a comment.
 */

/**
 * Signs in and hands back the token the running application is holding, captured off a request it
 * made itself: the session lives in memory, so there is no cookie or stored token to borrow.
 */
async function authorizedHeader(page: Page): Promise<string> {
  let authorization: string | undefined;
  page.on('request', (request) => {
    const header = request.headers()['authorization'];
    if (header && request.url().includes('/api/v1/')) {
      authorization = header;
    }
  });

  await login(page);
  await expect.poll(() => authorization, { timeout: 30_000 }).toBeTruthy();
  return authorization!;
}

/**
 * A camp out of the seeded data, with the working area drawn on it.
 *
 * The id is asked for over the API rather than clicked to out of the list, because these tests
 * are about what a camp's own page does and a click path through a filtered list would make each
 * of them depend on that list's ordering as well.
 */
async function seededCamp(page: Page): Promise<{ id: string; name: string }> {
  const authorization = await authorizedHeader(page);

  const response = await page.request.get('/api/v1/expeditions?pageSize=50', {
    headers: { authorization },
  });
  expect(response.ok()).toBe(true);
  const body = (await response.json()) as { items: { id: string; name: string; geom: unknown }[] };
  // The one with a working area: it is what the map tab has to draw for this to prove anything.
  const camp = body.items.find((x) => x.geom !== null) ?? body.items[0];
  expect(camp, 'the seeded data holds no camp to open').toBeTruthy();
  return camp;
}

test('a camp opens at the tab its address names, and keeps it across a reload', async ({ page }) => {
  const camp = await seededCamp(page);

  await gotoRoute(page, `/expeditions/${camp.id}`);
  await expect(page.getByTestId('expedition-name')).toHaveText(camp.name);
  // No tab in the address is the page's own first tab, and the address stays clean.
  await expect(page.getByTestId('expedition-trips-tab')).toBeVisible({ timeout: 15_000 });

  await page.goto(`/expeditions/${camp.id}?tab=roster`);
  await expect(page.getByTestId('expedition-roster-tab')).toBeVisible({ timeout: 15_000 });

  // A reload is the whole point of the tab living in the address rather than in memory.
  await page.reload();
  await expect(page.getByTestId('expedition-roster-tab')).toBeVisible({ timeout: 15_000 });
  expect(new URL(page.url()).searchParams.get('tab')).toBe('roster');
});

test("a camp's map draws once its tab is the one on screen", async ({ page }) => {
  const camp = await seededCamp(page);

  await page.goto(`/expeditions/${camp.id}?tab=map`);
  await expect(page.getByTestId('expedition-map-tab')).toBeVisible({ timeout: 15_000 });

  // A map that measured nothing renders no canvas of its own and no attribution; a drawn one
  // has both. This is the assertion that would have caught a map built in a hidden pane.
  const canvas = page.getByTestId('expedition-map').locator('canvas');
  await expect(canvas).toBeVisible({ timeout: 30_000 });
  const box = await canvas.boundingBox();
  expect(box!.width).toBeGreaterThan(100);
  expect(box!.height).toBeGreaterThan(100);

  // Said on the screen, not only in the response: the same camp draws differently for two
  // readers, and a map without that sentence gets reported as missing data.
  await expect(page.getByText(/Drawn over the trips you may read/)).toBeVisible();
});

test('a camp is found in the search box and opens on its own page', async ({ page }) => {
  const camp = await seededCamp(page);

  await gotoRoute(page, '/map');
  // The name is typed the way somebody looking for a camp types it: the leading word of it,
  // not an id. What this proves that a unit test cannot is that the section the server grew and
  // the section the box renders are the same one — they are registered in two hand-written
  // places with nothing tying them together.
  // By its placeholder rather than by role: the first combobox on this page is the language
  // selector, which is readonly and would swallow the typing.
  const box = page.getByPlaceholder(/Search features, trips or places/);
  await box.click();
  await box.pressSequentially(camp.name.slice(0, 18), { delay: 30 });
  const option = page.getByText(camp.name, { exact: false }).last();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();

  // A camp is not a feature, so a hit routed like one would ask the resolver about a place that
  // does not exist and leave the reader on an error message.
  await expect(page).toHaveURL(new RegExp(`/expeditions/${camp.id}`), { timeout: 20_000 });
  await expect(page.getByTestId('expedition-name')).toHaveText(camp.name);
});

test('the camp list narrows by name and by how far along a camp is', async ({ page }) => {
  const camp = await seededCamp(page);

  await gotoRoute(page, '/expeditions');
  const rows = page.locator('.ant-table-tbody tr.ant-table-row');
  await expect(rows.first()).toBeVisible({ timeout: 20_000 });
  const before = await rows.count();

  // Typed the way somebody narrows a list they are looking at, and debounced on the way to the
  // server: the assertion has to wait for the settled request rather than the keystroke, which is
  // why it polls instead of reading the table once.
  await page.getByTestId('expedition-search').locator('input').fill(camp.name.slice(0, 12));
  await expect.poll(() => rows.count(), { timeout: 20_000 }).toBeLessThanOrEqual(before);
  await expect(page.getByRole('cell', { name: camp.name, exact: true })).toBeVisible({
    timeout: 20_000,
  });

  // The state filter is the second hand-rolled narrowing, and it is the one that would silently
  // do nothing if the query string it writes and the one the server reads ever drifted apart.
  // Every row left has to carry the state that was asked for — checking only "some row matched"
  // would pass against a filter the server ignored entirely.
  await page.getByTestId('expedition-search').locator('input').fill('');
  await page.getByTestId('expedition-state-filter').click();
  // A state the seeded data actually holds. Filtering on one nothing carries would settle to an
  // empty table, and a loop over no rows asserts nothing at all.
  await page.getByTitle('Confirmed', { exact: true }).click();
  // The previous rows stay on screen while the narrowed request is in flight, so the settled
  // answer has to be waited for rather than read once: a snapshot taken now would be the
  // unfiltered table. Polling on "some row, and every row, says Confirmed" is both the wait and
  // the assertion — it cannot be satisfied by the stale table or by an empty one.
  const stateCells = page.locator('.ant-table-tbody tr.ant-table-row td:nth-child(3)');
  await expect
    .poll(
      async () => {
        const states = await stateCells.allTextContents();
        return states.length > 0 && states.every((state) => state.includes('Confirmed'));
      },
      { timeout: 20_000 },
    )
    .toBe(true);
});

test('a camp turns up in what happened lately and opens from there', async ({ page }) => {
  // The feed merges camps with trips and features and then keeps only the most recent few, so a
  // seeded camp may sit below the cut through no fault of the registry. A camp made here is the
  // newest thing there is, which turns "is a camp in the feed" into a question with one answer.
  const authorization = await authorizedHeader(page);
  const name = `Feed camp ${Date.now()}`;
  const created = await page.request.post('/api/v1/expeditions', {
    headers: { authorization },
    data: { name, startDate: '2026-03-02', visibility: 'authenticated' },
  });
  expect(created.ok(), await created.text()).toBe(true);
  const { id } = (await created.json()) as { id: string };

  await gotoRoute(page, '/dashboard');
  // The feed and the search box are two separate hand-written registries with nothing tying them
  // to each other; a camp missing from either is the kind of gap that gets reported as a bug
  // rather than noticed here. This drives the feed's own row, not the API behind it.
  const row = page.locator('.silex-list-item', { hasText: name });
  await expect(row).toBeVisible({ timeout: 30_000 });
  // The row says what kind of thing it is, which is the half of the registration that lives in
  // the client: a kind the client has no label for renders a blank where the word should be.
  await expect(row).toContainText('Camp');

  // A camp is not a feature, so a row routed through the feature resolver would land on an error
  // message instead of the camp — the reason the feed answers this kind before its fallthrough.
  await row.click();
  await expect(page).toHaveURL(new RegExp(`/expeditions/${id}`), { timeout: 20_000 });
  await expect(page.getByTestId('expedition-name')).toHaveText(name);
});

test("a camp's leads board is a tab of its own and says whose board it is", async ({ page }) => {
  const camp = await seededCamp(page);

  await page.goto(`/expeditions/${camp.id}?tab=leads`);
  await expect(page.getByTestId('expedition-leads-tab')).toBeVisible({ timeout: 15_000 });

  // The board is either a list of leads or the sentence explaining there are none to show;
  // whichever it is, the caveat that this is one reader's answer is on the screen. Asserting
  // only "the tab rendered" would pass against a pane that failed to load its own contents.
  const board = page.getByTestId('expedition-leads-count');
  const empty = page.getByTestId('expedition-leads-empty');
  await expect(board.or(empty)).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(/Read as visible to you/)).toBeVisible();

  // The tab lives in the address like every other one, so a board somebody found is a board
  // they can send to the person who has to go back and push it.
  await page.reload();
  await expect(page.getByTestId('expedition-leads-tab')).toBeVisible({ timeout: 15_000 });
  expect(new URL(page.url()).searchParams.get('tab')).toBe('leads');
});
