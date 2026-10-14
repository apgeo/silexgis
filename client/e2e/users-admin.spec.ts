// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { ownContext, test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * The account register, driven end to end: an administrator opens an account for somebody, the
 * somebody chooses a password through the link and signs in, and the administrator then locks the
 * account and deletes it.
 *
 * <b>The link is followed, not only shown.</b> The runner starts the server with no mail server,
 * so the invitation comes back to the administrator as a link to hand over. A link that appeared
 * on screen and let nobody in would pass every check short of opening it, so a second browser —
 * with no session, as the invitee's would be — opens it and signs in with what it chose.
 *
 * <b>The page is reached through the rail</b>, since a page registered with the router and missing
 * from the rail is one nobody finds while nothing fails. The subject is an account made for this
 * run and removed by it; the shared administrator is only ever the one acting.
 */
test('an administrator invites somebody, who sets a password and signs in, then locks and deletes the account', async ({
  page,
  browser,
}) => {
  test.setTimeout(180_000);
  const stamp = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const name = `E2E Invitat ${stamp}`;
  const email = `e2e-invite-${stamp}@dev.local`;
  const password = `e2e-invite-pass-${stamp}`;

  await login(page);
  await page.getByRole('menuitem', { name: /Administration$/ }).click();
  await page.getByRole('menuitem', { name: 'Accounts' }).click();
  await expect(page.getByRole('heading', { name: 'Accounts' })).toBeVisible({ timeout: 15_000 });

  await page.getByRole('button', { name: 'Invite someone' }).click();
  const invite = page.getByRole('dialog', { name: 'Invite someone' });
  await invite.getByLabel('Name').fill(name);
  await invite.getByLabel('Email address').fill(email);
  await invite.getByRole('button', { name: 'Open the account' }).click();

  const handOver = page.getByRole('dialog', { name: 'Hand this link over' });
  await expect(handOver).toBeVisible({ timeout: 15_000 });
  const link = (await handOver.getByTestId('hand-over-link').textContent())?.trim() ?? '';
  expect(link).toContain('/reset-password?');
  await handOver.getByRole('button', { name: 'I have copied it' }).click();
  await expect(handOver).toBeHidden();

  // The invitee, in a browser of their own. The address the server wrote names whatever public
  // address it was configured with; the path and the token are what matter, so they are opened
  // against the application under test.
  const context = await ownContext(browser);
  try {
    const invitee = await context.newPage();
    const target = new URL(link);
    await invitee.goto(`${target.pathname}${target.search}`);
    await expect(invitee.getByRole('heading', { name: 'Choose your password' })).toBeVisible({ timeout: 30_000 });
    await invitee.getByLabel('New password').fill(password);
    await invitee.getByLabel('Repeat password').fill(password);
    await invitee.getByRole('button', { name: 'Set password' }).click();
    await expect(invitee.getByText('Your password is set. You can sign in now.')).toBeVisible({ timeout: 15_000 });

    await login(invitee, email, password);
  } finally {
    await context.close();
  }

  // Searching reads the register again, so the row now says the account has been entered.
  await page.getByLabel('Search accounts').fill(email);
  const row = page.getByRole('row', { name: new RegExp(email.replace(/[.]/g, '\\.')) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await expect(row.getByText('Can sign in')).toBeVisible();
  await expect(row.getByText('No password yet')).toHaveCount(0);

  await row.getByRole('button', { name: 'Lock' }).click();
  await expect(page.getByText(`${name} is locked out.`)).toBeVisible({ timeout: 15_000 });
  await expect(row.getByText('Locked')).toBeVisible();

  await row.getByRole('button', { name: 'Delete' }).click();
  const confirm = page.getByRole('tooltip').filter({ hasText: `Delete the account of ${name}?` });
  await expect(confirm).toBeVisible();
  await confirm.getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByText(`The account of ${name} was deleted.`)).toBeVisible({ timeout: 15_000 });
  await expect(row).toHaveCount(0);
});
