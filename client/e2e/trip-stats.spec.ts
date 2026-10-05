// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { chooseOption, gotoRoute, login, narrowingFacetOption } from './helpers.ts';

/**
 * What the trips add up to, reached from the list that narrowed them.
 *
 * Nothing here creates a trip and nothing asserts a number the seed happens to hold. What is
 * checked is what the page says about itself: that the figures are the reader's rather than the
 * archive's, that the narrowing arrived with the reader, and that moving the scope moves the
 * titles — the last because a heading that stays put while the population under it changes is the
 * one defect on this page that looks like nothing is wrong.
 */

/** The population a card title claims, as the pair of numbers it holds. */
async function titleNumbers(page: import('@playwright/test').Page): Promise<string> {
  const heading = page.locator('.ant-card-head-title').first();
  await expect(heading).toBeVisible();
  return ((await heading.textContent()) ?? '').trim();
}

test('totals the narrowed listing, says whose totals they are, and moves its titles with its scope', async ({
  page,
}) => {
  await login(page);
  await gotoRoute(page, '/trip-logs');

  // Narrow the listing first, so the page is reached the way it is actually reached — by an option
  // that leaves some of the trips and not all, so that the charts can be seen to carry it.
  const countLine = page.getByTestId('trip-list-count');
  const overall = Number((((await countLine.textContent()) ?? '').match(/\d+/g) ?? [])[1]);
  const stateFilter = page.getByTestId('trip-facet-states');
  const { label, count } = await narrowingFacetOption(page, stateFilter, overall);
  await chooseOption(page, stateFilter, label);
  await page.keyboard.press('Escape');
  await expect(page).toHaveURL(/[?&]states=/);
  // The address moves the moment the option is taken and the page follows it a moment later; the
  // button below carries the narrowing the page is showing, so it is pressed once the page shows
  // it, as somebody reading the count before moving on would.
  await expect(countLine).toContainText(`Showing ${count} of ${overall}`, { timeout: 15_000 });

  await page.getByTestId('trip-list-insights').click();

  // The narrowing travelled, so the charts are of the trips the reader was looking at.
  await expect(page).toHaveURL(/\/trip-logs\/stats\?.*states=/);

  // The one sentence this page owes its reader: the totals are theirs, not the club's.
  await expect(page.getByTestId('trip-stats-access')).toContainText('trips you may read');

  // The population the charts are of is the one the listing showed, in the same two numbers.
  const filtered = await titleNumbers(page);
  expect(filtered).toContain(`the ${count} of ${overall} trips you can read that this filter leaves`);

  // The scope control, and the thing the original of this page got wrong: the population in the
  // title has to move with it. Compared against itself rather than against a seeded number.
  await page.getByTestId('trip-stats-scope').getByText('All trips').click();
  await expect(page).toHaveURL(/[?&]scope=all/);
  await expect.poll(async () => titleNumbers(page), { timeout: 15_000 }).not.toBe(filtered);
  expect(await titleNumbers(page)).toContain(`all ${overall} trips you can read`);

  // The filter is still in the address, so switching back is not a lost narrowing.
  await expect(page).toHaveURL(/[?&]states=/);
  await page.getByTestId('trip-stats-scope').getByText('The current filter').click();
  await expect.poll(async () => titleNumbers(page), { timeout: 15_000 }).toBe(filtered);

  // And back to the list the reader came from, still narrowed.
  await page.getByTestId('trip-stats-back').click();
  await expect(page).toHaveURL(/\/trip-logs\?.*states=/);
});
