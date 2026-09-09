// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * What a reader is told about a grouping before they are shown its colours.
 *
 * <p>
 * What this proves that no other test can. The arithmetic is settled elsewhere: the domain tests
 * prove the grouping recovers separated fixtures, the integration tests prove the publishable
 * floor withholds a thin group's measurements while keeping its count, and the component tests
 * prove each sentence renders from the field that decides it. None of those can show that the
 * sentences reach the person looking at the picture, in the order that makes them useful.
 * </p>
 * <p>
 * Two properties, and each is a way the screen could be wrong while every figure on it stayed
 * correct.
 * </p>
 * <ul>
 *   <li><b>The population comes first.</b> Most caves in a register have no survey and so have no
 *       measurements to group on. A scatter of coloured points over a register many times larger,
 *       shown without saying so, is a claim about the register that is false — and a reader who
 *       meets the colours first has already drawn their conclusion by the time they reach the
 *       footnote. So the account is asserted to be <i>above</i> the chart in the document, not
 *       merely present somewhere on the page.</li>
 *   <li><b>A weak grouping says it is weak.</b> The method returns exactly the number of groups it
 *       was asked for on any data whatever, noise included, so colours look precisely as confident
 *       when there is nothing there. The only figure that can contradict them is the one comparing
 *       how wide the groups are with how far apart they sit, and it is worthless unless it is put
 *       into words on screen.</li>
 * </ul>
 */

/** The length-against-depth view, which is the one the grouping colours. */
async function openLengthDepth(page: Page) {
  await gotoRoute(page, '/caves');
  await expect(page.getByTestId('cave-distributions')).toBeVisible({ timeout: 30_000 });
  await page.getByTitle('Length vs depth').click();
}

test('the reader is told who was left out before they are shown the colours', async ({ page }) => {
  await login(page);
  await openLengthDepth(page);

  // The real route answers here. Whatever this registry holds, the account of who could be
  // grouped is part of the answer and is published even when nothing could be grouped at all.
  const account = page.getByTestId('cave-cluster-population');
  await expect(account).toBeVisible({ timeout: 30_000 });

  const counts = page.getByTestId('cave-cluster-excluded');
  await expect(counts).toContainText(/\d/);

  // Which measures the distances were taken over, named rather than implied by the axes.
  await expect(page.getByTestId('cave-cluster-measures')).toContainText(/\S/);

  // The server's own sentence about what it counted over, in its own words.
  await expect(page.getByTestId('cave-cluster-basis')).toContainText(/\S/);

  // Order, not merely presence. `compareDocumentPosition` returns DOCUMENT_POSITION_FOLLOWING (4)
  // when the chart comes after the account in the document, which is what a reader meets first.
  const order = await page.evaluate(() => {
    const note = document.querySelector('[data-testid="cave-cluster-population"]');
    const chart = document.querySelector('[data-testid="chart-correlation"]');
    if (!note || !chart) return null;
    return note.compareDocumentPosition(chart) & Node.DOCUMENT_POSITION_FOLLOWING ? 'before' : 'after';
  });
  expect(order).toBe('before');
});

test('a grouping whose groups are as wide as the gaps between them says it is weak', async ({
  page,
}) => {
  // An invented answer, because the property under test is what the screen does with an answer of
  // this shape and no registry can be relied on to produce one. Every field is the route's own,
  // and the two readings that matter are the ones a real weak grouping would carry: groups no
  // narrower than the distance between their middles, and a group under the publishable floor
  // whose measurements the server withheld while keeping its count.
  await page.route('**/api/v1/stats/registry/clustering*', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        measures: ['surveyedLength', 'depth'],
        requestedClusterCount: 3,
        population: {
          considered: 3000,
          eligible: 400,
          excluded: 2600,
          measures: [
            { measure: 'surveyedLength', recorded: 500, missing: 2500, soleReason: 90 },
            { measure: 'depth', recorded: 2900, missing: 100, soleReason: 12 },
          ],
        },
        scaling: [
          { measure: 'surveyedLength', mean: 120, standardDeviation: 45 },
          { measure: 'depth', mean: 30, standardDeviation: 12 },
        ],
        clusters: [
          { index: 0, count: 200, centre: [100, 20], scaledCentre: [-0.4, -0.8], meanDistanceToCentre: 1.4 },
          { index: 1, count: 198, centre: [900, 90], scaledCentre: [0.4, 0.6], meanDistanceToCentre: 1.3 },
          { index: 2, count: 2, centre: null, scaledCentre: null, meanDistanceToCentre: null },
        ],
        assignments: [],
        separation: { meanWithinDistance: 1.35, meanBetweenDistance: 1.2, ratio: 1.125 },
        minimumEligibleCount: 8,
        minimumPublishableClusterSize: 3,
        iterations: 100,
        converged: false,
        basis: 'Computed over the caves you may read.',
      }),
    }),
  );

  await login(page);
  await openLengthDepth(page);

  await expect(page.getByTestId('cave-cluster-population')).toBeVisible({ timeout: 30_000 });

  // The reading that can contradict the colours, in words.
  await expect(page.getByTestId('cave-cluster-weak')).toContainText(/weak/i);

  // A group the server declined to summarise is accounted for rather than drawn as a group.
  await expect(page.getByTestId('cave-cluster-withheld')).toContainText('3');

  // A run still moving caves when it hit its limit is arbitrary in its details, and says so.
  await expect(page.getByTestId('cave-cluster-arbitrary')).toContainText(/\S/);

  // Both counts a reader needs to judge the picture, from an answer that grouped a small fraction
  // of what it was offered.
  await expect(page.getByTestId('cave-cluster-excluded')).toContainText('2600');
});
