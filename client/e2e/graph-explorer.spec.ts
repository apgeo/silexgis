// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount, signedInElsewhere } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The relations graph, driven the way a person drives it: opened by address on one item, read
 * through its list, one neighbour chosen and expanded, the arrangement switched.
 *
 * What this shows that the unit tests cannot. The model, the view and the toolbar are covered
 * against a fake renderer; only a real canvas can show that growing the graph leaves the nodes
 * already drawn where they were, which is the property that makes expanding usable. Positions are
 * read through the test hook the canvas exposes to an automated browser, so nothing here looks at
 * pixels.
 *
 * The network is built through the API for this run and named with a stamp, so the flow depends on
 * nothing the demo seeder leaves behind:
 *
 *   seed ── near ── far
 *     └──── side
 *
 * The seed's first hop is `near` and `side`; `far` appears only after `near` is expanded.
 */

type Positions = Record<string, { x: number; y: number }>;

interface Network {
  seed: string;
  near: string;
  far: string;
  side: string;
  ids: string[];
}

async function createCave(page: Page, name: string, region: string, visibility = 'authenticated'): Promise<string> {
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name,
    caveTypeId: caveTypes[0].id,
    region,
    visibility,
    locationProtected: false,
    explorationStatus: 'Unknown',
    isShowCave: false,
  });
  return cave.id;
}

async function relate(page: Page, relationTypeId: number, from: string, to: string): Promise<void> {
  // The general relation is undirected, and an undirected link may not mark a main member.
  const member = (targetId: string, sortOrder: number) => ({
    targetType: 'feature',
    targetId,
    isMain: false,
    sortOrder,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
  });
  await asPerson(page, 'POST', '/api/v1/reslinks', {
    relationTypeId,
    description: null,
    members: [member(from, 0), member(to, 1)],
  });
}

async function arrangeNetwork(page: Page, stamp: string): Promise<Network> {
  const region = `E2E graph region ${stamp}`;
  const [seed, near, far, side] = [
    await createCave(page, `E2E graph seed ${stamp}`, region),
    await createCave(page, `E2E graph near ${stamp}`, region),
    await createCave(page, `E2E graph far ${stamp}`, region),
    await createCave(page, `E2E graph side ${stamp}`, region),
  ];
  const relationTypes = await asPerson<{ id: number; code: string }[]>(page, 'GET', '/api/v1/reslinks/relation-types');
  const related = relationTypes.find((row) => row.code === 'related-to');
  expect(related, 'the seeded general relation is what this network is drawn with').toBeTruthy();
  await relate(page, related!.id, seed, near);
  await relate(page, related!.id, seed, side);
  await relate(page, related!.id, near, far);
  return { seed, near, far, side, ids: [seed, near, far, side] };
}

const canvas = (page: Page) => page.getByTestId('graph-canvas');
const keyOf = (id: string) => `feature:${id}`;
const listItem = (page: Page, id: string) => page.locator(`[data-testid="graph-list-item"][data-key="${keyOf(id)}"]`);

async function positions(page: Page): Promise<Positions> {
  return page.evaluate(() => (window as unknown as { __silexgisGraph: { positions: () => Positions } }).__silexgisGraph.positions());
}

/** The drawing has stopped moving: two reads a moment apart agree to within a pixel. */
async function atRest(page: Page): Promise<Positions> {
  let previous = await positions(page);
  await expect
    .poll(
      async () => {
        const now = await positions(page);
        const still =
          Object.keys(now).length === Object.keys(previous).length &&
          Object.entries(now).every(([key, p]) => previous[key] && Math.hypot(p.x - previous[key].x, p.y - previous[key].y) < 1);
        previous = now;
        return still;
      },
      { timeout: 30_000, intervals: [500] },
    )
    .toBe(true);
  return previous;
}

test.describe('relations graph', () => {
  test.slow();

  test('opens on an item, lists its neighbours, expands one without moving the rest, and switches arrangement', async ({ page }) => {
    const stamp = `${Date.now()}${String(test.info().parallelIndex).padStart(2, '0')}`;
    await login(page);
    const network = await arrangeNetwork(page, stamp);
    try {
      await gotoRoute(page, `/graph?type=feature&id=${network.seed}`);

      // The first hop: the seed and the two things linked to it, nothing further.
      await expect(canvas(page)).toHaveAttribute('data-node-count', '3', { timeout: 30_000 });
      await expect(canvas(page)).toHaveAttribute('data-ready', 'true', { timeout: 30_000 });
      // The drawing, not just the model, holds all three: a first arrangement still under way would move them later.
      await expect(canvas(page)).toHaveAttribute('data-drawn-nodes', '3', { timeout: 30_000 });
      await expect(listItem(page, network.seed)).toBeVisible();
      await expect(listItem(page, network.near)).toBeVisible();
      await expect(listItem(page, network.side)).toBeVisible();
      await expect(listItem(page, network.far)).toHaveCount(0);
      await expect(listItem(page, network.near)).toContainText(`E2E graph near ${stamp}`);

      // Choosing a row does what pressing the node does: it is the selection.
      await listItem(page, network.near).getByTestId('graph-list-select').click();
      await expect(canvas(page)).toHaveAttribute('data-selected', keyOf(network.near));

      const before = await atRest(page);
      expect(Object.keys(before).sort()).toEqual([network.seed, network.near, network.side].map(keyOf).sort());

      // Expanding the selected neighbour brings its own neighbour in and leaves the rest where they were.
      await page.getByTestId('graph-expand').click();
      await expect(canvas(page)).toHaveAttribute('data-node-count', '4', { timeout: 30_000 });
      await expect(listItem(page, network.far)).toBeVisible();
      await expect(canvas(page)).toHaveAttribute('data-drawn-nodes', '4', { timeout: 30_000 });
      const after = await positions(page);
      expect(Object.keys(after)).toContain(keyOf(network.far));
      const role: Record<string, string> = {
        [keyOf(network.seed)]: 'seed',
        [keyOf(network.near)]: 'near',
        [keyOf(network.side)]: 'side',
      };
      const moved = Object.entries(before).map(([key, was]) => {
        expect(after[key], `${key} is still drawn`).toBeTruthy();
        return { role: role[key], distance: Math.hypot(after[key].x - was.x, after[key].y - was.y), was, now: after[key] };
      });
      expect(moved.filter((m) => m.distance > 2), `nodes that moved when the graph grew: ${JSON.stringify(moved)}`).toEqual([]);

      // A different arrangement re-places the nodes; the graph itself is unchanged.
      const settled = await atRest(page);
      await page.getByTestId('graph-toolbar').getByText('Radial', { exact: true }).click();
      await expect(canvas(page)).toHaveAttribute('data-layout', 'radial');
      await expect(canvas(page)).toHaveAttribute('data-node-count', '4');
      await expect
        .poll(
          async () => {
            const now = await positions(page);
            return Object.keys(settled).some((key) => Math.hypot(now[key].x - settled[key].x, now[key].y - settled[key].y) > 2);
          },
          { timeout: 30_000 },
        )
        .toBe(true);
    } finally {
      for (const id of network.ids) {
        await asPerson(page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
      }
    }
  });

  test('opens from a cave page, hides a kind of relation and brings it back, and finds an item by name', async ({ page }) => {
    const stamp = `${Date.now()}${String(test.info().parallelIndex).padStart(2, '0')}`;
    await login(page);
    const network = await arrangeNetwork(page, stamp);
    try {
      // The entry point: the links section of the cave's own page.
      await gotoRoute(page, `/caves/${network.seed}`);
      await page.getByTestId('show-as-graph').first().click();
      await expect(page).toHaveURL(new RegExp(`/graph\\?type=feature&id=${network.seed}`));
      await expect(canvas(page)).toHaveAttribute('data-node-count', '3', { timeout: 30_000 });
      await expect(canvas(page)).toHaveAttribute('data-ready', 'true', { timeout: 30_000 });

      // Several kinds can be switched off at once; here the one kind that joins these items.
      const links = page.locator('[data-testid="graph-filter-category"][data-category="resLink"]');
      await expect(links).toBeVisible();
      await links.getByTestId('graph-filter-category-check').uncheck();
      await expect(canvas(page)).toHaveAttribute('data-node-count', '1', { timeout: 30_000 });
      await expect(listItem(page, network.near)).toHaveCount(0);
      await expect(listItem(page, network.seed)).toBeVisible();
      await links.getByTestId('graph-filter-category-check').check();
      await expect(canvas(page)).toHaveAttribute('data-node-count', '3', { timeout: 30_000 });
      await expect(listItem(page, network.near)).toBeVisible();

      // Searching inside the graph reports the match; a name that is not there reports none.
      await page.getByLabel('Search the graph').fill(`graph side ${stamp}`);
      await expect(page.getByTestId('graph-search-count')).toContainText('1');
      await page.getByLabel('Search the graph').fill(`nothing called this ${stamp}`);
      await expect(page.getByTestId('graph-search-count')).toBeVisible();

      // The details of a chosen item name it and offer its page.
      await listItem(page, network.near).getByTestId('graph-list-select').click();
      await page.getByTestId('graph-details-open-button').click();
      await expect(page.getByTestId('graph-details')).toContainText(`E2E graph near ${stamp}`);
      await expect(page.getByTestId('graph-details-open')).toBeVisible();
    } finally {
      for (const id of network.ids) {
        await asPerson(page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
      }
    }
  });

  test('finds a starting item by name on the empty page', async ({ page }) => {
    const stamp = `${Date.now()}${String(test.info().parallelIndex).padStart(2, '0')}`;
    await login(page);
    const network = await arrangeNetwork(page, stamp);
    try {
      await gotoRoute(page, '/graph');
      await expect(page.getByTestId('graph-no-seed')).toBeVisible();
      await page.getByTestId('graph-no-seed').getByTestId('graph-seed-search-input').fill(`E2E graph seed ${stamp}`);
      await page.locator('.ant-select-dropdown:visible').getByText(`E2E graph seed ${stamp}`).first().click();
      await expect(page).toHaveURL(new RegExp(`/graph\\?type=feature&id=${network.seed}`));
      await expect(canvas(page)).toHaveAttribute('data-node-count', '3', { timeout: 30_000 });
    } finally {
      for (const id of network.ids) {
        await asPerson(page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
      }
    }
  });

  test('shows a viewer a restricted placeholder that cannot be expanded', async ({ page, browser }) => {
    const stamp = `${Date.now()}${String(test.info().parallelIndex).padStart(2, '0')}`;
    await login(page);
    const region = `E2E graph region ${stamp}`;
    const open = await createCave(page, `E2E graph open ${stamp}`, region);
    const readable = await createCave(page, `E2E graph readable ${stamp}`, region);
    const hidden = await createCave(page, `E2E graph hidden ${stamp}`, region, 'private');
    const relationTypes = await asPerson<{ id: number; code: string }[]>(page, 'GET', '/api/v1/reslinks/relation-types');
    const related = relationTypes.find((row) => row.code === 'related-to')!;
    await relate(page, related.id, open, readable);
    await relate(page, related.id, open, hidden);
    const viewerAccount = await registerAccount(page.request, 'graphviewer');
    const viewer = await signedInElsewhere(browser, viewerAccount);
    try {
      await gotoRoute(viewer.page, `/graph?type=feature&id=${open}`);
      const viewerCanvas = canvas(viewer.page);
      await expect(viewerCanvas).toHaveAttribute('data-node-count', '3', { timeout: 30_000 });
      await expect(listItem(viewer.page, readable)).toContainText(`E2E graph readable ${stamp}`);
      // The unreadable member is there as a placeholder: no title, and nothing of its name anywhere on the page.
      const placeholder = viewer.page.getByTestId('graph-list-select').filter({ hasText: 'Restricted' });
      await expect(placeholder).toHaveCount(1);
      await expect(viewer.page.getByText(`E2E graph hidden ${stamp}`)).toHaveCount(0);
      // Positive control: a readable neighbour, once selected, can be expanded. Without it the
      // button is disabled for the trivial reason that nothing is selected.
      await listItem(viewer.page, readable).getByTestId('graph-list-select').click();
      await expect(viewerCanvas).toHaveAttribute('data-selected', keyOf(readable));
      await expect(viewer.page.getByTestId('graph-expand')).toBeEnabled();
      await placeholder.click();
      await expect(viewerCanvas).toHaveAttribute('data-selected', /^restricted:/);
      await expect(viewer.page.getByTestId('graph-expand')).toBeDisabled();
      // The administrator, who may read it, sees the real item in the same position.
      await gotoRoute(page, `/graph?type=feature&id=${open}`);
      await expect(listItem(page, hidden)).toContainText(`E2E graph hidden ${stamp}`, { timeout: 30_000 });
    } finally {
      await viewer.context.close();
      for (const id of [open, readable, hidden]) {
        await asPerson(page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
      }
    }
  });

  test('pages through a family too big for one step with the plus-N-more control', async ({ page }) => {
    const stamp = `${Date.now()}${String(test.info().parallelIndex).padStart(2, '0')}`;
    await login(page);
    const region = `E2E graph region ${stamp}`;
    const hub = await createCave(page, `E2E graph hub ${stamp}`, region);
    const ids: string[] = [hub];
    const relationTypes = await asPerson<{ id: number; code: string }[]>(page, 'GET', '/api/v1/reslinks/relation-types');
    const related = relationTypes.find((row) => row.code === 'related-to')!;
    // One step carries at most forty links of a kind; forty-three leaves a remainder of three.
    const total = 43;
    try {
      for (let start = 0; start < total; start += 8) {
        const batch = await Promise.all(
          Array.from({ length: Math.min(8, total - start) }, async (_, i) => {
            const id = await createCave(page, `E2E graph spoke ${start + i} ${stamp}`, region);
            ids.push(id);
            await relate(page, related.id, hub, id);
            return id;
          }),
        );
        expect(batch.length).toBeGreaterThan(0);
      }
      await gotoRoute(page, `/graph?type=feature&id=${hub}`);
      await expect(canvas(page)).toHaveAttribute('data-node-count', '41', { timeout: 60_000 });
      const more = page.getByTestId('graph-list-more');
      await expect(more).toHaveCount(1);
      await more.click();
      await expect(canvas(page)).toHaveAttribute('data-node-count', String(total + 1), { timeout: 60_000 });
      await expect(page.getByTestId('graph-list-more')).toHaveCount(0);
    } finally {
      for (const id of ids) {
        await asPerson(page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
      }
    }
  });
});

test.describe('relations graph in the dark theme', () => {
  test.slow();
  test.use({ colorScheme: 'dark' });

  test('draws under the dark theme', async ({ page }) => {
    const stamp = `${Date.now()}${String(test.info().parallelIndex).padStart(2, '0')}`;
    await login(page);
    const network = await arrangeNetwork(page, stamp);
    try {
      await gotoRoute(page, `/graph?type=feature&id=${network.seed}`);
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
      await expect(canvas(page)).toHaveAttribute('data-node-count', '3', { timeout: 30_000 });
      await expect(canvas(page)).toHaveAttribute('data-drawn-nodes', '3', { timeout: 30_000 });
      await expect(page.getByTestId('graph-legend')).toBeVisible();
    } finally {
      for (const id of network.ids) {
        await asPerson(page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
      }
    }
  });
});
