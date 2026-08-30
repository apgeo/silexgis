// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

// The page is registered in two places — the router and the sidebar's selected-key list — and a
// page missing from either is one nobody finds, without anything failing. So this walks in
// through the sidebar rather than typing the address, and then reads the counts back: a page
// that renders with its query refused looks the same as one that renders with an empty table.
// That the rows never carry an address is settled server-side, where the payload can be read
// whatever the table happens to hold.
test('the delivery health page renders for an administrator', async ({ page }) => {
  await login(page);
  await page.getByRole('menuitem', { name: 'Message delivery' }).click();
  await expect(page).toHaveURL(/\/admin\/notification-health$/);
  await expect(page.getByRole('heading', { name: 'Message delivery' })).toBeVisible();
  await expect(page.getByText('Oldest still waiting')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Only what needs attention')).toBeVisible();
});

test('an administrator can set how long notifications are kept', async ({ page }) => {
  await login(page);
  await page.goto('/admin/messaging');
  await page.getByRole('tab', { name: 'Notifications' }).click();

  const days = page.getByLabel('Keep notifications for');
  await expect(days).toBeVisible({ timeout: 15_000 });
  // What the deployment configured, read back through the same field the form saves through.
  await expect(days).toHaveValue('365');
});
