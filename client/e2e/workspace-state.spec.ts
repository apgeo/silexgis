// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { registerAccount, signedInElsewhere } from './arrange.ts';
import { gotoRoute, login, openCaveFromRegistry } from './helpers.ts';

/** Picks the option of a select whose text begins as given, from the keyboard — see `chooseOption`. */
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

const coordinateFormat = (page: Page) => page.getByRole('combobox', { name: 'Coordinates shown as' });
/** The select's shown value, which is the option's whole text. */
const shownFormat = (page: Page) => page.getByTestId('coordinate-format');

/**
 * What a member arranges travels with the account: chosen in one browser, it is there in another,
 * and it is not there for the next member who signs in on the first one. Two registered accounts
 * of their own, so that nothing set here reaches any other spec.
 */
test('workspace preferences follow the account, and stay with it', async ({ page, browser }) => {
  const first = await registerAccount(page.request, 'prefs-a');
  const second = await registerAccount(page.request, 'prefs-b');

  await login(page, first.email, first.password);
  await gotoRoute(page, '/settings/coordinates');
  const written = page.waitForResponse(
    (response) => response.url().endsWith('/api/v1/me/preferences') && response.request().method() === 'PATCH',
  );
  await choose(page, coordinateFormat(page), /^UTM, with zone/);
  await expect(shownFormat(page)).toContainText('UTM, with zone');
  expect((await written).ok()).toBe(true);

  // Another browser, the same member: the choice is there, from the account.
  const elsewhere = await signedInElsewhere(browser, first);
  await gotoRoute(elsewhere.page, '/settings/coordinates');
  await expect(shownFormat(elsewhere.page)).toContainText('UTM, with zone', { timeout: 15_000 });
  await elsewhere.context.close();

  // The first browser, another member: none of the first member's choice is left behind for them.
  // The session is dropped rather than signed out of, so that the browser's own copy of the first
  // member's arrangement is still there when the second signs in — the case that has to hold.
  await page.context().clearCookies();
  await login(page, second.email, second.password);
  await gotoRoute(page, '/settings/coordinates');
  await expect(shownFormat(page)).toContainText('Decimal degrees', { timeout: 15_000 });
});

/**
 * What is selected is in the address, so a copied link opens on the same selection — resolved the
 * way the reader fetches anything, so an id they may not see selects nothing.
 */
test('the selection travels in the address', async ({ page, consoleErrors }) => {
  // The id at the end is looked up like any other and answered 404 — the browser reports that to
  // its console as a failed resource, naming no address; nothing else in this flow is answered 404.
  consoleErrors.allow(
    /Failed to load resource: .*404/,
    'an address naming an object that does not exist is answered 404, and selects nothing',
  );
  await login(page);
  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  await page.waitForURL(/\/caves\/[0-9a-f-]{36}$/, { timeout: 30_000 });
  const caveId = new URL(page.url()).pathname.split('/').pop()!;

  await gotoRoute(page, `/map?select=cave:${caveId}`);
  await expect(page.getByRole('heading', { name: 'Peștera Demo Mare' })).toBeVisible({ timeout: 30_000 });
  expect(new URL(page.url()).searchParams.get('select')).toBe(`cave:${caveId}`);

  // An id nobody can see — this one exists nowhere — selects nothing and leaves the address clean.
  await gotoRoute(page, '/map?select=cave:00000000-0000-4000-8000-00000000dead');
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
  await expect.poll(() => new URL(page.url()).searchParams.get('select'), { timeout: 15_000 }).toBeNull();
  await expect(page.getByRole('heading', { name: 'Peștera Demo Mare' })).toHaveCount(0);
});
