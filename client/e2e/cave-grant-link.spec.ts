// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount, signedInElsewhere, tryAsPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * The quick way to open a cave to somebody who was asked on a trip there.
 *
 * Being asked on a trip grants nothing, so whoever can open the cave is sent a message with a
 * link: the cave's own page, its permissions dialog, and the account the message is about. The
 * link is followed here the way a mail client follows it — as a plain address, with nothing in
 * memory — and what is asserted is the pair that makes such a link worth sending and safe to
 * send. It leaves the reader one confirmation away from the grant, and it grants nothing until
 * that confirmation: the person it is about is asked what they can open before the address is
 * followed, after the dialog has drawn its proposal, and after the reader has said yes.
 *
 * The watched page is the one doing the granting. The person being granted to has a browser of
 * their own, and is only ever asked what the server answers them.
 */

/** What the server answers this person for one address, refusal included. */
async function answerFor(page: Page, path: string): Promise<number> {
  const token = await bearerToken(page);
  const response = await page.request.get(path, { headers: { Authorization: `Bearer ${token}` } });
  return response.status();
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
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  // Private, so that nothing but a rule naming them lets the other person in: a cave anybody
  // signed in can read would open for them before and after alike, and prove nothing.
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: caveName,
    caveTypeId: caveTypes[0].id,
    visibility: 'private',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
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
