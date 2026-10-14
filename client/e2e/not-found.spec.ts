// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

// A bookmark to a page that moved, a mistyped link, a link from an older version: the address is
// answered inside the application's own frame, which stays usable, instead of replacing the whole
// application with the router's error screen.
test.describe('an address nobody answers', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('shows the not-found page inside the frame, and the frame still navigates', async ({ page }) => {
    await page.goto('/caves-from-an-old-bookmark/42');

    await expect(page.getByText('No page at this address')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText('/caves-from-an-old-bookmark/42')).toBeVisible();

    await page.getByRole('menuitem', { name: 'Dashboard' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByText('No page at this address')).toHaveCount(0);
  });

  test('answers a mistyped tab of a section that exists, and leads back to the start', async ({ page }) => {
    await page.goto('/settings/no-such-tab');

    await expect(page.getByText('No page at this address')).toBeVisible({ timeout: 15_000 });
    await page.getByRole('button', { name: 'Go to the start page' }).click();
    await expect(page).toHaveURL(/\/$/);
  });
});
