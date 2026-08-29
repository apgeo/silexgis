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
    //
    // Scoped to the open list, because the closed control carries the same title attribute for
    // whatever is currently chosen: unscoped, this finds one element or two depending on which
    // theme the account happens to be on, so it fails on exactly the re-run that follows an
    // interrupted one — the run where the previous attempt did not reach the reset below.
    await page.locator('.ant-select-dropdown:visible').getByTitle('Dark', { exact: true }).click();

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
    await page
      .locator('.ant-select-dropdown:visible')
      .getByTitle('Match my system', { exact: true })
      .click();
  });

  test('a selection of caves for a phone can be made, reviewed and revoked', async ({ page }) => {
    await page.goto('/settings/sync');

    // Unique per run and revoked at the end: the dev database persists between runs, so a fixed
    // name would accumulate and make every later assertion ambiguous.
    const name = `Weekend-${Date.now()}`;

    await expect(page.getByTestId('sync-set-new')).toBeVisible({ timeout: 15_000 });
    await page.getByTestId('sync-set-new').click();

    const dialog = page.getByRole('dialog');
    await dialog.getByTestId('sync-set-name').fill(name);

    // One real cave, chosen from the list the server answered — the server refuses a root the
    // caller cannot read, so a made-up id would fail the write rather than the selection.
    await dialog.getByTestId('sync-set-caves').click();
    const option = page.locator('.ant-select-dropdown:visible .ant-select-item-option').first();
    await expect(option).toBeVisible({ timeout: 15_000 });
    const caveName = (await option.textContent())?.trim() ?? '';

    // Chosen by typing the name and pressing Enter rather than by clicking the row. The menu
    // re-renders its rows as it finishes measuring itself, and a click dispatched in that window
    // lands on a row that no longer exists, so the choice is silently dropped; the keyboard path
    // goes through the same selection handler without depending on a row surviving the click.
    await page.keyboard.type(caveName);
    await expect(page.locator('.ant-select-dropdown:visible .ant-select-item-option')).toHaveCount(1);
    await page.keyboard.press('Enter');
    // The selection has to be real before the form is submitted, so a dropped choice fails here
    // rather than as an empty selection several steps later.
    await expect(dialog.locator('.ant-select-selection-item')).toHaveCount(1);
    await page.keyboard.press('Escape');

    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

    // Reviewing it is the half a phone cannot do, so it is asserted after a reload: what is on
    // screen has to have come back from the server, not from the form that was just submitted.
    await page.reload();
    const row = page.locator('.silex-list-item').filter({ hasText: name });
    await expect(row).toBeVisible({ timeout: 20_000 });
    await expect(row).toContainText('Caves carried: 1');
    if (caveName) {
      await expect(row).toContainText(caveName);
    }

    await row.getByRole('button', { name: 'Revoke' }).click();
    await page.getByRole('button', { name: 'Revoke' }).last().click();
    await expect(page.getByText('Selection revoked.')).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.silex-list-item').filter({ hasText: name })).toHaveCount(0);
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
