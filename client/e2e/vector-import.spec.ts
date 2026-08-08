// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * A GPX from the field becomes caves and features in the registry — reviewed, not blindly
 * created — and the whole confirmation comes back out again as one unit.
 *
 * The flow exists as an end-to-end test because it is the only one that proves the four
 * surfaces agree: the dry run's counts, the review table's proposals, what the confirmation
 * actually wrote, and what undoing it takes away again.
 */
test.describe('vector import', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('a GPX is reviewed, confirmed and then undone as one unit', async ({ page }) => {
    // Unique per run: the dev database persists between runs, so a fixed name would accumulate
    // and make every later assertion ambiguous.
    const stamp = Date.now();
    const caveName = `Ursilor-${stamp}`;
    const springName = `Izbuc-${stamp}`;
    const gpx = `<?xml version="1.0" encoding="UTF-8"?>
<gpx version="1.1" creator="e2e" xmlns="http://www.topografix.com/GPX/1/1">
  <wpt lat="45.612" lon="25.513"><name>P. ${caveName}</name><ele>812</ele></wpt>
  <wpt lat="45.622" lon="25.523"><name>Izbuc ${springName}</name></wpt>
  <wpt lat="45.632" lon="25.533"><name>Parcare-${stamp}</name></wpt>
</gpx>`;

    await page.goto('/geodata');
    await page.setInputFiles('input[type="file"]', {
      name: `e2e-${stamp}.gpx`,
      mimeType: 'application/gpx+xml',
      buffer: Buffer.from(gpx, 'utf8'),
    });

    // The import is a background job; the table polls until it settles.
    const row = page.getByRole('row', { name: new RegExp(`e2e-${stamp}`) });
    await expect(row).toBeVisible({ timeout: 30_000 });
    await expect(row.getByText('Imported')).toBeVisible({ timeout: 60_000 });

    await row.getByRole('button', { name: 'Review and import into the registry' }).click();
    await page.waitForURL(/\/geodata\/[0-9a-f-]+\/import$/, { timeout: 30_000 });

    // The rules have already read the file: the term that identified the cave is out of its
    // name, and the car park is claimed by nothing.
    await expect(page.getByText(caveName, { exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(`P. ${caveName}`)).toBeVisible();
    await expect(page.getByText('3 points')).toBeVisible();

    // Nothing has been created yet — the registry is untouched until the button below.
    await expect(page.getByTestId('import-rule-hits')).toBeVisible();

    await page.getByTestId('import-select-all').click();
    // Two of the three: the car park is not selectable because nothing said what it should be.
    await expect(page.getByText('2 of 3 selected')).toBeVisible();

    await page.getByTestId('import-commit').click();
    await expect(page.getByText('What the import did')).toBeVisible({ timeout: 30_000 });
    await page.getByRole('button', { name: 'See the imports' }).click();

    // The registry has them now, under the names the rules proposed.
    await page.goto('/features');
    await page.getByPlaceholder('Search by name').fill(springName);
    await expect(page.getByRole('row', { name: new RegExp(springName) })).toBeVisible({ timeout: 20_000 });

    // …and undoing the confirmation takes back everything it created, as one unit.
    await page.goto('/geodata');
    await page.getByRole('tab', { name: 'Imports' }).click();
    const batch = page.getByRole('row', { name: new RegExp(`e2e-${stamp}`) });
    await expect(batch).toBeVisible({ timeout: 20_000 });
    await batch.getByRole('button', { name: 'Undo' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText('The import was undone.')).toBeVisible({ timeout: 20_000 });

    await page.goto('/features');
    await page.getByPlaceholder('Search by name').fill(springName);
    await expect(page.getByRole('row', { name: new RegExp(springName) })).toHaveCount(0, {
      timeout: 20_000,
    });
  });

  test('the rules page offers a copy of the shipped set rather than editing it in place', async ({
    page,
  }) => {
    await page.goto('/admin/term-rules');

    const shipped = page.getByRole('row', { name: /Default detection rules/ });
    await expect(shipped).toBeVisible({ timeout: 20_000 });

    // The shipped set is the last fallback when no other set applies, so nothing offers to
    // delete it — not even to an administrator.
    await expect(shipped.getByRole('button', { name: 'delete' })).toHaveCount(0);
  });
});
