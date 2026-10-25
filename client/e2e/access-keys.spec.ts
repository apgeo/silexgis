// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * A person makes an access key for a field device, reads it once, and withdraws it.
 *
 * What no component test can show: the secret exists on screen only between the creating answer
 * and the moment it is dismissed or the page is left, and the list afterwards says only a prefix —
 * so a reload is the proof that the page kept nothing it should not have. Signed in with a
 * password, which is the only way this page works.
 */
test('an access key is made, shown once, and revoked', async ({ page }) => {
  const name = `E2E phone ${Date.now()}`;
  await login(page);
  await gotoRoute(page, '/settings/access-keys');

  await page.getByPlaceholder('TopoDroid on my phone').fill(name);
  await page.getByRole('button', { name: 'Create key' }).click();

  // The text of the key and the warning that it will not be shown again.
  const secret = page.getByTestId('access-key-secret');
  await expect(secret).toBeVisible();
  const text = (await secret.innerText()).trim();
  expect(text.length).toBeGreaterThan(16);
  await expect(page.getByText('it is shown only once')).toBeVisible();

  const row = page.getByTestId('access-key-row').filter({ hasText: name });
  await expect(row).toBeVisible();
  await expect(row).toHaveAttribute('data-state', 'active');
  await expect(row.getByText('Never')).toBeVisible();

  // Leaving and coming back: the list shows the key's prefix and dates, never its text.
  await gotoRoute(page, '/settings/access-keys');
  await expect(page.getByTestId('access-key-secret')).toHaveCount(0);
  const again = page.getByTestId('access-key-row').filter({ hasText: name });
  await expect(again).toBeVisible();
  await expect(again).not.toContainText(text);

  await again.getByRole('button', { name: 'Revoke' }).click();
  await page.getByRole('button', { name: 'Revoke' }).last().click();
  await expect(page.getByTestId('access-key-row').filter({ hasText: name })).toHaveAttribute('data-state', 'revoked');
  await expect(page.getByTestId('access-key-row').filter({ hasText: name }).getByRole('button', { name: 'Revoke' })).toHaveCount(0);
});
