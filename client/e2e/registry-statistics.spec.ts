// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { chooseOption, gotoRoute, login } from './helpers.ts';

/**
 * The three registry explorers, checked on what no server test can reach.
 *
 * The server proves the figures are right and proves the file says what the screen was told. What
 * it cannot prove is that any of it arrives: a chart that mounts empty, a sentence that never
 * renders, a table whose rows stop at the fetch, and an export button wired to a second question
 * of its own all leave every server test green. So nothing here re-checks arithmetic. What is
 * asserted is that the figures reach the reader, that the one thing the registry says about its
 * own reticence — an interval joined because it was too thin to publish — is visible as joined
 * rather than drawn as an ordinary interval, and that the file is asked for with the screen's own
 * question and no other.
 *
 * Nothing asserts a number the seed happens to hold. Where a particular shape of answer is needed
 * it is asked for through the address, which is a control the reader has, so the assertion holds
 * on any registry rather than on this one.
 */

/** The query the browser asked a route, as a plain map, whichever of the two surfaces asked it. */
function asked(url: string): URLSearchParams {
  return new URL(url).searchParams;
}

test('shows a distribution, names the intervals it joined, and exports the same question', async ({
  page,
}) => {
  await login(page);

  // The floor is raised to the highest the registry accepts and the range is cut in two, which is
  // the state most likely to leave an interval too thin to publish on its own. It is not certain
  // on every registry, and the precondition is checkable: the registry joins an interval holding
  // between one cave and the floor, so a set recording fewer caves than the floor certainly has
  // one — while a set of a hundred split evenly across two intervals certainly has none. The
  // counts sentence on the page says which of the two this registry is, so the assertion is made
  // where it holds and named where it cannot be.
  await gotoRoute(page, '/statistics/distribution?bins=2&minimumBinCaveCount=50');

  // The figures arrive: the counts the distribution was taken over, and the registry's own
  // sentence about which caves those were.
  const counts = page.getByTestId('registry-distribution-counts');
  await expect(counts).toBeVisible();
  await expect(counts).toContainText(/\d/);
  await expect(page.getByTestId('registry-distribution-basis')).not.toBeEmpty();

  // The joined intervals are named as joined — once under the chart, and once on the axis itself,
  // which is the half that matters: a wider bar with nothing said reads as a real feature of the
  // caves rather than as the registry declining to publish a count too small to publish.
  const joined = page.getByTestId('registry-distribution-joined');
  const chart = page.getByTestId('chart-registry-distribution');
  const measured = Number(
    /over the (\d+) of/.exec((await counts.textContent()) ?? '')?.[1] ?? '0',
  );
  if (measured > 0 && measured < 50) {
    await expect(joined).toBeVisible();
    await expect(chart.locator('text').filter({ hasText: /joined/i }).first()).toBeVisible();
  } else {
    // Not the state this assertion needs, and saying so is better than asserting something
    // weaker under the same name: a registry this size may legitimately have joined nothing.
    test.info().annotations.push({
      type: 'precondition',
      description: `${measured} caves record this measurement, which is not fewer than the floor of 50, so no interval is certainly joined`,
    });
    // Whatever the answer is, the two halves of it agree: an axis naming a joined interval and a
    // summary saying none was joined would be the screen disagreeing with itself.
    const named = await chart.locator('text').filter({ hasText: /joined/i }).count();
    expect(named > 0).toBe(await joined.isVisible());
  }

  // The file is the same answer. Not "an export happened" — the question it carried is compared,
  // key for key, against the question the address is currently asking.
  const exported = page.waitForResponse(
    (response) =>
      response.url().includes('/stats/registry/distribution/export') && response.status() === 200,
  );
  await page.getByTestId('registry-distribution-export').click();
  const fileQuery = asked((await exported).url());
  const screenQuery = asked(page.url());
  expect(fileQuery.get('bins')).toBe(screenQuery.get('bins'));
  expect(fileQuery.get('minimumBinCaveCount')).toBe(screenQuery.get('minimumBinCaveCount'));
});

test('shows a relationship with what it is worth, and exports the pair on screen', async ({
  page,
}) => {
  await login(page);
  await gotoRoute(page, '/statistics/correlation');

  // A slope is never alone on this page: the goodness of the fit and the number of caves the fit
  // was taken over are the two things that say whether it is worth reading.
  await expect(page.getByTestId('registry-correlation-slope')).toBeVisible();
  await expect(page.getByTestId('registry-correlation-r2')).toBeVisible();
  await expect(page.getByTestId('registry-correlation-count')).toContainText(/\d/);
  await expect(page.getByTestId('registry-correlation-basis')).not.toBeEmpty();

  // Change the vertical measurement, so the export is asked after the screen has moved: an export
  // wired to the question the page opened with would pass a test taken on the opening question.
  // Moved to a measurement other than the one shown, whichever row of the list that is: choosing
  // the one already chosen changes nothing, and which row comes first is a fact about the list.
  const vertical = page.getByTestId('registry-correlation-y');
  const shownBefore = ((await vertical.textContent()) ?? '').trim();
  await vertical.click();
  const offered = page.locator('.ant-select-dropdown:visible .ant-select-item-option');
  await expect(offered.first()).toBeVisible();
  const other = (await offered.allTextContents())
    .map((label) => label.trim())
    .find((label) => label !== shownBefore);
  await page.keyboard.press('Escape');
  expect(other, 'a second vertical measurement to move to').toBeDefined();
  await chooseOption(page, vertical, other!);
  await expect(page).toHaveURL(/[?&]y=/);

  // The file is offered only for the answer on screen, so the button stands disabled while the new
  // answer is on its way, and a press made then is a press on nothing. It is pressed once the
  // screen has caught up, as the person reading it would.
  const exportButton = page.getByTestId('registry-correlation-export');
  await expect(exportButton).toBeEnabled({ timeout: 15_000 });
  const exported = page.waitForResponse(
    (response) =>
      response.url().includes('/stats/registry/correlation/export') && response.status() === 200,
  );
  await exportButton.click();
  const fileQuery = asked((await exported).url());
  const screenQuery = asked(page.url());
  expect(fileQuery.get('y')).toBe(screenQuery.get('y'));
  expect(fileQuery.get('x')).toBe(screenQuery.get('x') ?? 'surveyedLength');
});

test('breaks the registry down by region and exports the narrowing on screen', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/statistics/regions');

  // The rows arrive, and so does the set they were taken from — a breakdown without the total
  // behind it is a list of numbers whose scale nobody can judge.
  const table = page.getByTestId('registry-regions-table');
  await expect(table).toBeVisible();
  await expect(table.locator('tbody tr')).not.toHaveCount(0);
  await expect(page.getByTestId('registry-regions-counts')).toContainText(/\d/);
  await expect(page.getByTestId('registry-regions-basis')).not.toBeEmpty();

  // Narrow it, so the file is taken off a screen that has moved away from its opening question.
  const region = page.getByTestId('registry-regions-region');
  const firstRegion = ((await table.locator('tbody tr td').first().textContent()) ?? '').trim();
  await region.fill(firstRegion);
  await region.press('Enter');
  await expect(page).toHaveURL(/[?&]region=/);
  // The address moves the moment Enter is pressed and the table follows it a moment later; the
  // file is asked for once the screen shows the narrowing, as the person reading it would. A
  // region is matched whole, so the narrowed table is that one region's row.
  const rows = table.locator('tbody tr.ant-table-row');
  await expect(rows).toHaveCount(1, { timeout: 15_000 });
  await expect(rows.locator('td').first()).toHaveText(firstRegion);

  const exported = page.waitForResponse(
    (response) =>
      response.url().includes('/stats/registry/regions/export') && response.status() === 200,
  );
  await page.getByTestId('registry-regions-export').click();
  const fileQuery = asked((await exported).url());
  expect(fileQuery.get('region')).toBe(asked(page.url()).get('region'));
});
