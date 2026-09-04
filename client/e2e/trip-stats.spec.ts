// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

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

  // Narrow the listing first, so the page is reached the way it is actually reached.
  const stateFilter = page.getByTestId('trip-facet-states');
  await stateFilter.click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').first().click();
  await page.keyboard.press('Escape');
  await expect(page).toHaveURL(/[?&]states=/);

  await page.getByTestId('trip-list-insights').click();

  // The narrowing travelled, so the charts are of the trips the reader was looking at.
  await expect(page).toHaveURL(/\/trip-logs\/stats\?.*states=/);

  // The one sentence this page owes its reader: the totals are theirs, not the club's.
  await expect(page.getByTestId('trip-stats-access')).toContainText('trips you may read');

  const filtered = await titleNumbers(page);
  expect(filtered).toMatch(/trips you can read/);

  // The scope control, and the thing the original of this page got wrong: the population in the
  // title has to move with it. Compared against itself rather than against a seeded number.
  await page.getByTestId('trip-stats-scope').getByText('All trips').click();
  await expect(page).toHaveURL(/[?&]scope=all/);
  await expect.poll(async () => titleNumbers(page), { timeout: 15_000 }).not.toBe(filtered);
  expect(await titleNumbers(page)).toMatch(/all \d+ trips you can read/);

  // The filter is still in the address, so switching back is not a lost narrowing.
  await expect(page).toHaveURL(/[?&]states=/);
  await page.getByTestId('trip-stats-scope').getByText('The current filter').click();
  await expect.poll(async () => titleNumbers(page), { timeout: 15_000 }).toBe(filtered);

  // And back to the list the reader came from, still narrowed.
  await page.getByTestId('trip-stats-back').click();
  await expect(page).toHaveURL(/\/trip-logs\?.*states=/);
});
