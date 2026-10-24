// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';

// What the map-workspace flows share, desktop and phone: where a pane is, whether one fills the
// workspace, the workspace's menus, a count of the page's graphics contexts, and whether the
// survey-line notice lies over a control.

export const pane = (page: Page, id: string) => page.locator(`[data-workspace-pane="${id}"]`);

export async function boxOf(locator: Locator) {
  const box = await locator.boundingBox();
  expect(box, 'the pane is laid out').not.toBeNull();
  return box!;
}

/** Waits until a pane sits on the given side of the map, or under it. */
export async function expectBeside(page: Page, id: string, place: 'left' | 'right' | 'bottom') {
  await expect
    .poll(
      async () => {
        const map = await pane(page, 'map').boundingBox();
        const it = await pane(page, id).boundingBox();
        if (!map || !it || it.width < 40 || it.height < 40) {
          return 'not laid out';
        }
        if (place === 'left') {
          return it.x + it.width <= map.x + 2 ? 'left' : 'elsewhere';
        }
        if (place === 'right') {
          return it.x >= map.x + map.width - 2 ? 'right' : 'elsewhere';
        }
        return it.y >= map.y + map.height - 2 && it.x < map.x + map.width ? 'bottom' : 'elsewhere';
      },
      { timeout: 30_000, message: `the ${id} pane is placed ${place} of the map` },
    )
    .toBe(place);
}

/** Waits until a pane covers the workspace's whole box. */
export async function expectFillsWorkspace(page: Page, id: string) {
  await expect
    .poll(
      async () => {
        const workspace = await page.locator('.map-workspace').boundingBox();
        const it = await pane(page, id).boundingBox();
        if (!workspace || !it) {
          return false;
        }
        return (
          Math.abs(it.x - workspace.x) <= 2 &&
          Math.abs(it.y - workspace.y) <= 2 &&
          Math.abs(it.width - workspace.width) <= 2 &&
          Math.abs(it.height - workspace.height) <= 2
        );
      },
      { timeout: 15_000, message: `the ${id} pane fills the workspace` },
    )
    .toBe(true);
}

/**
 * Waits until no menu is open. A menu that is still closing counts as open to the page's shortcuts,
 * which stand aside while one is up, so a key pressed straight after picking an entry would be
 * dropped.
 */
export async function menusClosed(page: Page) {
  await expect(page.locator('.ant-dropdown:not(.ant-dropdown-hidden)')).toHaveCount(0);
}

export async function chooseInWorkspaceMenu(page: Page, entry: string) {
  await page.getByTestId('workspace-menu').click();
  await page.getByRole('menuitem').filter({ hasText: entry }).click();
  await menusClosed(page);
}

export async function movePane(page: Page, testId: string, place: string) {
  await page.getByTestId(`${testId}-move`).click();
  await page.getByRole('menuitem', { name: place, exact: true }).click();
  await menusClosed(page);
}

/**
 * Counts every WebGL context the page creates, and says how many are alive in the document — not
 * lost, and on a canvas that is still part of the page. Installed before anything of the page runs.
 */
export async function countGraphicsContexts(page: Page) {
  await page.addInitScript(() => {
    const created: (WebGLRenderingContext | WebGL2RenderingContext)[] = [];
    const original = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function (this: HTMLCanvasElement, ...args: unknown[]) {
      const context = (original as (...a: unknown[]) => RenderingContext | null).apply(this, args);
      if (
        context &&
        typeof args[0] === 'string' &&
        args[0].includes('webgl') &&
        !created.includes(context as WebGLRenderingContext)
      ) {
        created.push(context as WebGLRenderingContext);
      }
      return context;
    } as typeof HTMLCanvasElement.prototype.getContext;
    (window as unknown as { __graphics: unknown }).__graphics = {
      created: () => created.length,
      alive: () =>
        created.filter(
          (context) =>
            !context.isContextLost() && context.canvas instanceof HTMLCanvasElement && context.canvas.isConnected,
        ).length,
    };
  });
}

export const graphics = (page: Page) =>
  page.evaluate(() => {
    const counter = (window as unknown as { __graphics: { created: () => number; alive: () => number } }).__graphics;
    return { created: counter.created(), alive: counter.alive() };
  });

/** Marks a pane's drawing surface, so a later look can say whether it is still the same element. */
export const markCanvas = (page: Page, selector: string) =>
  page.evaluate((within) => {
    const canvas = document.querySelector(`${within} canvas`);
    if (!canvas) {
      throw new Error(`no drawing surface in ${within}`);
    }
    canvas.setAttribute('data-e2e-marked', 'yes');
  }, selector);

export const markedCanvasStillThere = (page: Page, selector: string) =>
  page.evaluate((within) => {
    const canvas = document.querySelector(`${within} canvas[data-e2e-marked="yes"]`);
    return canvas !== null && canvas.isConnected;
  }, selector);

/**
 * Whether one box overlaps any of the boxes the given selectors draw — each that is on screen. A
 * selector that draws nothing is not a pass for the check: the caller names the ones that must be
 * there, and this says which of them it measured.
 */
export async function overlapsWith(page: Page, target: Locator, selectors: readonly string[]) {
  const box = await boxOf(target);
  const found: Record<string, boolean> = {};
  for (const selector of selectors) {
    const boxes = await page.locator(selector).evaluateAll((elements) =>
      elements
        .filter((element) => (element as HTMLElement).offsetParent !== null)
        .map((element) => {
          const rect = element.getBoundingClientRect();
          return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
        })
        .filter((rect) => rect.width > 0 && rect.height > 0),
    );
    found[selector] = boxes.some(
      (other) =>
        box.x < other.x + other.width &&
        other.x < box.x + box.width &&
        box.y < other.y + other.height &&
        other.y < box.y + box.height,
    );
  }
  return found;
}

/** Survey lines the map holds back, said by the server — three of them, whatever the data. */
export async function withheldLines(page: Page) {
  await page.route(
    (url) => url.pathname === '/api/v1/map/cave-centerlines',
    async (route) => {
      const response = await route.fetch();
      const body = (await response.json()) as Record<string, unknown>;
      await route.fulfill({ response, json: { ...body, withheldCount: 3 } });
    },
  );
}

/** The controls in the map's corners that the survey-line notice must never lie over. */
export const CORNER_CONTROLS = [
  '.ol-scale-line',
  '.ol-attribution',
  '.ol-zoom',
  '.bg-layer-chooser',
  '.map-dock-toggle',
  '.map-scale-overlay',
] as const;
