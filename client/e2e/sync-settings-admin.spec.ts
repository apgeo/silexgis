// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

interface SyncSettings {
  peopleDirectoryEnabled: boolean;
  peopleDirectoryScope: string;
  convertTripsWithProblems: boolean;
  stagedUploadRetentionDays: number;
  rejectedHeldBytesRetentionDays: number;
}

/**
 * The phone-sync settings section: every setting is explained where it is set, a change is saved
 * and is the server's after a reload.
 *
 * The installation's settings are shared by every test of the run, so the section is put back as
 * it was found, whatever the outcome.
 */
test('the phone sync settings are explained, saved and kept', async ({ page }) => {
  test.slow();

  await login(page);
  const before = (await asPerson<{ sync: SyncSettings }>(page, 'GET', '/api/v1/admin/settings')).sync;
  try {
    await gotoRoute(page, '/admin/messaging');
    await page.getByRole('tab', { name: 'Phone sync' }).click();

    // Each setting carries its own help beside it.
    const form = page.getByTestId('sync-settings-form');
    await expect(form.getByText(/names only and never an address or a phone number/)).toBeVisible();
    await expect(form.getByText(/waits on the cave page until somebody converts it by hand/)).toBeVisible();
    await expect(form.getByText(/deleted after this many days/)).toBeVisible();

    // A value the installation is not using now, so that the change is the one seen afterwards.
    const days = before.stagedUploadRetentionDays === 9 ? 10 : 9;
    const field = form.getByLabel(/Keep photos sent ahead of an upload/);
    await field.fill(`${days}`);
    await form.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Saved.').first()).toBeVisible();

    await expect
      .poll(async () => (await asPerson<{ sync: SyncSettings }>(page, 'GET', '/api/v1/admin/settings')).sync)
      .toEqual({ ...before, stagedUploadRetentionDays: days });

    // After a reload the section shows what the server holds.
    await page.reload();
    await page.getByRole('tab', { name: 'Phone sync' }).click();
    await expect(page.getByTestId('sync-settings-form').getByLabel(/Keep photos sent ahead of an upload/)).toHaveValue(
      `${days}`,
      { timeout: 30_000 },
    );
  } finally {
    await asPerson(page, 'PUT', '/api/v1/admin/settings/sync', before);
  }
});
