// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * What a karst area adds up to, and how thickly its features sit.
 *
 * What this shows that no server test can. The arithmetic is proved against known fixtures by the
 * integration tests — a random scatter indexing near one, a clustered set clearly below it, a cell
 * finer than the protection lattice refused. What only a browser can show is that the two
 * qualifications those numbers depend on actually reach the reader.
 *
 * Both are load-bearing and both are invisible to a server test that only checks a payload.
 * A count over an area means something different depending on whether membership was declared by a
 * person or inferred from geometry, and a page that shows the total without saying which invites
 * exactly the wrong reading. And a density is published no finer than the location-protection
 * grid; a reader who is not told the floor exists will read a coarse surface as the real one.
 */
test('an area says what it holds, and on what basis it counted', async ({ page }) => {
  await login(page);

  await page.goto('/features');
  const row = page.getByRole('row', { name: /Platoul Demo/ });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.click();
  await expect(page).toHaveURL(/\/features\/[0-9a-f-]+$/, { timeout: 15_000 });

  const card = page.locator('.ant-card').filter({ hasText: 'Karst area statistics' });
  await expect(card).toBeVisible({ timeout: 15_000 });

  // The figure and the unit that makes it a density rather than a count.
  await expect(card.getByText('Caves per km²')).toBeVisible();

  // The basis is the assertion this test exists for. Membership here is what somebody entered, not
  // what happens to fall inside the outline, and the two answer different questions — a page that
  // omits it lets a reader take a declared count for a geographic one.
  await expect(card.getByText(/declared to be in this area/)).toBeVisible();
});

test('a density says how fine it is allowed to be, and why', async ({ page }) => {
  await login(page);

  await page.goto('/features');
  const row = page.getByRole('row', { name: /Platoul Demo/ });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.click();
  await expect(page).toHaveURL(/\/features\/[0-9a-f-]+$/, { timeout: 15_000 });

  const card = page.getByTestId('area-point-pattern');
  await expect(card).toBeVisible({ timeout: 15_000 });

  // The floor is stated, in metres, as the protection grid it comes from. Without this the reader
  // has no way to know the surface they are looking at is deliberately coarse rather than simply
  // what the data supports — and asking for something finer is refused, not quietly widened.
  await expect(card.getByText(/finest this installation publishes/)).toBeVisible();

  // The cell actually in force, so the stated floor can be compared against what was drawn.
  await expect(card.getByTestId('karst-density-cell-value')).toBeVisible();
});
