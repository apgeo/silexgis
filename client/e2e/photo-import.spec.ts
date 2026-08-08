// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * A trip's worth of photographs becomes entrances and features in the registry, and the whole
 * confirmation comes back out again as one unit.
 *
 * The flow exists end to end because it is the only thing that proves the surfaces agree: the
 * grouping the server worked out, what the review table shows about it, what the confirmation
 * actually wrote — with the pictures hanging on it — and what undoing it takes away again.
 *
 * The fixtures are three small JPEGs carrying real EXIF: two of them within a few metres of each
 * other and one two hundred metres off, so the grouping has something to get right.
 */
test.describe('photo import', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('photographs become an entrance and a feature, then are undone as one unit', async ({ page }) => {
    // Unique per run: the dev database persists between runs, so a fixed name would accumulate
    // and make every later assertion ambiguous.
    const stamp = Date.now();
    const entranceName = `Foto-Ursilor-${stamp}`;
    const springName = `Foto-Izbuc-${stamp}`;

    await page.goto('/geodata');
    await page.getByTestId('photo-import-open').click();
    await page.waitForURL(/\/geodata\/photo-import$/, { timeout: 30_000 });

    // Scoped to the drop zone: the layout carries other file inputs, and an unscoped locator
    // sends a trip's photographs to whichever one the route happened to leave in the document.
    await page.getByTestId('photo-import-dropzone').locator('input[type="file"]').setInputFiles([
      'e2e/fixtures/e2e-entrance-1.jpg',
      'e2e/fixtures/e2e-entrance-2.jpg',
      'e2e/fixtures/e2e-spring.jpg',
    ]);

    // Three pictures, two places: the two shots of one entrance are a single row with a
    // gallery, which is the whole difference between a review somebody finishes and one they
    // abandon.
    const table = page.getByTestId('photo-candidate-table');
    await expect(table.getByRole('row')).toHaveCount(3, { timeout: 60_000 }); // header + two places

    // The camera placed them, and the review says so rather than presenting a bare coordinate.
    await expect(page.getByText('From the camera').first()).toBeVisible();
    await expect(page.getByText('Facing 137°')).toBeVisible();

    const rows = table.getByRole('row');
    await rows.nth(1).getByRole('textbox').fill(entranceName);
    await rows.nth(2).getByRole('textbox').fill(springName);

    // Nothing has been created yet — the registry is untouched until the button below.
    await page.getByRole('columnheader').first().getByRole('checkbox').check();
    await page.getByTestId('photo-import-commit').click();
    await expect(page.getByText('What the import did')).toBeVisible({ timeout: 30_000 });
    await page.getByRole('button', { name: 'See the imports' }).click();

    // The registry has them now, under the names typed above.
    await page.goto('/features');
    await page.getByPlaceholder('Search by name').fill(entranceName);
    await expect(page.getByRole('row', { name: new RegExp(entranceName) })).toBeVisible({
      timeout: 20_000,
    });

    // …and undoing the confirmation takes back everything it created, as one unit.
    await page.goto('/geodata');
    await page.getByRole('tab', { name: 'Imports' }).click();
    const batch = page.getByRole('row', { name: /Photographs|Fotografii/ }).first();
    await expect(batch).toBeVisible({ timeout: 20_000 });
    await batch.getByRole('button', { name: 'Undo' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText('The import was undone.')).toBeVisible({ timeout: 20_000 });

    await page.goto('/features');
    await page.getByPlaceholder('Search by name').fill(entranceName);
    await expect(page.getByRole('row', { name: new RegExp(entranceName) })).toHaveCount(0, {
      timeout: 20_000,
    });
  });
});
