// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { tripsOfItsOwn, tryAsPerson } from './arrange.ts';
import { chooseOption, gotoRoute, login, narrowingFacetOption } from './helpers.ts';

/**
 * What the trips add up to, reached from the list that narrowed them.
 *
 * Nothing asserts a number the seed happens to hold. What is checked is what the page says about
 * itself: that the figures are the reader's rather than the archive's, that the narrowing arrived
 * with the reader, and that moving the scope moves the titles — the last because a heading that
 * stays put while the population under it changes is the one defect on this page that looks like
 * nothing is wrong.
 *
 * The page names its population in two numbers, and they are not the same kind of number. How
 * many trips the filter leaves is followed from the list to the charts and back, so it has to
 * stand still while the flow looks — and the installation's trips do not, with every other flow
 * writing and deleting them meanwhile. So the flow writes a few trips of its own and narrows to a
 * word only their titles hold: a count nothing else can move. How many trips the reader may open
 * at all is narrowed by nothing and moves under every flow. It is never carried from one page to
 * the next: a title is held to the number in the answer it was drawn from, which is the only
 * reading of it that title could have had.
 */

/** The population a card title claims, as the pair of numbers it holds. */
async function titleNumbers(page: Page): Promise<string> {
  const heading = page.locator('.ant-card-head-title').first();
  await expect(heading).toBeVisible();
  return ((await heading.textContent()) ?? '').trim();
}

/**
 * The answers the charts are drawn from, as they arrive: what each was asked, and how many trips
 * it says the reader may open. Handed back as a way to ask for the latest one under a scope —
 * the current filter, which is asked with its narrowings, or everything, which is asked with none.
 */
function chartAnswers(page: Page) {
  interface Answer {
    asked: Record<string, string>;
    overall?: number;
  }
  const answers: Answer[] = [];
  page.on('response', (response) => {
    const url = new URL(response.url());
    if (url.pathname !== '/api/v1/trip-logs/stats' || response.status() !== 200) {
      return;
    }
    // Kept in the order the answers began to arrive, and completed when the body has.
    const answer: Answer = { asked: Object.fromEntries(url.searchParams) };
    answers.push(answer);
    response.json().then(
      (body: { overall: number }) => {
        answer.overall = body.overall;
      },
      () => {
        // The page moved on before the body was read; a later answer is the one that counts.
      },
    );
  });
  return (scope: 'filter' | 'all') => {
    const latest = answers.findLast(
      (answer) => (Object.keys(answer.asked).length === 0) === (scope === 'all'),
    );
    if (latest?.overall === undefined) {
      throw new Error(`the charts have not been answered under scope "${scope}" yet`);
    }
    return { asked: latest.asked, overall: latest.overall };
  };
}

test('totals the narrowed listing, says whose totals they are, and moves its titles with its scope', async ({
  page,
}) => {
  const word = `totalled${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const made: string[] = [];

  await login(page);
  try {
    // Three trips of this run's own, two of them still drafts and one done.
    await tripsOfItsOwn(page, word, made, [
      { day: '2018-05-12' },
      { day: '2020-09-19' },
      { day: '2020-10-03', done: true },
    ]);
    await gotoRoute(page, '/trip-logs');

    // Narrowed to the word first: this run's trips, all of them and nothing else.
    const countLine = page.getByTestId('trip-list-count');
    await page.getByPlaceholder('Search by title…').fill(word);
    await expect(page).toHaveURL(new RegExp(`[?&]q=${word}`), { timeout: 15_000 });
    await expect(countLine).toHaveText(new RegExp(`^Showing ${made.length} of \\d+ `), {
      timeout: 15_000,
    });

    // Then narrowed the way the page is actually reached — by an option that leaves some of the
    // trips and not all, so that the charts can be seen to carry it.
    const stateFilter = page.getByTestId('trip-facet-states');
    const { label, count } = await narrowingFacetOption(page, stateFilter, made.length);
    await chooseOption(page, stateFilter, label);
    await page.keyboard.press('Escape');
    await expect(page).toHaveURL(/[?&]states=/);
    // The address moves the moment the option is taken and the page follows it a moment later; the
    // button below carries the narrowing the page is showing, so it is pressed once the page shows
    // it, as somebody reading the count before moving on would.
    await expect(countLine).toHaveText(new RegExp(`^Showing ${count} of \\d+ `), {
      timeout: 15_000,
    });
    const narrowing = new URL(page.url()).searchParams;

    const drawnFrom = chartAnswers(page);
    await page.getByTestId('trip-list-insights').click();

    // The narrowing travelled, so the charts are of the trips the reader was looking at: it is in
    // the address, and it is what the charts' own figures were asked for.
    await expect(page).toHaveURL(/\/trip-logs\/stats\?.*states=/);
    await expect(() => {
      expect(drawnFrom('filter').asked).toEqual({ search: word, states: narrowing.get('states') });
    }).toPass({ timeout: 15_000 });

    // The one sentence this page owes its reader: the totals are theirs, not the club's.
    await expect(page.getByTestId('trip-stats-access')).toContainText('trips you may read');

    // The population the charts are of is the one the listing showed: the count the option
    // promised there, out of however many trips the answer under this title says the reader may
    // open.
    const leaves = (overall: number) =>
      `the ${count} of ${overall} trips you can read that this filter leaves`;
    let filtered = '';
    let overallThen = 0;
    await expect(async () => {
      overallThen = drawnFrom('filter').overall;
      filtered = await titleNumbers(page);
      expect(filtered).toContain(leaves(overallThen));
    }).toPass({ timeout: 15_000 });

    // Under the curve, what "new" is measured against: the trips being counted, named by the same
    // two numbers. Without it a rising line reads as ground nobody had covered, when it is ground
    // the trips this filter left had not.
    const newWithin = page.getByTestId('trip-stats-new-within');
    await expect(newWithin).toContainText(`new among ${leaves(overallThen)}`);

    // The scope control, and the thing the original of this page got wrong: the population in the
    // title has to move with it. Compared against itself rather than against a seeded number.
    await page.getByTestId('trip-stats-scope').getByText('All trips').click();
    await expect(page).toHaveURL(/[?&]scope=all/);
    await expect.poll(async () => titleNumbers(page), { timeout: 15_000 }).not.toBe(filtered);
    // Everything, as the answer asked with no narrowing at all counts it — the title and the
    // sentence under the curve from one drawing of the page, so that the sentence never describes
    // a filter the line is no longer drawn over.
    await expect(async () => {
      const everything = drawnFrom('all').overall;
      expect(await titleNumbers(page)).toContain(`all ${everything} trips you can read`);
      expect((await newWithin.textContent()) ?? '').toContain(
        `new among all ${everything} trips you can read`,
      );
    }).toPass({ timeout: 15_000 });
    await expect(newWithin).not.toContainText('this filter leaves');

    // The filter is still in the address, so switching back is not a lost narrowing: the heading
    // is the one it was, word for word, around however many trips the reader may open by now.
    await expect(page).toHaveURL(/[?&]states=/);
    await page.getByTestId('trip-stats-scope').getByText('The current filter').click();
    await expect(async () => {
      expect(await titleNumbers(page)).toBe(
        filtered.replace(leaves(overallThen), leaves(drawnFrom('filter').overall)),
      );
    }).toPass({ timeout: 15_000 });

    // And back to the list the reader came from, still narrowed.
    await page.getByTestId('trip-stats-back').click();
    await expect(page).toHaveURL(/\/trip-logs\?.*states=/);
  } finally {
    for (const id of made) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${id}`);
    }
  }
});
