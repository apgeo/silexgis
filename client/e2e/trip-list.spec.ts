// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { tripsOfItsOwn, tryAsPerson } from './arrange.ts';
import { chooseOption, gotoRoute, login, narrowingFacetOption } from './helpers.ts';

/**
 * The trip listing as somebody actually uses it: narrow it, read what the narrowing did, look at
 * the shape of what is left, and take it away as a file.
 *
 * Every assertion is written against what the page says about itself rather than against a number
 * that depends on the seeded data — a count compared with itself before and after a narrowing
 * holds whatever the demo instance happens to hold, and a count hard-coded here would be a test
 * about the seed.
 *
 * A count compared with itself has to be a count of trips that stand still, though, and the
 * installation's do not: every other flow writes and deletes trips while this one reads, and a
 * number read twice a few seconds apart is then off by the trip somebody else just made. So the
 * flow writes a few trips of its own and narrows to a word only their titles hold. Nothing else
 * writes into that listing, and the counts followed from one moment to the next — what an option
 * promises, what choosing it leaves, what the slices add up to — are counts of those.
 *
 * The one number no narrowing reaches is the second half of "Showing N of M": how many trips the
 * reader may open at all. It is never carried from one answer to the next here. What it is held
 * to is what the same answer says beside it, read in the same moment.
 *
 * One answer is still not one instant. The server counts the two halves of that line and the
 * options of each control in separate queries, so a trip written or deleted between two of them
 * leaves the answer a trip apart from itself — the line can read "Showing 15 of 14". The two
 * comparisons here that involve the second number are open to exactly that: for the milliseconds
 * between two queries, not for the seconds this flow takes.
 */

/**
 * The "Showing N of M" line above the table, as the two numbers it holds. The line is drawn only
 * once there is an answer to count, so reading it waits for the listing's first one.
 */
async function showing(page: Page): Promise<[number, number]> {
  const text = (await page.getByTestId('trip-list-count').textContent()) ?? '';
  const numbers = text.match(/\d+/g) ?? [];
  expect(numbers.length).toBeGreaterThanOrEqual(2);
  return [Number(numbers[0]), Number(numbers[1])];
}

/**
 * The line above the table and the options of the open state control, read in one go.
 *
 * Both are drawn from one answer, so read together they are one reading of the listing. Read one
 * after the other they need not be: an answer landing in between leaves the line from one and
 * the options from the next, and the two then differ by whatever was written between them.
 */
async function lineAndStateOptions(page: Page) {
  return page.evaluate(() => {
    const line = document.querySelector('[data-testid="trip-list-count"]')?.textContent ?? '';
    const numbers = line.match(/\d+/g) ?? [];
    const options = Array.from(
      document.querySelectorAll('.ant-select-dropdown .ant-select-item-option'),
    )
      // Only the list that is open: a control opened earlier keeps its list in the page, hidden.
      .filter((option) => option.getClientRects().length > 0)
      .map((option) => ({
        count: Number(((option.textContent ?? '').trim().match(/\((\d+)\)$/) ?? [])[1]),
        chosen: option.classList.contains('ant-select-item-option-selected'),
      }));
    return { matching: Number(numbers[0]), overall: Number(numbers[1]), options };
  });
}

test('narrows the listing, says what the narrowing did, groups what is left and exports it', async ({
  page,
}) => {
  const word = `listed${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const made: string[] = [];

  await login(page);
  try {
    // Three trips of this run's own: two still drafts, in different years, and one that is done.
    await tripsOfItsOwn(page, word, made, [
      { day: '2018-05-12' },
      { day: '2020-09-19' },
      { day: '2020-10-03', done: true },
    ]);
    await gotoRoute(page, '/trip-logs');

    // The count is permanently above the table, narrowed or not, so a filter's effect is never
    // something the reader has to work out. Unnarrowed, both halves are the same number.
    const [everything, overall] = await showing(page);
    expect(everything).toBe(overall);

    // Narrowed to the word: this run's trips, all of them and nothing else. From here on the
    // first number is one nothing but this flow can move.
    const search = page.getByPlaceholder('Search by title…');
    await search.fill(word);
    await expect(page).toHaveURL(new RegExp(`[?&]q=${word}`), { timeout: 15_000 });
    await expect
      .poll(async () => (await showing(page))[0], { timeout: 15_000 })
      .toBe(made.length);
    const matchingAtFirst = made.length;

    // A facet value, chosen from the control that says how many trips each option would leave. The
    // count travels in the option's own label, which is the number this narrowing must produce.
    const stateFilter = page.getByTestId('trip-facet-states');
    const { label, count: promised } = await narrowingFacetOption(page, stateFilter, matchingAtFirst);
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

    // The back button walks out of the narrowing, because the address is where it lives.
    await page.goBack();
    await expect
      .poll(async () => (await showing(page))[0], { timeout: 15_000 })
      .toBe(matchingAtFirst);
    await page.goForward();
    await expect(page).toHaveURL(/[?&]states=/);

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

    // The file is the filter and not the page, so the narrowing travels with the request — the
    // word and the option both.
    const exported = page.waitForResponse(
      (response) =>
        response.url().includes('/api/v1/trip-logs/export') && response.status() === 200,
    );
    await page.getByTestId('trip-list-export').click();
    const asked = new URL((await exported).url()).searchParams;
    expect(asked.get('states')).not.toBeNull();
    expect(asked.get('search')).toBe(word);

    // What the second number counts. With the word taken away the listing is narrowed by the
    // state alone, over everything the reader may open, and the state control still offers every
    // state with what it would leave. A trip is in exactly one state, so the options together
    // are every trip the reader may open — and that is what the second number says, while the
    // first has come down to the one option chosen: narrowing moved the first and left the second
    // counting the whole. Read off one drawing of the page rather than against the number the
    // line held earlier, which other flows have moved since.
    await search.fill('');
    await expect(page).not.toHaveURL(/[?&]q=/, { timeout: 15_000 });
    // Opened from the keyboard: a control holding a choice has a remove mark on the choice and a
    // clear mark at its edge, and a click meant to open it can land on either.
    await stateFilter.locator('input').press('ArrowDown');
    await expect(async () => {
      const { matching, overall: whole, options } = await lineAndStateOptions(page);
      expect(options.filter((option) => option.chosen)).toHaveLength(1);
      expect(matching).toBeLessThan(whole);
      expect(options.reduce((sum, option) => sum + option.count, 0)).toBe(whole);
    }).toPass({ timeout: 15_000 });
    await page.keyboard.press('Escape');

    // And the filter can be handed to the map, which is where the same trips are looked at rather
    // than listed.
    await page.getByTestId('trip-list-show-on-map').click();
    await expect(page).toHaveURL(/\/map\?.*states=/);
  } finally {
    for (const id of made) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${id}`);
    }
  }
});
