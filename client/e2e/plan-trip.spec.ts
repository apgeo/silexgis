// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import {
  asPerson,
  registerAccount,
  signedInElsewhere,
  tryAsPerson,
  type Account,
} from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * Planning a trip, as the person planning it does it: from the page of their own trips or from
 * the trip list, through the one trip form on its plan door.
 *
 * What the plan door does that the report door does not is decide who reads a trip the author
 * says nothing about — their caving group when they are in exactly one, and private otherwise,
 * because guessing between several would show a plan to a club with nothing to do with it. So
 * the person planning here is never the administrator, whose memberships every other flow may
 * change: a person is minted for each run and put in exactly as many groups as the case needs,
 * by the administrator in a browser of their own. The watched page is the planner's.
 *
 * Each case asserts the same pair: what the form said before the trip existed, and what the trip
 * then got. A default the form announces and the server does not apply, or applies and the form
 * never mentioned, would fail here either way.
 */

/** The person's own entry in the club's records, found the way the invitation picker finds it. */
async function caverOf(admin: Page, account: Account): Promise<{ id: string }> {
  const found = await asPerson<{ id: string; name: string }[]>(
    admin,
    'GET',
    `/api/v1/cavers?search=${encodeURIComponent(account.displayName)}`,
  );
  const caver = found.find((row) => row.name === account.displayName);
  expect(caver, 'a registered account gets an entry in the club records').toBeTruthy();
  return caver!;
}

/**
 * Lets the person create trips at all, by enrolling them in the seeded Editors group.
 *
 * A caving group's starter ruleset does grant its members create on trips, but only at the
 * group's own scope, and the interface gates its create doors on the caller's domain-level
 * rights — the ones held with no particular row in view, which a group-scoped entry never is.
 * So a self-registered account whose only rights come through its club sees no plan door,
 * although the server would accept the plan. The enrolment is undone when the case ends.
 */
async function ableToPlan(admin: Page, userId: string): Promise<() => Promise<void>> {
  const groups = await asPerson<{ id: string; slug: string }[]>(
    admin,
    'GET',
    '/api/v1/permission-groups',
  );
  const editors = groups.find((group) => group.slug === 'editors');
  expect(editors, 'the seeded Editors group is what hands out create on trips').toBeTruthy();
  await asPerson(admin, 'POST', `/api/v1/permission-groups/${editors!.id}/members`, {
    memberKind: 'user',
    memberId: userId,
  });
  return () =>
    tryAsPerson(admin, 'DELETE', `/api/v1/permission-groups/${editors!.id}/members/user/${userId}`);
}

/**
 * A caving group of this run's own with the person as an ordinary member — the membership the
 * plan door reads its default audience from.
 */
async function groupWith(admin: Page, caverId: string, name: string): Promise<string> {
  const group = await asPerson<{ id: string }>(admin, 'POST', '/api/v1/caving-groups', {
    name,
    type: 'cavingClub',
    description: null,
    website: null,
  });
  await asPerson(admin, 'POST', `/api/v1/caving-groups/${group.id}/members`, {
    caverId,
    role: 'member',
  });
  return group.id;
}

/** The plan form, by the heading it opens with. */
function planDialog(page: Page): Locator {
  return page.getByRole('dialog', { name: 'Plan a trip' });
}

/**
 * The audience control: the select wrapped around the combobox the form labels "Visibility",
 * reached by climbing from that combobox. What is asserted on it is the chosen option's words,
 * which the control draws inside itself; the line beside the control is the form item's extra
 * text and sits outside the select, so the two cannot be confused.
 */
function chosenAudience(dialog: Locator): Locator {
  return dialog
    .getByRole('combobox', { name: 'Visibility' })
    .locator('xpath=ancestor::*[contains(concat(" ", normalize-space(@class), " "), " ant-select ")][1]');
}

/** The audience the trip's page states for it. */
function statedAudience(page: Page): Locator {
  return page
    .locator('.ant-descriptions-item')
    .filter({ has: page.locator('.ant-descriptions-item-label', { hasText: /^Visibility$/ }) })
    .locator('.ant-tag');
}

/**
 * Fills the form's title and saves it through the plan door, handing back the trip the server
 * created once the page has moved to it.
 */
async function savePlan(page: Page, dialog: Locator, title: string): Promise<string> {
  await dialog.getByLabel('Title', { exact: true }).fill(title);
  const created = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' && response.url().endsWith('/api/v1/trip-logs/plans'),
  );
  await dialog.getByRole('button', { name: 'OK' }).click();
  const response = await created;
  expect(response.status(), await response.text()).toBe(201);
  const trip = (await response.json()) as { id: string };
  await page.waitForURL((url) => url.pathname === `/trip-logs/${trip.id}`, { timeout: 30_000 });
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  return trip.id;
}

test('a plan opened from my trips is shared with the one caving group the planner is in, and the form names it first', async ({
  page,
  browser,
  request,
}) => {
  test.slow();
  const stamp = Date.now();
  const groupName = `E2E Plan Club ${stamp}`;
  const title = `E2E Planned Outing ${stamp}`;
  const planner = await registerAccount(request, 'planner');
  const admin = await signedInElsewhere(browser);
  let groupId: string | undefined;
  let tripId: string | undefined;
  let unenrol: (() => Promise<void>) | undefined;

  try {
    unenrol = await ableToPlan(admin.page, planner.id);
    groupId = await groupWith(admin.page, (await caverOf(admin.page, planner)).id, groupName);

    await login(page, planner.email, planner.password);
    await gotoRoute(page, '/trip-logs/mine');
    await page.getByTestId('my-trips-plan').click();
    const dialog = planDialog(page);
    await expect(dialog).toBeVisible();

    // Before the trip exists: the control shows the group audience, and the line beside it
    // names the group rather than calling it "your group".
    await expect(chosenAudience(dialog)).toContainText('Caving group', { timeout: 15_000 });
    await expect(dialog.getByTestId('trip-plan-audience')).toContainText(groupName);

    tripId = await savePlan(page, dialog, title);

    // After: the trip got the audience the form promised, and it is bound to that group — the
    // page names the audience, and a reload reads it back from the server rather than the form.
    await expect(statedAudience(page)).toHaveText('Caving group');
    await page.reload();
    await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 30_000 });
    await expect(statedAudience(page)).toHaveText('Caving group');
    const stored = await asPerson<{ visibility: string; cavingGroupId: string | null }>(
      page,
      'GET',
      `/api/v1/trip-logs/${tripId}`,
    );
    expect(stored.visibility).toBe('cavingGroup');
    expect(stored.cavingGroupId).toBe(groupId);
  } finally {
    if (tripId) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    if (groupId) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/caving-groups/${groupId}`);
    }
    await unenrol?.();
    await admin.context.close();
  }
});

test('a plan opened from the trip list by somebody in two caving groups starts private, and the form says why', async ({
  page,
  browser,
  request,
}) => {
  test.slow();
  const stamp = Date.now();
  const title = `E2E Undecided Outing ${stamp}`;
  const planner = await registerAccount(request, 'torn');
  const admin = await signedInElsewhere(browser);
  const groupIds: string[] = [];
  let tripId: string | undefined;
  let unenrol: (() => Promise<void>) | undefined;

  try {
    unenrol = await ableToPlan(admin.page, planner.id);
    const caver = await caverOf(admin.page, planner);
    groupIds.push(await groupWith(admin.page, caver.id, `E2E First Club ${stamp}`));
    groupIds.push(await groupWith(admin.page, caver.id, `E2E Second Club ${stamp}`));

    await login(page, planner.email, planner.password);
    await gotoRoute(page, '/trip-logs');
    // The second door is behind the create button, in the menu beside it.
    await page.getByTestId('trip-create-menu').click();
    await page.getByRole('menuitem', { name: /Plan a trip/ }).click();
    const dialog = planDialog(page);
    await expect(dialog).toBeVisible();

    // Two groups carry no order and no precedence, so nothing is guessed: private, said in words.
    await expect(dialog.getByTestId('trip-plan-audience')).toContainText('starts private', {
      timeout: 15_000,
    });
    await expect(chosenAudience(dialog)).toContainText('Private');

    tripId = await savePlan(page, dialog, title);
    await expect(statedAudience(page)).toHaveText('Private');
  } finally {
    if (tripId) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    for (const groupId of groupIds) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/caving-groups/${groupId}`);
    }
    await unenrol?.();
    await admin.context.close();
  }
});
