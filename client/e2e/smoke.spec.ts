// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test } from '@playwright/test';

// Demo credentials/data: `dotnet run -- seed-demo` with the dev admin bootstrap
// (06-deployment.md §4). The full OIDC code+PKCE flow runs in the real browser.
const adminEmail = 'admin@dev.local';
const adminPassword = 'dev-admin-pass-1';

test('login, map workspace and cave registry work end to end', async ({ page }) => {
  await page.goto('/');

  // Unauthenticated → OIDC authorize → SPA login page with returnUrl.
  await page.waitForURL(/\/login\?returnUrl=/);
  await page.getByLabel('Email').fill(adminEmail);
  await page.getByLabel('Password').fill(adminPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();

  // Authorize completes, callback exchanges the code, workspace renders.
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText('OpenStreetMap')).toBeVisible();

  // Entrance features load from the map endpoint (demo data): canvas exists and
  // the layer panel shows the overlay toggle.
  await expect(page.getByText('Cave entrances')).toBeVisible();

  // Cave registry lists demo data.
  await page.goto('/caves');
  await expect(page.getByText('Peștera Demo Mare')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Avenul Demo Protejat')).toBeVisible();

  // Detail page shows the protected demo cave with its entrance table.
  await page.getByText('Avenul Demo Protejat').click();
  await expect(page.getByText('Entrances')).toBeVisible();
});
