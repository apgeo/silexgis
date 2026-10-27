// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The phone binding of the Phone users page: on a phone profile (touch, coarse pointer, 412 CSS
 * px) a phone user nobody has mapped is mapped to a caver and unmapped again by finger. Arranged
 * exactly as the desktop spec arranges it.
 *
 * The phone user is arranged through the sync route a phone itself uses, so the row is the one a
 * real upload writes (unmapped, set by the device). What is driven is the part a person does:
 * finding the user at the top of the list, saying who they are, seeing that the roster keeper made
 * the mapping, and taking it back.
 */
test('a phone user is mapped and unmapped by finger on the Phone users page', async ({ page }) => {
  test.slow();

  const stamp = `${Date.now()}`;
  const deviceUserId = crypto.randomUUID();
  const head = deviceUserId.slice(0, 8);
  const caverName = `E2E Phone Caver M ${stamp}`;

  await login(page);
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E Phone Users Cave M ${stamp}`,
    description: null,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
  const set = await asPerson<{ id: string }>(page, 'POST', '/api/v1/sync/sets/', {
    name: `E2E phone users m ${stamp}`,
    uploadVisibility: 'private',
    rootFeatureIds: [cave.id],
    settings: {},
  });
  await asPerson(page, 'POST', '/api/v1/cavers', {
    fullName: caverName,
    email: null,
    phone: null,
    notes: null,
  });
  await asPerson(page, 'POST', `/api/v1/sync/sets/${set.id}/upload`, {
    batchId: crypto.randomUUID(),
    contractVersion: 1,
    people: [
      { deviceUserId, username: `phone${stamp}`, firstName: 'Nia', lastName: 'Invented', caverId: null },
    ],
  });

  // From the roster page, where the way in is offered.
  await gotoRoute(page, '/cavers');
  await page.getByTestId('cavers-phone-users').tap();
  // The page is loaded on first use: on a busy machine its first load takes longer than the
  // default wait, so the arrival is checked by address first and the heading given the same room.
  await expect(page).toHaveURL(/\/cavers\/phone-users$/, { timeout: 15_000 });
  await expect(page.getByRole('heading', { name: 'Phone users' })).toBeVisible({ timeout: 15_000 });

  // Nobody has said who they are: they head the list, marked as not mapped.
  await expect(page.getByTestId(`phone-user-source-${head}`)).toHaveText('Not mapped', { timeout: 30_000 });

  // Nobody is proposed; the keeper searches the roster and chooses.
  const pick = page.getByTestId(`phone-user-pick-${head}`);
  await pick.tap();
  await page.keyboard.type(caverName);
  await page.locator('.ant-select-dropdown:visible').getByText(caverName, { exact: true }).tap();
  await expect(page.getByText('Mapped.')).toBeVisible();
  await expect(page.getByTestId(`phone-user-source-${head}`)).toHaveText('Somebody keeping the roster');

  // The mapping is the server's, not the page's: it is there after a reload.
  await page.reload();
  await expect(page.getByTestId(`phone-user-source-${head}`)).toHaveText('Somebody keeping the roster', {
    timeout: 30_000,
  });
  await expect(page.getByTestId(`phone-user-pick-${head}`)).toContainText(caverName);

  // Taking it back leaves the phone user listed, as not mapped.
  await page.getByTestId(`phone-user-unlink-${head}`).tap();
  await page.locator('.ant-popover:visible').getByRole('button', { name: 'Remove mapping' }).tap();
  await expect(page.getByText('Mapping removed.')).toBeVisible();
  await expect(page.getByTestId(`phone-user-source-${head}`)).toHaveText('Not mapped');
});
