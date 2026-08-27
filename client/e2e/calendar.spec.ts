// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The record of everything dated, driven the way somebody reads it.
 *
 * The window is required by the answer, so the first thing worth proving is that arriving at the
 * page with nothing in mind still produces one and still asks for it — a page that opened with no
 * window would be refused and would look, to whoever opened it, like a calendar that is broken.
 *
 * The toggles are the second thing. Every one of them is a display decision: what they narrow
 * away stays exactly as readable as it was on its own page, and none of them is ever a
 * permission. What is checked here is that they reach the server as the words the answer knows,
 * and that a pair of them asking for no kind of record at all says so instead of asking.
 */

/** What the page asked for on its last request, read off the wire. */
async function askedFor(page: import('@playwright/test').Page): Promise<URLSearchParams> {
  const request = await page.waitForRequest(
    (r) => r.url().includes('/api/v1/calendar?'),
    { timeout: 30_000 },
  );
  return new URL(request.url()).searchParams;
}

test('the calendar opens on a window of its own and asks for both ends of it', async ({ page }) => {
  await login(page);

  const asked = askedFor(page);
  await gotoRoute(page, '/calendar');
  const query = await asked;

  expect(query.get('from')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  expect(query.get('to')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  // Both families are on, so no family is named.
  expect(query.get('source')).toBeNull();
  await expect(page.getByRole('heading', { name: 'Calendar' })).toBeVisible();
});

test('the sidebar offers the calendar and lands on it', async ({ page }) => {
  await login(page);

  await page.getByRole('menuitem', { name: 'Calendar' }).click();
  await page.waitForURL((url) => url.pathname === '/calendar', { timeout: 60_000 });
  await expect(page.getByRole('heading', { name: 'Calendar' })).toBeVisible();
});

test('turning a family off narrows the question rather than the reader', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/calendar');
  await expect(page.getByTestId('calendar-toggle-trips')).toBeVisible();

  const asked = askedFor(page);
  await page.getByTestId('calendar-toggle-other').click();
  expect((await asked).get('source')).toBe('tripLog');

  // The other way round names every family that is not a trip, one by one. "Not the trips" is
  // more than one family, and a narrowing that could only name one would drop the family it left
  // out of a record that says nothing is missing.
  await page.getByTestId('calendar-toggle-other').click();
  const askedForTheRest = askedFor(page);
  await page.getByTestId('calendar-toggle-trips').click();
  expect((await askedForTheRest).get('source')?.split(',').sort()).toEqual([
    'event',
    'expedition',
  ]);

  // Neither family wanted is not a question the answer can be asked, so nothing is asked.
  await page.getByTestId('calendar-toggle-other').click();
  await expect(page.getByTestId('calendar-empty')).toBeVisible();
});

test('a row clicks through to the record it came from', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/calendar');

  const rows = page.locator('.ant-table-tbody tr.ant-table-row');
  const empty = page.getByTestId('calendar-empty');
  // Wait for the answer to land before counting anything. Until it does the table is in its
  // loading state and holds neither a row nor the empty placeholder, so a count taken then is
  // zero for the one reason that says nothing at all about what the window holds — and the
  // branch below would then assert an empty record against a page that is merely still asking.
  await expect(rows.or(empty).first()).toBeVisible({ timeout: 60_000 });
  // The seeded data may hold nothing in the opening window, and an empty record is a correct
  // answer rather than a failure — so this proves the click-through only where there is a row.
  if ((await rows.count()) === 0) {
    await expect(empty).toBeVisible();
    return;
  }

  await rows.first().click();
  await page.waitForURL(/\/(trip-logs|expeditions|events)\/[0-9a-f-]{36}$/, { timeout: 60_000 });
});
