// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * Signs in and hands back the session's `authorization` header.
 *
 * A direct request of this test's own is not the application's request: `page.request` carries the
 * context's cookies, and this API authenticates with a bearer token the running application holds
 * and attaches itself. So a probe sent without this header is answered 401 whether or not the rule
 * it means to check is enforced, which would make the check below pass for the wrong reason on a
 * server that had no floor at all. The token is read off a request the application really sent
 * rather than out of storage, so it stays correct if where it is kept ever changes.
 */
async function authorizedHeader(page: Page): Promise<string> {
  let authorization: string | undefined;
  page.on('request', (request) => {
    const header = request.headers()['authorization'];
    if (header && request.url().includes('/api/v1/')) {
      authorization = header;
    }
  });

  await login(page);
  await expect.poll(() => authorization, { timeout: 30_000 }).toBeTruthy();
  return authorization!;
}

/**
 * Whether a density pattern is arranged by more than chance, and what the reader is told about the
 * reading before they read it.
 *
 * What this shows that no server test can. The arithmetic is proved against known fixtures
 * elsewhere — a random arrangement indexing near its expectation, a constructed clustered one
 * clearly above it, a cell finer than the protection lattice refused with a code. Those prove the
 * numbers. They cannot prove that the three qualifications the numbers are worthless without
 * actually reach the person looking at the screen.
 *
 * All three are load-bearing, and each one is a way the surface could be wrong while every figure
 * on it stayed correct:
 *
 * - A hot-spot surface is a density surface. It must never appear to be a finer-grained map than
 *   the density it was computed from, so the grid it was taken over is named beside it and the
 *   control cannot ask for anything below the published floor.
 * - The score is computed at every cell at once and is not corrected for that, so over a large
 *   window a scattering of individually striking cells is what chance alone produces. A reader not
 *   told this reads each hot cell as a finding.
 * - "Undetermined" and "random" are different claims — the first says the window could not support
 *   the question, the second says it was asked and the answer was "like chance". A surface that
 *   renders an absent index as nought turns the first silently into the second.
 */
test('the hot and cold spots are shown at the grid they came from and no finer', async ({
  page,
}) => {
  await login(page);

  await page.goto('/features');
  const row = page.getByRole('row', { name: /Platoul Demo/ });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.click();
  await expect(page).toHaveURL(/\/features\/[0-9a-f-]+$/, { timeout: 15_000 });

  const card = page.getByTestId('area-point-pattern');
  await expect(card).toBeVisible({ timeout: 15_000 });

  const reading = card.getByTestId('karst-autocorrelation');
  await expect(reading).toBeVisible();

  // The cell size the reading was taken over, stated on the reading itself rather than only on the
  // density above it. This is the sentence that stops a hot-spot shading being read as a sharper
  // map than the one it is arithmetic over.
  await expect(reading).toContainText(/cell and no finer/);

  // The same floor the density publishes, and the same control: there is no second cell size here
  // that could be set below it, because the statistic has no cell size of its own.
  await expect(card.getByText(/finest this installation publishes/)).toBeVisible();
});

test('the reader is told the hot-cell count is uncorrected before they read the cells', async ({
  page,
}) => {
  await login(page);

  await page.goto('/features');
  const row = page.getByRole('row', { name: /Platoul Demo/ });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.click();
  await expect(page).toHaveURL(/\/features\/[0-9a-f-]+$/, { timeout: 15_000 });

  const spots = page.getByTestId('karst-hotspot-count');
  await expect(spots).toBeVisible({ timeout: 15_000 });

  // How many were striking, out of how many could be scored at all — a cell with no score is not a
  // cell that scored nought, and the denominator is what keeps those apart.
  await expect(spots).toContainText(/that could be scored/);

  // And the warning that makes the count readable rather than a list of findings.
  await expect(spots).toContainText(/not corrected/);
});

test('asking for a finer grid than the installation publishes is refused, not answered', async ({
  page,
}) => {
  const authorization = await authorizedHeader(page);

  // The control offers multiples of the published floor, so a refusal is unreachable by clicking.
  // Asking the route directly is the only way to show that the floor is enforced by the server
  // rather than by the shape of the control — a client-side-only floor would leave the surface
  // looking identical and the protection gone.
  const probe = await page.request.get('/api/v1/map/density?bbox=25.40,45.49,25.48,45.56', {
    headers: { authorization },
  });
  expect(probe.ok()).toBeTruthy();
  const floor = (await probe.json()).minimumCellMetres as number;
  expect(floor).toBeGreaterThan(0);

  const finer = await page.request.get(
    `/api/v1/map/density?bbox=25.40,45.49,25.48,45.56&cellMetres=${floor / 2}`,
    { headers: { authorization } },
  );
  expect(finer.status()).toBe(400);

  // Refused with a code a client can branch on, and refused rather than quietly widened to the
  // floor: silently answering a different question than the one asked is how a caller comes to
  // believe a coarse surface is a fine one.
  const problem = await finer.json();
  expect(problem.code).toBe('density.cell_below_protection_grid');
});
