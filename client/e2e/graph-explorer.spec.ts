// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
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

async function createCave(page: Page, name: string, region: string): Promise<string> {
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name,
    caveTypeId: caveTypes[0].id,
    region,
    visibility: 'authenticated',
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
});
