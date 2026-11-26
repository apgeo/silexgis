// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, localDay, tripBody, tryAsPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The heading row a detail page opens with, measured in a real browser at a phone's width and at a
 * desktop's.
 *
 * The pages used to lay their title and a row of buttons side by side. On a phone the buttons kept
 * their width and the title was squeezed into what was left — a long name broken one word, or one
 * letter, per line beside a column of buttons, and on the cabinet page the delete button pushed
 * off the edge so the whole page scrolled sideways. On a desktop the title ran straight into the
 * first button. What is asserted is the layout itself, which a test without a browser cannot see:
 * nothing wider than the screen, the actions under the title on a phone, and a gap between the two
 * where they share a row.
 */
const PHONE = { width: 390, height: 844 };
const DESKTOP = { width: 1440, height: 900 };

interface Boxes {
  title: DOMRect;
  actions: DOMRect;
  overflow: number;
}

async function measure(page: Page, title: string): Promise<Boxes> {
  const header = page.getByTestId('page-header').filter({ hasText: title });
  await expect(header.getByRole('heading', { name: title })).toBeVisible({ timeout: 30_000 });
  // The actions' line is measured from its first control: the line itself is as wide as it may
  // grow, and what the reader sees beside the title is where the buttons start.
  return header.evaluate((element) => {
    const heading = element.querySelector('h1, h2, h3, h4, h5') as HTMLElement;
    const actions = element.querySelector('[data-testid="page-header-actions"]') as HTMLElement;
    const first = (actions.firstElementChild as HTMLElement | null) ?? actions;
    return {
      title: heading.getBoundingClientRect().toJSON() as DOMRect,
      actions: first.getBoundingClientRect().toJSON() as DOMRect,
      overflow: document.documentElement.scrollWidth - window.innerWidth,
    };
  });
}

/** Reached at a desktop width (the address round trip waits for the desktop frame), then sized. */
async function open(page: Page, path: string, size: { width: number; height: number }) {
  await page.setViewportSize({ width: 1280, height: 800 });
  await gotoRoute(page, path);
  await page.setViewportSize(size);
}

test('a long title wraps on its own line on a phone and keeps its distance on a desktop', async ({ page }) => {
  test.setTimeout(300_000);
  const stamp = `${Date.now()}`;
  const long = (kind: string) => `E2E ${kind} cu un nume inventat foarte lung pentru antet ${stamp}`;

  await login(page);
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: long('Peștera'),
    description: null,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
  const camp = await asPerson<{ id: string }>(page, 'POST', '/api/v1/expeditions', {
    name: long('Tabăra'),
    startDate: localDay(-5),
    endDate: localDay(-1),
    visibility: 'authenticated',
  });
  const trip = await asPerson<{ id: string }>(page, 'POST', '/api/v1/trip-logs', tripBody(long('Tura'), localDay(-3)));
  const cabinet = await asPerson<{ id: string }>(page, 'POST', '/api/v1/cabinets', {
    name: long('Dulapul'),
    description: null,
    parentId: null,
  });

  const pages: { path: string; title: string; pick?: string }[] = [
    { path: `/caves/${cave.id}`, title: long('Peștera') },
    { path: `/expeditions/${camp.id}`, title: long('Tabăra') },
    { path: `/trip-logs/${trip.id}`, title: long('Tura') },
    { path: '/cabinets', title: long('Dulapul'), pick: long('Dulapul') },
  ];

  try {
    for (const { path, title, pick } of pages) {
      for (const size of [PHONE, DESKTOP]) {
        await open(page, path, size);
        if (pick) {
          // On a phone the shelves are folded into a panel above the documents.
          const shelf = page.getByRole('treeitem').filter({ hasText: pick });
          const folded = page.locator('.ant-collapse-header').first();
          await expect(shelf.or(folded).first()).toBeVisible({ timeout: 30_000 });
          if (!(await shelf.isVisible())) {
            await folded.click();
          }
          await shelf.click();
        }
        const where = `${path} at ${size.width}px`;
        const boxes = await measure(page, title);

        expect(boxes.overflow, `${where} is no wider than the screen`).toBeLessThanOrEqual(0);
        expect(boxes.title.right, `${where} keeps the title on screen`).toBeLessThanOrEqual(size.width);
        if (size === PHONE) {
          expect(boxes.actions.top, `${where} puts the actions under the title`).toBeGreaterThanOrEqual(
            boxes.title.bottom - 1,
          );
          // Wrapped by word: a title broken a letter per line would be many times taller than this.
          expect(boxes.title.height, `${where} wraps the title by word`).toBeLessThan(200);
        } else if (boxes.actions.top < boxes.title.bottom) {
          expect(
            boxes.actions.left - boxes.title.right,
            `${where} leaves a gap between title and actions`,
          ).toBeGreaterThanOrEqual(12);
        }
      }
    }
  } finally {
    await tryAsPerson(page, 'DELETE', `/api/v1/cabinets/${cabinet.id}`);
    await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
    await tryAsPerson(page, 'DELETE', `/api/v1/expeditions/${camp.id}`);
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${cave.id}`);
  }
});
