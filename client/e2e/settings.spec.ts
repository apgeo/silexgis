// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

test.describe('settings', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('the old security address still reaches the security section', async ({ page }) => {
    await page.goto('/account/security');

    await expect(page).toHaveURL(/\/settings\/security$/);
    await expect(page.getByText('Two-factor authentication')).toBeVisible({ timeout: 15_000 });
  });

  test('the profile saves a name and an address with a point picked on the map', async ({ page }) => {
    await page.goto('/settings/profile');

    // Unique per run, and removed at the end: the dev database persists between runs, so a
    // fixed label would accumulate and make every later assertion ambiguous.
    const stamp = Date.now();
    const surname = `Pop-${stamp}`;
    const addressLabel = `Home-${stamp}`;
    await page.getByLabel('First name').fill('Ana');
    await page.getByLabel('Surname').fill(surname);

    await page.getByRole('button', { name: 'Add an address' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Label').fill(addressLabel);
    await dialog.getByLabel('City').fill('Braşov');

    await dialog.getByRole('button', { name: 'Pick on the map' }).click();
    const picker = page.getByTestId('point-picker-map');
    await expect(picker.locator('canvas')).toBeVisible({ timeout: 20_000 });
    await picker.click({ position: { x: 300, y: 180 } });
    await page.getByRole('button', { name: 'Use this point' }).click();

    // Saves the address (its own resource), then the profile fields.
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
    // The picked point comes back as formatted coordinates on the saved row.
    await expect(page.getByText(/°[NS] .*°[EW]/).first()).toBeVisible();

    // Waits on the request itself: a second "Saved." toast is indistinguishable from the one
    // the address save just raised, so reloading on the toast would race the profile write.
    const profileSaved = page.waitForResponse(
      (r) => r.url().endsWith('/api/v1/me') && r.request().method() === 'PUT',
    );
    await page.getByRole('button', { name: 'Save' }).last().click();
    await profileSaved;

    await page.reload();
    await expect(page.getByLabel('Surname')).toHaveValue(surname);

    // last(): the section's own card also contains the row, so the filter matches both.
    const card = page.locator('.ant-card').filter({ hasText: addressLabel }).last();
    await expect(card).toBeVisible();

    // Clean up, so a re-run starts from the same state this one did.
    await card.getByLabel('Remove address').click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
  });

  test('a dark theme survives a reload, painted before the app loads', async ({ page }) => {
    await page.goto('/settings/accessibility');

    // By accessible name, not position: the shell's own language switcher is the first
    // combobox in the document.
    await page.getByLabel('Theme').click();
    // The visible options are .ant-select-item-option with a title; antd's role="option" nodes
    // live in a hidden a11y listbox that holds only two of them.
    await page.getByTitle('Dark', { exact: true }).click();

    // Tokens are held in memory, so a reload takes a silent authorize round trip before the
    // page settles. Evaluating during it would run against a context that is about to be torn
    // down by the redirect.
    await page.reload();
    await page.waitForURL(/\/settings\/accessibility/, { timeout: 20_000 });
    await expect(page.getByLabel('Theme')).toBeVisible({ timeout: 20_000 });

    // Proves the pre-mount script, which is the only thing that can decide this before the
    // bundle has run — a React-only implementation flashes white first.
    await expect
      .poll(() => page.evaluate(() => document.documentElement.dataset.theme))
      .toBe('dark');

    // Put it back so the next run starts from the default.
    await page.goto('/settings/accessibility');
    await page.getByLabel('Theme').click();
    await page.getByTitle('Match my system', { exact: true }).click();
  });

  test('turning notification email off disables the per-category switches', async ({ page }) => {
    await page.goto('/settings/notifications');

    const master = page.getByLabel('Notify me by email');
    await expect(master).toBeVisible({ timeout: 15_000 });
    await master.click();

    const securityAlerts = page.getByLabel('Security alerts');
    await expect(securityAlerts).toBeDisabled();
  });
});
