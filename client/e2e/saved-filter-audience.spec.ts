// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount, signedInElsewhere } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * Who a saved filter is shared with, from the member's default in Settings to another member
 * running it.
 *
 * What only a browser can show is the road between the screens: that the default chosen in
 * Settings follows the account into the save dialog, which shows it and says what it means; that
 * the filter then reaches a member who shares no club with its author; that running it there
 * answers with that member's own access and nothing more; and that the dialog's "change your
 * default" link lands on the choice in Settings. The server's own tests prove the audience rules
 * and the refusals.
 *
 * Two caves are made for the run, both named with its stamp so the filter matches exactly them:
 * one every signed-in member may read, one only its maker (the administrator) may. The second is
 * what proves the filter grants nothing — the administrator running the same filter sees it, the
 * other member does not.
 */

async function caveFor(admin: Page, name: string, visibility: 'authenticated' | 'private'): Promise<{ id: string }> {
  const caveTypes = await asPerson<{ id: number }[]>(admin, 'GET', '/api/v1/cave-types');
  return asPerson<{ id: string }>(admin, 'POST', '/api/v1/caves', {
    name,
    caveTypeId: caveTypes[0].id,
    visibility,
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
}

/** Opens a saved filter from the saved-filters screen, the way a member who did not write it would. */
async function runFromTheList(page: Page, filterName: string) {
  await gotoRoute(page, '/saved-filters');
  const row = page.getByRole('row').filter({ hasText: filterName });
  await expect(row).toBeVisible({ timeout: 30_000 });
  await expect(row).toContainText('Everyone signed in');
  await row.getByRole('button', { name: 'Open in the cave registry' }).click();
  await expect(page).toHaveURL(/\/caves\?/);
  await expect(page.getByTestId('cave-registry-active-filter')).toContainText(filterName, { timeout: 30_000 });
}

test('a filter saved with the default “everyone signed in” reaches another member and grants nothing', async ({
  page,
  browser,
}) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const prefix = `E2E audience ${stamp}`;
  const openCave = `${prefix} open`;
  const shutCave = `${prefix} shut`;
  const filterName = `E2E audience filter ${stamp}`;

  const author = await registerAccount(page.request, 'audience-author');
  const reader = await registerAccount(page.request, 'audience-reader');

  const admin = await signedInElsewhere(browser);
  const readerBrowser = await signedInElsewhere(browser, reader);
  const made = [await caveFor(admin.page, openCave, 'authenticated'), await caveFor(admin.page, shutCave, 'private')];
  let savedId: string | undefined;

  try {
    await login(page, author.email, author.password);

    // ---- the default, chosen in Settings ----
    await gotoRoute(page, '/settings/filters');
    const preferenceSaved = page.waitForResponse(
      (r) => r.url().includes('/api/v1/me/preferences') && r.request().method() !== 'GET',
    );
    await page.getByTestId('saved-filter-default-audience').click();
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option')
      .filter({ hasText: 'Everyone signed in' })
      .click();
    expect((await preferenceSaved).ok()).toBeTruthy();
    await expect(page.getByTestId('saved-filter-default-audience-explanation')).toContainText(
      'Everyone signed in can use it',
    );

    // ---- the registry's save dialog starts from it and says what it means ----
    await gotoRoute(page, '/caves');
    await page.getByPlaceholder('Search name or toponym…').fill(prefix);
    await expect(page.getByRole('cell', { name: openCave, exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('cell', { name: shutCave, exact: true })).toHaveCount(0);

    await page.getByTestId('cave-registry-save-filter').click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByTestId('saved-filter-audience')).toContainText('Everyone signed in');
    await expect(dialog.getByTestId('saved-filter-audience-explanation')).toHaveText(
      'Everyone signed in can use it — it shows each of them only what they can already see.',
    );
    await dialog.getByTestId('saved-filter-name').fill(filterName);
    const created = page.waitForResponse(
      (r) => r.url().endsWith('/api/v1/saved-filters') && r.request().method() === 'POST',
    );
    await dialog.getByRole('button', { name: 'Save', exact: true }).click();
    const createdResponse = await created;
    expect(createdResponse.status()).toBe(201);
    const body = (await createdResponse.json()) as { id: string; visibility: string; cavingGroupId: string | null };
    savedId = body.id;
    expect(body.visibility).toBe('authenticated');
    expect(body.cavingGroupId).toBeNull();
    await expect(page.getByTestId('cave-registry-active-filter')).toContainText(filterName);

    // ---- another member, in no club with the author, finds it and runs it under their own access ----
    await runFromTheList(readerBrowser.page, filterName);
    await expect(readerBrowser.page.getByRole('cell', { name: openCave, exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(readerBrowser.page.getByRole('cell', { name: shutCave, exact: true })).toHaveCount(0);

    // ---- the same filter, run by someone who may read the shut cave, does show it ----
    await runFromTheList(admin.page, filterName);
    await expect(admin.page.getByRole('cell', { name: openCave, exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(admin.page.getByRole('cell', { name: shutCave, exact: true })).toBeVisible();

    // ---- the dialog's link lands on the choice in Settings ----
    await page.getByTestId('cave-registry-save-filter').click();
    await page.getByRole('dialog').getByRole('link', { name: /Change your default/ }).click();
    await expect(page).toHaveURL(/\/settings\/filters#default-audience$/);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Share new filters with' })).toBeFocused();
  } finally {
    if (savedId !== undefined) {
      await asPerson(page, 'DELETE', `/api/v1/saved-filters/${savedId}`);
    }
    for (const cave of made) {
      await asPerson(admin.page, 'DELETE', `/api/v1/caves/${cave.id}`);
    }
    await readerBrowser.context.close();
    await admin.context.close();
  }
});
