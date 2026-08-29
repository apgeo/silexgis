// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');

/**
 * A compiled survey no longer arrives finished: it is read into its stations and shots by a
 * background job, so the row it creates passes through a waiting state on its way to being ready.
 *
 * What this shows that no unit test can. The list is what a person watches while that happens, and
 * it polls itself — so the states have to name work that is really outstanding, the list has to
 * notice on its own when the work is done, and the plot has to stay viewable throughout, since the
 * viewer reads the uploaded file and never waited on any of this. A page that only ever saw a
 * finished row would show none of it.
 */
test('a compiled survey is read in the background and the list follows it there', async ({ page }) => {
  const caveName = `E2E Survey Cave ${Date.now()}`;
  await login(page);

  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });

  await page.getByRole('button', { name: 'Upload model' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.locator('input[type="file"]').setInputFiles(path.join(fixtures, 'P8_Master.3d'));
  await dialog.getByRole('button', { name: 'Upload model' }).click();

  // The row exists as soon as the upload is accepted, and it says what the plot is.
  const models = page.getByRole('table').filter({ hasText: 'Survex .3d' });
  await expect(models.getByText('Survex .3d')).toBeVisible({ timeout: 30_000 });

  // Viewable while it is being read. The viewer parses the uploaded file itself, so nothing it
  // needs is waiting on the job — an offer that appeared only afterwards would be a regression
  // nobody watching a small fixture finish quickly would ever notice.
  await expect(models.getByRole('button', { name: 'View in 3D' })).toBeVisible();

  // And the list settles by itself: it keeps asking while there is work outstanding and stops
  // when there is not. Nothing here reloads the page.
  await expect(models.getByText('Ready')).toBeVisible({ timeout: 60_000 });
  await expect(models.getByText('Could not be processed')).toHaveCount(0);

  // The reading is what produced the cave's centerline: the survey as it was measured, recorded
  // as the cave's own shape rather than uploaded a second time by hand.
  await expect(page.getByRole('table').filter({ hasText: 'Extracted' })).toBeVisible({
    timeout: 30_000,
  });
});
