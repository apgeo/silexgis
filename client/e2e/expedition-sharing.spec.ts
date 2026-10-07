// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import {
  asPerson,
  localDay,
  registerAccount,
  signedInElsewhere,
  tripBody,
  tryAsPerson,
} from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * Sharing a camp that gathers somebody else's trip.
 *
 * A camp exists to gather other people's trips, and its organiser may not manage the permissions
 * of all of them. Sharing the camp reaches the trips they may manage and skips the others, and
 * what this flow is about is that the organiser is *told*: the dialog names the trip it skipped
 * and says whose consent is missing, and applying again picks the trip up once that consent has
 * been given.
 *
 * The organiser cannot be the administrator — an administrator may manage every trip, so nothing
 * would ever be skipped — and so the fixture's page is an account registered for this run, and
 * the administrator is the other browser: the owner of the lent trip, who delegates.
 */

/**
 * Lets a self-registered account write camps and trips, by enrolling it in the seeded Editors
 * group; the enrolment is undone when the flow ends.
 */
async function ableToOrganise(admin: Page, userId: string): Promise<() => Promise<void>> {
  const groups = await asPerson<{ id: string; slug: string }[]>(
    admin,
    'GET',
    '/api/v1/permission-groups',
  );
  const editors = groups.find((group) => group.slug === 'editors');
  expect(editors, 'the seeded Editors group is what hands out create on trips and camps').toBeTruthy();
  await asPerson(admin, 'POST', `/api/v1/permission-groups/${editors!.id}/members`, {
    memberKind: 'user',
    memberId: userId,
  });
  return () =>
    tryAsPerson(admin, 'DELETE', `/api/v1/permission-groups/${editors!.id}/members/user/${userId}`);
}

test('sharing a camp names the trip it skipped, and applying again picks it up once its owner has delegated', async ({
  page,
  browser,
  request,
}) => {
  // Three accounts and two full sign-ins, then a dialog driven twice.
  test.slow();
  const stamp = Date.now();
  const organiser = await registerAccount(request, 'camporg');
  const partner = await registerAccount(request, 'partner');
  const ownTitle = `E2E own trip ${stamp}`;
  const lentTitle = `E2E lent trip ${stamp}`;

  const admins = await signedInElsewhere(browser);
  const unenrol = await ableToOrganise(admins.page, organiser.id);
  let campId: string | undefined;
  let ownId: string | undefined;
  let lentId: string | undefined;

  try {
    // The trip somebody else owns: the administrator's, readable by anybody signed in and with
    // nobody but its owner allowed to manage its permissions.
    lentId = (
      await asPerson<{ id: string }>(
        admins.page,
        'POST',
        '/api/v1/trip-logs',
        tripBody(lentTitle, localDay(-3)),
      )
    ).id;

    await login(page, organiser.email, organiser.password);
    ownId = (
      await asPerson<{ id: string }>(page, 'POST', '/api/v1/trip-logs', tripBody(ownTitle, localDay(-4)))
    ).id;
    campId = (
      await asPerson<{ id: string }>(page, 'POST', '/api/v1/expeditions', {
        name: `E2E mixed camp ${stamp}`,
        startDate: localDay(-5),
        endDate: localDay(-1),
        visibility: 'authenticated',
      })
    ).id;
    for (const tripLogId of [ownId, lentId]) {
      await asPerson(page, 'POST', `/api/v1/expeditions/${campId}/trips`, { tripLogId });
    }

    await gotoRoute(page, `/expeditions/${campId}`);
    await page.getByTestId('expedition-share').click();
    const dialog = page.getByRole('dialog', { name: 'Share the camp' });
    await expect(dialog).toBeVisible({ timeout: 15_000 });
    await expect(dialog.getByTestId('expedition-sharing-trips')).toContainText('2', {
      timeout: 15_000,
    });

    // ---- compose one rule: the partner may read
    const selects = dialog.locator('.ant-select');
    await selects.nth(0).click();
    await page
      .locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: /^User$/ })
      .click();
    await dialog.getByRole('combobox').nth(1).pressSequentially(partner.displayName.slice(0, 16), {
      delay: 30,
    });
    const offered = page.locator('.ant-select-dropdown:visible .ant-select-item-option', {
      hasText: partner.displayName,
    });
    await expect(offered).toBeVisible({ timeout: 20_000 });
    await offered.click();
    await dialog.getByTestId('expedition-sharing-add').click();
    await expect(dialog.getByTestId('expedition-sharing-drafts')).toContainText(partner.displayName);

    // ---- apply: shared on the organiser's own trip, and the lent one is skipped and named
    const applied = page.waitForResponse(
      (response) =>
        response.request().method() === 'POST' &&
        /\/api\/v1\/expeditions\/[^/]+\/sharing$/.test(new URL(response.url()).pathname),
      { timeout: 30_000 },
    );
    await dialog.getByTestId('expedition-sharing-apply').click();
    expect((await applied).status()).toBe(200);

    const skipped = dialog.getByTestId('expedition-sharing-skipped');
    await expect(skipped).toBeVisible({ timeout: 15_000 });
    await expect(skipped).toContainText('Trips shared: 1. Trips skipped: 1.');
    const named = skipped.getByTestId('expedition-sharing-skipped-trip');
    await expect(named).toHaveCount(1);
    await expect(named).toContainText(lentTitle);
    await expect(named).toContainText('its owner must let you manage its permissions');
    // Nothing was skipped that the organiser may not read, so nothing is merely counted.
    await expect(skipped.getByTestId('expedition-sharing-skipped-unnamed')).toHaveCount(0);

    // The coverage is the truth about the rows: one trip of the two carries the rule.
    await expect(dialog.getByTestId('expedition-sharing-coverage')).toContainText('1 of 2', {
      timeout: 15_000,
    });

    // ---- the lent trip's owner delegates, and applying again picks it up
    await asPerson(admins.page, 'PUT', `/api/v1/objects/tripLog/${lentId}/access`, {
      entries: [
        {
          subjectKind: 'user',
          subjectId: organiser.id,
          effect: 'allow',
          actions: 'managePermissions',
          scopeKind: 'object',
        },
      ],
    });

    const reapplied = page.waitForResponse(
      (response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname.endsWith('/sharing/re-apply'),
      { timeout: 30_000 },
    );
    await dialog.getByTestId('expedition-sharing-reapply').click();
    expect((await reapplied).status()).toBe(200);

    await expect(skipped).toBeHidden({ timeout: 15_000 });
    await expect(dialog.getByTestId('expedition-sharing-coverage')).toContainText('2 of 2', {
      timeout: 15_000,
    });
  } finally {
    // The camp first: it takes with it the rules its sharing wrote onto both trips.
    if (campId) {
      await tryAsPerson(admins.page, 'DELETE', `/api/v1/expeditions/${campId}`);
    }
    for (const tripId of [ownId, lentId]) {
      if (tripId) {
        await tryAsPerson(admins.page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
      }
    }
    await unenrol();
    await admins.context.close();
  }
});
