// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { registerAccount } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/** Picks the option of a select whose text begins as given, from the keyboard. */
async function choose(page: Page, select: Locator, startsWith: RegExp) {
  await select.click();
  const option = page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: startsWith });
  await expect(option).toBeVisible({ timeout: 15_000 });
  for (let step = 0; step < 10; step++) {
    if (await option.evaluate((el) => el.classList.contains('ant-select-item-option-active'))) {
      break;
    }
    await page.keyboard.press('ArrowDown');
  }
  await page.keyboard.press('Enter');
}

/** The application's own cache of the account's arrangement, as this browser holds it. */
const cachedPrefs = (page: Page) =>
  page.evaluate(() => {
    const state = JSON.parse(localStorage.getItem('silexgis.uiPrefs') ?? '{}').state ?? {};
    return { accountId: state.accountId as string | undefined, coordinateFormat: state.coordinateFormat as string };
  });

/**
 * Signing out through the account menu ends the session, not just the page's copy of it.
 *
 * The tokens live in memory, so any full navigation drops them; what decides whether the person is
 * really signed out is the server's sign-in cookie, which the authorization endpoint accepts without
 * asking anything. A sign-out that left it behind would look done and be undone by the next page
 * load — so the proof is a protected route opened afterwards, as a new document, that has to ask for
 * the password. As an account of its own: the session and the arrangement ended here are nobody
 * else's.
 */
test('signing out ends the session and releases the arrangement this browser held', async ({ page }) => {
  const member = await registerAccount(page.request, 'signout');
  await login(page, member.email, member.password);

  // Something of the member's for this browser to hold: a choice that is not the default.
  await gotoRoute(page, '/settings/accessibility');
  const written = page.waitForResponse(
    (response) => response.url().endsWith('/api/v1/me/preferences') && response.request().method() === 'PATCH',
  );
  await choose(page, page.getByRole('combobox', { name: 'Coordinates shown as' }), /^UTM, with zone/);
  expect((await written).ok()).toBe(true);
  // The positive case first: before signing out the cache is the member's and carries the choice.
  await expect.poll(() => cachedPrefs(page)).toEqual({ accountId: member.id, coordinateFormat: 'utm' });

  await page.getByTestId('account-menu').click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();

  // The end-session round trip comes back to the application, which finds no session and is sent
  // to the sign-in form — not straight back in.
  await page.waitForURL(/\/login\?returnUrl=/, { timeout: 60_000 });
  await expect(page.getByLabel('Password')).toBeVisible();
  const released = await cachedPrefs(page);
  expect(released.accountId).toBeUndefined();
  expect(released.coordinateFormat).not.toBe('utm');

  // A protected route opened afresh asks again: nothing left in the browser signs it in.
  await page.goto('about:blank');
  await page.goto('/settings/profile');
  await page.waitForURL(/\/login\?returnUrl=/, { timeout: 60_000 });
  await expect(page.getByLabel('Password')).toBeVisible();
  await expect(page.getByTestId('app-sider')).toHaveCount(0);
});
