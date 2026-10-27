// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { uploaderSeesEachDecision } from './reviewUploaderFlow.ts';

/**
 * The two ends of review that are not the reviewer's queue: the administrator marking an account,
 * and the account's own list of what it sent and how each upload was decided.
 */

test('an uploader reads each decision, and the reviewer\'s note, in their own list', async ({ page, browser }) => {
  test.slow();
  await uploaderSeesEachDecision(page, browser, { phone: false });
});

test('an administrator marks an account so its uploads are held, and clears the mark', async ({ page }) => {
  test.slow();
  const account = await registerAccount(page.request, 'mark');
  await login(page);
  await gotoRoute(page, '/admin/users');
  await page.getByRole('searchbox', { name: 'Search accounts' }).fill(account.displayName);
  const row = page.getByRole('row').filter({ hasText: account.displayName });
  await expect(row).toHaveCount(1, { timeout: 30_000 });
  await expect(row.getByTestId('user-upload-review-mark')).toHaveCount(0);

  await row.getByTestId('user-upload-review-toggle').click();
  await expect(row.getByTestId('user-upload-review-mark')).toBeVisible();
  await expect(row.getByTestId('user-upload-review-toggle')).toHaveText('Stop holding uploads');
  await expect
    .poll(async () => (await asPerson<{ uploadsNeedReview: boolean }>(page, 'GET', `/api/v1/users/${account.id}`)).uploadsNeedReview)
    .toBe(true);

  await row.getByTestId('user-upload-review-toggle').click();
  await expect(row.getByTestId('user-upload-review-mark')).toHaveCount(0);
  await expect(row.getByTestId('user-upload-review-toggle')).toHaveText('Hold uploads for review');
  await expect
    .poll(async () => (await asPerson<{ uploadsNeedReview: boolean }>(page, 'GET', `/api/v1/users/${account.id}`)).uploadsNeedReview)
    .toBe(false);
});
