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
  type Account,
} from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * The quick way to open a cave to somebody who was asked on a trip there.
 *
 * Being asked on a trip grants nothing, so whoever can open the cave is told, and what they
 * are told leads to the cave's own page, its permissions dialog, and the account the message
 * is about. That message has two forms and both are driven here. The first flow follows the
 * address the way a mail client follows it — cold, with nothing in memory. The second causes
 * the message for real, by somebody asking somebody on a trip, and follows the line it leaves
 * in the inbox, which an installation that sends no mail has nothing else but.
 *
 * What is asserted either way is the pair that makes such a link worth sending and safe to
 * send. It leaves its reader one confirmation away from the grant, and it grants nothing until
 * that confirmation: the person it is about is asked what they can open before the link is
 * followed, after the dialog has drawn its proposal, and after the reader has said yes.
 *
 * The watched page is the one doing the granting. Everybody else has a browser of their own,
 * and is only ever asked what the server answers them.
 */

/** What the server answers this person for one address, refusal included. */
async function answerFor(page: Page, path: string): Promise<number> {
  const token = await bearerToken(page);
  const response = await page.request.get(path, { headers: { Authorization: `Bearer ${token}` } });
  return response.status();
}

/**
 * A cave only its maker can open. Private, so that nothing but a rule naming somebody lets them
 * in: a cave anybody signed in can read would open for them before and after alike, and prove
 * nothing.
 */
async function shutCave(owner: Page, name: string): Promise<{ id: string }> {
  const caveTypes = await asPerson<{ id: number }[]>(owner, 'GET', '/api/v1/cave-types');
  return asPerson<{ id: string }>(owner, 'POST', '/api/v1/caves', {
    name,
    caveTypeId: caveTypes[0].id,
    visibility: 'private',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
}

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
 * Lets a self-registered account write trips, by enrolling it in the seeded Editors group; the
 * enrolment is undone when the flow ends. An account that only registered holds no right to
 * create a trip anywhere, and somebody has to do the asking.
 */
async function ableToOrganise(admin: Page, userId: string): Promise<() => Promise<void>> {
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

test('a link about somebody who cannot open a cave proposes read access for them, and one confirmation grants it', async ({
  page,
  browser,
  request,
}) => {
  // Two people, two full sign-ins, and a third round trip through the authorization server when
  // the address is followed. It needs room.
  test.slow();
  const stamp = Date.now();
  const caveName = `E2E Shut Cave ${stamp}`;
  const invitee = await registerAccount(request, 'invitee');

  await login(page);
  const cave = await shutCave(page, caveName);
  const theirs = await signedInElsewhere(browser, invitee);
  const cavePath = `/api/v1/caves/${cave.id}`;

  try {
    expect(await answerFor(theirs.page, cavePath), 'shut to them before anything is granted').toBe(404);

    // The address a message carries, followed cold.
    await gotoRoute(page, `/caves/${cave.id}?permissions=1&grantTo=${invitee.id}`);
    const modal = page.getByRole('dialog', { name: 'Permissions' });
    await expect(modal).toBeVisible({ timeout: 30_000 });

    // The dialog says why a row nobody typed is in it, and whom it is about.
    const why = modal.getByTestId('permissions-proposed');
    await expect(why).toContainText(invitee.displayName, { timeout: 15_000 });
    await expect(why).toContainText('Nothing is granted until you press OK');

    // One row, marked, with Read ticked and nothing else — in particular not the exact
    // position, which is never something an address decides for its reader.
    const row = modal.getByRole('row', { name: new RegExp(invitee.displayName) });
    await expect(row).toContainText('proposed');
    await expect(row.getByRole('checkbox', { checked: true })).toHaveCount(1);
    await expect(row.getByRole('checkbox').first()).toBeChecked();

    // Following the link granted nothing.
    expect(await answerFor(theirs.page, cavePath), 'still shut while the proposal is only drawn').toBe(404);

    const saved = page.waitForResponse(
      (response) =>
        response.request().method() === 'PUT' &&
        response.url().endsWith(`/api/v1/objects/feature/${cave.id}/access`),
    );
    await modal.getByRole('button', { name: 'OK' }).click();
    expect((await saved).status()).toBe(200);
    await expect(modal).toBeHidden({ timeout: 15_000 });

    // The address is handed back clean: neither the dialog nor the account is left in it.
    await expect.poll(() => new URL(page.url()).search).toBe('');

    // And the person it was about can open the cave — to read it, and no further.
    expect(await answerFor(theirs.page, cavePath), 'open to them once the reader confirmed').toBe(200);
    const held = await asPerson<{ actions: string }>(
      theirs.page,
      'GET',
      `/api/v1/objects/feature/${cave.id}/effective-access`,
    );
    expect(held.actions).toBe('read');
  } finally {
    await tryAsPerson(page, 'DELETE', cavePath);
    await theirs.context.close();
  }
});

test('somebody asked on a trip to a shut cave is one inbox line and one confirmation away from being let in', async ({
  page,
  browser,
  request,
}) => {
  // Three people and three full sign-ins: whoever does the asking is never the one told about
  // it, so the organiser, the cave's keeper and the person asked cannot be fewer than three.
  test.slow();
  const stamp = Date.now();
  const caveName = `E2E Guarded Cave ${stamp}`;
  const organiser = await registerAccount(request, 'organiser');
  const invitee = await registerAccount(request, 'asked');

  // The keeper is the administrator: the cave's owner, who is told and who may grant.
  await login(page);
  const cave = await shutCave(page, caveName);
  const cavePath = `/api/v1/caves/${cave.id}`;
  const theirs = await signedInElsewhere(browser, invitee);
  const organisers = await signedInElsewhere(browser, organiser);
  let tripId: string | undefined;
  let unenrol: (() => Promise<void>) | undefined;

  try {
    // The organiser can write a trip, and can read the cave it is to — by a rule of the cave's
    // own, which is also what the dialog has to carry along when it saves a second one.
    unenrol = await ableToOrganise(page, organiser.id);
    await asPerson(page, 'PUT', `/api/v1/objects/feature/${cave.id}/access`, {
      entries: [
        {
          subjectKind: 'user',
          subjectId: organiser.id,
          effect: 'allow',
          actions: 'read',
          scopeKind: 'object',
        },
      ],
    });
    const asked = await caverOf(page, invitee);

    // The act that causes the message: a trip to the cave, and somebody asked on it who cannot
    // open the cave.
    const trip = await asPerson<{ id: string }>(
      organisers.page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(`E2E Guarded Trip ${stamp}`, localDay(9), { caveIds: [cave.id] }),
    );
    tripId = trip.id;
    await asPerson(organisers.page, 'POST', `/api/v1/trip-logs/${trip.id}/invitations`, {
      caverId: asked.id,
    });
    expect(await answerFor(theirs.page, cavePath), 'being asked opened nothing').toBe(404);

    // The keeper reads their inbox. The line names the person and the cave, and it is a link.
    await page.getByRole('button', { name: 'Notifications' }).click();
    await page.waitForURL((url) => url.pathname === '/notifications', { timeout: 30_000 });
    const line = page.getByRole('row', {
      name: new RegExp(`${invitee.displayName} cannot open ${caveName}`),
    });
    await expect(line).toBeVisible({ timeout: 15_000 });
    await line.getByRole('link').click();

    // It lands on the cave, with its permissions open about that person.
    await page.waitForURL((url) => url.pathname === `/caves/${cave.id}`, { timeout: 30_000 });
    const modal = page.getByRole('dialog', { name: 'Permissions' });
    await expect(modal).toBeVisible({ timeout: 30_000 });
    await expect(modal.getByTestId('permissions-proposed')).toContainText(invitee.displayName, {
      timeout: 15_000,
    });
    const proposed = modal.getByRole('row', { name: new RegExp(invitee.displayName) });
    await expect(proposed).toContainText('proposed');
    await expect(proposed.getByRole('checkbox', { checked: true })).toHaveCount(1);
    // The rule already in force is listed beside it, and is not marked as anything new.
    const standing = modal.getByRole('row', { name: new RegExp(organiser.displayName) });
    await expect(standing).toBeVisible();
    await expect(standing).not.toContainText('proposed');

    expect(await answerFor(theirs.page, cavePath), 'still shut while the proposal is only drawn').toBe(404);

    const saved = page.waitForResponse(
      (response) =>
        response.request().method() === 'PUT' &&
        response.url().endsWith(`/api/v1/objects/feature/${cave.id}/access`),
    );
    await modal.getByRole('button', { name: 'OK' }).click();
    expect((await saved).status()).toBe(200);
    await expect(modal).toBeHidden({ timeout: 15_000 });
    await expect.poll(() => new URL(page.url()).search).toBe('');

    // The person asked can open the cave, and the organiser — whose rule the save had to carry
    // along rather than replace — still can.
    expect(await answerFor(theirs.page, cavePath), 'open to them once the keeper confirmed').toBe(200);
    expect(await answerFor(organisers.page, cavePath), 'the rule already there survived the save').toBe(200);
  } finally {
    if (tripId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    await tryAsPerson(page, 'DELETE', cavePath);
    await unenrol?.();
    await organisers.context.close();
    await theirs.context.close();
  }
});
