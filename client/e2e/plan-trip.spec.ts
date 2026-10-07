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
import { chooseOption, gotoRoute, login } from './helpers.ts';

/**
 * Creating a trip, as the person creating it does it: from the page of their own trips or from
 * the trip list, through the one trip form on either of its two doors.
 *
 * What the plan door does that the report door does not is decide who reads a trip the author
 * says nothing about — their caving group when they are in exactly one, and private otherwise,
 * because guessing between several would show a plan to a club with nothing to do with it. So
 * the person here is never the administrator, whose memberships every other flow may change: a
 * person is minted for each run and put in exactly as many groups as the case needs, by the
 * administrator in a browser of their own. The watched page is that person's.
 *
 * Most of these people hold nothing but what their club gives them. A club's own rules let its
 * members record the club's trips and nothing wider, which is a right over trips that belong to
 * the club and not a right over trips as such — and the doors have to open for them all the
 * same, onto a form that binds the trip to the club, because that is the only trip the server
 * accepts from them. One case keeps a person who does hold the right over trips as such, for
 * whom nothing is bound and nothing is asked.
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
 * Gives the person the right to record trips over the domain as such, by enrolling them in the
 * seeded Editors group.
 *
 * Not needed to reach a door: a club's starter ruleset already lets its members create the
 * club's trips, and the doors open on that. This is for the one case about the other caller —
 * somebody nothing obliges to file their work under a club — whose form binds nothing and asks
 * nothing, and whose plan the server's own rule decides. The enrolment is undone when the case
 * ends.
 */
async function holdingTheRightOverTrips(admin: Page, userId: string): Promise<() => Promise<void>> {
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
 * plan door reads its default audience from, and the one their right to record the club's trips
 * comes through.
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

/** The same form on its other door, by the heading that door opens with. */
function reportDialog(page: Page): Locator {
  return page.getByRole('dialog', { name: 'New trip log' });
}

/**
 * What the server says this person may create, read before anything is driven.
 *
 * Stated rather than assumed, because every case below about a club-only member proves nothing
 * if the account quietly holds the right over trips as such: the doors would open for that
 * reason and the flow would pass without the club's right ever being what admitted it.
 */
async function createRightsOf(
  page: Page,
): Promise<{ overTrips: boolean; inGroups: { id: string; name: string }[] }> {
  const capabilities = await asPerson<{
    domains: Record<string, string>;
    createInCavingGroups: Record<string, { id: string; name: string }[]>;
  }>(page, 'GET', '/api/v1/me/capabilities');
  return {
    overTrips: (capabilities.domains.tripLogs ?? '')
      .split(',')
      .map((action) => action.trim())
      .includes('create'),
    inGroups: capabilities.createInCavingGroups.tripLogs ?? [],
  };
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

/** The route each door posts to. */
const planDoor = '/api/v1/trip-logs/plans';
const reportDoor = '/api/v1/trip-logs';

/**
 * Fills the form's title and saves it through the door named, handing back the trip the server
 * created once the page has moved to it. Which door the request went through is part of what is
 * asserted: the wait is for a post to that route and no other.
 */
async function saveThrough(
  page: Page,
  dialog: Locator,
  title: string,
  door: typeof planDoor | typeof reportDoor,
): Promise<string> {
  await dialog.getByLabel('Title', { exact: true }).fill(title);
  const created = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' && new URL(response.url()).pathname === door,
  );
  await dialog.getByRole('button', { name: 'OK' }).click();
  const response = await created;
  expect(response.status(), await response.text()).toBe(201);
  const trip = (await response.json()) as { id: string };
  await page.waitForURL((url) => url.pathname === `/trip-logs/${trip.id}`, { timeout: 30_000 });
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  return trip.id;
}

test('a plan opened from my trips by a member whose only right is their club\'s is shared with that club, and the form names it first', async ({
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

  try {
    groupId = await groupWith(admin.page, (await caverOf(admin.page, planner)).id, groupName);

    await login(page, planner.email, planner.password);
    // The person holds nothing over trips as such; what they hold is the club's. If that were
    // not so, the door below would prove nothing about a member whose right is by club only.
    const rights = await createRightsOf(page);
    expect(rights.overTrips, 'a self-registered member holds no create over trips as such').toBe(false);
    expect(rights.inGroups).toEqual([{ id: groupId, name: groupName }]);

    await gotoRoute(page, '/trip-logs/mine');
    // The door is there at all, which is the first thing this case is about.
    await page.getByTestId('my-trips-plan').click();
    const dialog = planDialog(page);
    await expect(dialog).toBeVisible();

    // Before the trip exists: the control shows the group audience, and the line beside it
    // names the group rather than calling it "your group" — as does the line saying the plan
    // will belong to that group.
    await expect(chosenAudience(dialog)).toContainText('Caving group', { timeout: 15_000 });
    await expect(dialog.getByTestId('trip-plan-audience')).toContainText(groupName);
    await expect(dialog.getByTestId('trip-owning-group')).toContainText(
      `You record trips for ${groupName}; this one will belong to it.`,
    );

    tripId = await saveThrough(page, dialog, title, planDoor);

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
    await admin.context.close();
  }
});

test('a member whose only right is their club\'s files a report through the report door, and it lands bound to the club', async ({
  page,
  browser,
  request,
}) => {
  test.slow();
  const stamp = Date.now();
  const groupName = `E2E Report Club ${stamp}`;
  const title = `E2E Club Report ${stamp}`;
  const reporter = await registerAccount(request, 'reporter');
  const admin = await signedInElsewhere(browser);
  let groupId: string | undefined;
  let tripId: string | undefined;

  try {
    groupId = await groupWith(admin.page, (await caverOf(admin.page, reporter)).id, groupName);

    await login(page, reporter.email, reporter.password);
    const rights = await createRightsOf(page);
    expect(rights.overTrips, 'a self-registered member holds no create over trips as such').toBe(false);
    expect(rights.inGroups).toEqual([{ id: groupId, name: groupName }]);

    await gotoRoute(page, '/trip-logs');
    // The create control's own button, which files a report. Its accessible name carries the
    // icon's as well, so it is found by its words.
    await page.getByRole('button', { name: /New trip log/ }).click();
    const dialog = reportDialog(page);
    await expect(dialog).toBeVisible();

    // Before the trip exists: the form says the report will belong to the club, by name, and
    // the audience beside it is the narrowest there is — a report starts private, as it always
    // did, and the binding is what the server admits it on.
    await expect(dialog.getByTestId('trip-owning-group')).toContainText(
      `You record trips for ${groupName}; this one will belong to it.`,
      { timeout: 15_000 },
    );
    await expect(chosenAudience(dialog)).toContainText('Private');

    // Through the report door and no other. Left unbound, as this form used to send every
    // report, the server refuses this person the trip.
    tripId = await saveThrough(page, dialog, title, reportDoor);

    await expect(statedAudience(page)).toHaveText('Private');
    const stored = await asPerson<{ visibility: string; cavingGroupId: string | null }>(
      page,
      'GET',
      `/api/v1/trip-logs/${tripId}`,
    );
    expect(stored.visibility).toBe('private');
    expect(stored.cavingGroupId).toBe(groupId);
  } finally {
    if (tripId) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    if (groupId) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/caving-groups/${groupId}`);
    }
    await admin.context.close();
  }
});

test('a member of two clubs whose only right is theirs is asked which one a plan belongs to, and it is shared with the one chosen', async ({
  page,
  browser,
  request,
}) => {
  test.slow();
  const stamp = Date.now();
  const firstName = `E2E Alpha Club ${stamp}`;
  const secondName = `E2E Beta Club ${stamp}`;
  const title = `E2E Chosen Outing ${stamp}`;
  const planner = await registerAccount(request, 'chooser');
  const admin = await signedInElsewhere(browser);
  const groupIds: string[] = [];
  let tripId: string | undefined;

  try {
    const caver = await caverOf(admin.page, planner);
    groupIds.push(await groupWith(admin.page, caver.id, firstName));
    groupIds.push(await groupWith(admin.page, caver.id, secondName));

    await login(page, planner.email, planner.password);
    const rights = await createRightsOf(page);
    expect(rights.overTrips, 'a self-registered member holds no create over trips as such').toBe(false);
    expect(rights.inGroups.map((group) => group.name)).toEqual([firstName, secondName]);

    await gotoRoute(page, '/trip-logs');
    await page.getByTestId('trip-create-menu').click();
    await page.getByRole('menuitem', { name: /Plan a trip/ }).click();
    const dialog = planDialog(page);
    await expect(dialog).toBeVisible();

    // Two clubs carry no order and no precedence, so the form names neither: it asks. What it
    // does say is that the plan is shared with whichever club it is made to belong to.
    await expect(dialog.getByTestId('trip-owning-group')).toHaveCount(0);
    await expect(dialog.getByTestId('trip-plan-audience')).toContainText(
      'the caving group it belongs to',
      { timeout: 15_000 },
    );
    await expect(chosenAudience(dialog)).toContainText('Caving group');

    // Pressing OK before choosing is refused on the field, and nothing reaches the server: a
    // plan with no club behind it is the one request this person would be turned down for.
    const posts: string[] = [];
    page.on('request', (sent) => {
      if (sent.method() === 'POST' && new URL(sent.url()).pathname.startsWith(reportDoor)) {
        posts.push(sent.url());
      }
    });
    await dialog.getByLabel('Title', { exact: true }).fill(title);
    await dialog.getByRole('button', { name: 'OK' }).click();
    await expect(dialog.getByText('Choose the caving group this trip belongs to.')).toBeVisible();
    expect(posts).toEqual([]);

    // The choice is exactly the clubs the server said it would accept a trip in.
    const choice = dialog.getByTestId('trip-owning-group-choice');
    await choice.click();
    const offered = page.locator('.ant-select-dropdown:visible .ant-select-item-option');
    await expect(offered).toHaveText([firstName, secondName]);
    // Put away by moving on to another field rather than with Escape, which a dialog also
    // listens for; then chosen the way every select here is chosen, and read back off the
    // control.
    await dialog.getByLabel('Title', { exact: true }).click();
    await expect(offered).toHaveCount(0);
    await chooseOption(page, choice, secondName);

    tripId = await saveThrough(page, dialog, title, planDoor);

    await expect(statedAudience(page)).toHaveText('Caving group');
    const stored = await asPerson<{ visibility: string; cavingGroupId: string | null }>(
      page,
      'GET',
      `/api/v1/trip-logs/${tripId}`,
    );
    expect(stored.visibility).toBe('cavingGroup');
    expect(stored.cavingGroupId).toBe(groupIds[1]);
  } finally {
    if (tripId) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    for (const groupId of groupIds) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/caving-groups/${groupId}`);
    }
    await admin.context.close();
  }
});

test('a plan opened from the trip list by somebody who holds the right over trips and is in two caving groups starts private, and the form says why', async ({
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
    unenrol = await holdingTheRightOverTrips(admin.page, planner.id);
    const caver = await caverOf(admin.page, planner);
    groupIds.push(await groupWith(admin.page, caver.id, `E2E First Club ${stamp}`));
    groupIds.push(await groupWith(admin.page, caver.id, `E2E Second Club ${stamp}`));

    await login(page, planner.email, planner.password);
    // The other caller, stated: this one holds the right over trips as such, so nothing obliges
    // the plan to belong to either club.
    expect((await createRightsOf(page)).overTrips).toBe(true);
    await gotoRoute(page, '/trip-logs');
    // The second door is behind the create button, in the menu beside it.
    await page.getByTestId('trip-create-menu').click();
    await page.getByRole('menuitem', { name: /Plan a trip/ }).click();
    const dialog = planDialog(page);
    await expect(dialog).toBeVisible();

    // Two groups carry no order and no precedence, so nothing is guessed: private, said in words.
    // And nothing is asked either — the choice of a club is for somebody who has to make one.
    await expect(dialog.getByTestId('trip-plan-audience')).toContainText('starts private', {
      timeout: 15_000,
    });
    await expect(chosenAudience(dialog)).toContainText('Private');
    await expect(dialog.getByTestId('trip-owning-group-choice')).toHaveCount(0);
    await expect(dialog.getByTestId('trip-owning-group')).toHaveCount(0);

    tripId = await saveThrough(page, dialog, title, planDoor);
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
