// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

// The page is registered in the router and in the rail's selected-key list, and a page missing
// from either is one nobody finds without anything failing — so this walks in through the rail.
// Then it measures, because the daily report is never written at start-up and a fresh stack has
// none: the figures that come back are read from the page, so a page drawn with its query refused
// cannot pass for one drawn with an empty store. Deleting a group is not pressed here — what it
// deletes, and that originals are never among it, is settled by the server's own tests.
test('an administrator reads the storage report and sees what could be deleted', async ({ page, consoleErrors }) => {
  consoleErrors.allow(
    /status of 404/,
    'the report route answers 404 until the first report has been written, and a fresh stack has none',
  );
  await login(page);
  await page.getByRole('menuitem', { name: /Administration$/ }).click();
  await page.getByRole('menuitem', { name: 'Storage' }).click();
  await expect(page).toHaveURL(/\/admin\/storage$/);
  await expect(page.getByRole('heading', { name: 'Storage' })).toBeVisible();

  await page.getByTestId('storage-measure').click();
  await expect(page.getByTestId('storage-taken-at')).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText('Whole store', { exact: true })).toBeVisible();

  // Every group the server offers is listed with its own action, and none of them is originals.
  const groups = page.getByTestId('storage-reclaimable');
  for (const group of ['superseded-scan-products', 'failed-run-work', 'older-survey-versions', 'expired-uploads']) {
    await expect(groups.getByTestId(`storage-reclaim-${group}`)).toBeVisible();
  }
  await expect(groups).not.toContainText('original');
});
