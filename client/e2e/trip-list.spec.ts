// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { chooseOption, gotoRoute, login, narrowingFacetOption } from './helpers.ts';

/**
 * The trip listing as somebody actually uses it: narrow it, read what the narrowing did, look at
 * the shape of what is left, and take it away as a file.
 *
 * Nothing here creates a trip. The flow is about the listing's own controls, and every assertion
 * is written against what the page says about itself rather than against a number that depends on
 * the seeded data — a count compared with itself before and after a narrowing holds whatever the
 * demo instance happens to hold, and a count hard-coded here would be a test about the seed.
 */

/**
 * The "Showing N of M" line above the table, as the two numbers it holds. The line is drawn only
 * once there is an answer to count, so reading it waits for the listing's first one.
 */
async function showing(page: import('@playwright/test').Page): Promise<[number, number]> {
  const text = (await page.getByTestId('trip-list-count').textContent()) ?? '';
  const numbers = text.match(/\d+/g) ?? [];
  expect(numbers.length).toBeGreaterThanOrEqual(2);
  return [Number(numbers[0]), Number(numbers[1])];
}

test('narrows the listing, says what the narrowing did, groups what is left and exports it', async ({
  page,
}) => {
  await login(page);
  await gotoRoute(page, '/trip-logs');

  // The count is permanently above the table, narrowed or not, so a filter's effect is never
  // something the reader has to work out. Unnarrowed, both halves are the same number.
  const [matchingAtFirst, overall] = await showing(page);
  expect(matchingAtFirst).toBe(overall);

  // A facet value, chosen from the control that says how many trips each option would leave. The
  // count travels in the option's own label, which is the number this narrowing must produce.
  const stateFilter = page.getByTestId('trip-facet-states');
  const { label, count: promised } = await narrowingFacetOption(page, stateFilter, overall);
  await chooseOption(page, stateFilter, label);
  await page.keyboard.press('Escape');

  // The filter is in the address, so this listing is a link somebody can send.
  await expect(page).toHaveURL(/[?&]states=/);

  // The number beside the option and the listing that option produces agree. This is the whole
  // point of counting them: a count that promised more than it delivered would be telling the
  // reader they are being shown less than they may see.
  await expect
    .poll(async () => (await showing(page))[0], { timeout: 15_000 })
    .toBe(promised);
  expect((await showing(page))[1]).toBe(overall);

  // The back button walks out of the narrowing, because the address is where it lives.
  await page.goBack();
  await expect
    .poll(async () => (await showing(page))[0], { timeout: 15_000 })
    .toBe(matchingAtFirst);
  await page.goForward();

  // The shape above the table: sliced by year, each slice carrying its own count. A trip has one
  // year, so the slices share the narrowed listing out between them with nothing left over and
  // nothing counted twice — and the panel does not warn of an overlap there is not.
  const groupingPanel = page.getByTestId('trip-grouping-panel');
  await chooseOption(page, page.getByTestId('trip-grouping-primary'), 'Year');
  await expect(page).toHaveURL(/[?&]groupBy=year/);
  const sliceCounts = groupingPanel.locator('.ant-table-tbody .ant-table-row td:nth-child(2)');
  await expect(sliceCounts.first()).toBeVisible({ timeout: 15_000 });
  const yearTotal = (await sliceCounts.allTextContents()).reduce((sum, cell) => sum + Number(cell), 0);
  expect(yearTotal).toBe(promised);
  await expect(page.getByTestId('trip-grouping-overlap')).toHaveCount(0);

  // Sliced by person instead, the totals legitimately exceed the trip count — a trip counts into
  // every person it holds — and the panel says so rather than leaving a reader to notice.
  await chooseOption(page, page.getByTestId('trip-grouping-primary'), 'Person');
  await expect(page).toHaveURL(/[?&]groupBy=participant/);
  await expect(page.getByTestId('trip-grouping-overlap')).toBeVisible({ timeout: 15_000 });

  // The file is the filter and not the page, so the narrowing travels with the request.
  const exported = page.waitForResponse(
    (response) =>
      response.url().includes('/api/v1/trip-logs/export') && response.status() === 200,
  );
  await page.getByTestId('trip-list-export').click();
  const response = await exported;
  expect(new URL(response.url()).searchParams.get('states')).not.toBeNull();

  // And the filter can be handed to the map, which is where the same trips are looked at rather
  // than listed.
  await page.getByTestId('trip-list-show-on-map').click();
  await expect(page).toHaveURL(/\/map\?.*states=/);
});
