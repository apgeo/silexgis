// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { registerAccount, signedInElsewhere, type Account } from './arrange.ts';
import { confirmOk, gotoRoute, login } from './helpers.ts';

/**
 * A member changing who they are to the installation: their sign-in address, their user name, and
 * the copy of their data they can take away.
 *
 * Each change is proved where it has to hold: the account signs in afresh, in a browser of its own,
 * and the server names what was changed. Each test registers an account of its own, so nothing the
 * shared administrator holds is changed.
 *
 * The address change and the user-name change both rotate the account's sign-in stamp, and the
 * session the member is in has to be renewed with it rather than cleared — otherwise the next page
 * load lands on the sign-in page. The last two tests walk exactly that.
 *
 * The address change cannot be finished without the message it sends. A development host with no
 * mail server hands that link back in a response header instead — the way an invitation link is
 * handed to the administrator when nothing could be sent — and this flow follows it exactly as a
 * member would follow the mailed one.
 */

const developmentLinkHeader = 'x-silexgis-development-link';

/**
 * Asks for an address change on the settings page and returns the confirmation link a development
 * host without mail hands back, after checking the change is staged and not yet applied.
 */
async function requestAddressChange(page: Page, account: Account, newEmail: string): Promise<URL> {
  await gotoRoute(page, '/settings/emails');
  await expect(page.getByText(account.email, { exact: true })).toBeVisible();

  await page.getByLabel('New address').fill(newEmail);
  const asked = page.waitForResponse(
    (r) => r.url().endsWith('/api/v1/me/email/change') && r.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Send the link' }).click();
  const answer = await asked;
  expect(answer.status()).toBe(202);
  const handed = answer.headers()[developmentLinkHeader];
  expect(
    handed,
    'a development host without mail hands the confirmation link back; is a mail server configured for this run?',
  ).toBeTruthy();
  const link = new URL(handed!);
  expect(link.pathname).toBe('/settings/emails');
  expect(link.searchParams.get('confirm')).toBeTruthy();

  // Staged, not applied: the page names the waiting address and still shows the old one.
  await expect(page.getByText(`Waiting for ${newEmail} to be confirmed`)).toBeVisible();
  await expect(page.getByText(account.email, { exact: true })).toBeVisible();
  return link;
}

/** Follows the link by hard navigation, as opening it from a mailbox is, and waits for the confirm. */
async function followConfirmation(page: Page, link: URL, newEmail: string) {
  const confirming = page.waitForResponse(
    (r) => r.url().endsWith('/api/v1/me/email/confirm') && r.request().method() === 'POST',
    { timeout: 60_000 },
  );
  await gotoRoute(page, `${link.pathname}${link.search}`);
  expect((await confirming).status()).toBe(200);
  await expect(page.getByText('Your address has been changed.')).toBeVisible();
  await expect(page.getByText(newEmail, { exact: true })).toBeVisible();
  // The single-use token is taken out of the address once spent.
  await expect(page).not.toHaveURL(/confirm=/);
}

/** Changes the user name on the account page and waits for the server to accept it. */
async function changeUsername(page: Page, newName: string) {
  await gotoRoute(page, '/settings/account');
  const field = page.getByLabel('Username');
  await expect(field).toBeVisible();
  await field.fill(newName);
  const renamed = page.waitForResponse(
    (r) => r.url().endsWith('/api/v1/me/username') && r.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Change username' }).click();
  await confirmOk(page, { scope: page.locator('.ant-popconfirm') });
  expect((await renamed).ok()).toBeTruthy();
  await expect(page.getByText('Username changed.')).toBeVisible();
}

/** Whether signing in with these credentials succeeds, asked of the server outside any browser. */
async function signInAnswers(email: string, password: string): Promise<number> {
  const base = test.info().project.use.baseURL ?? '';
  const answer = await fetch(new URL('/api/v1/auth/login', base), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  return answer.status;
}

test.describe('own identity', () => {
  test('a member changes their sign-in address through the link sent to it, and signs in with the new one', async ({
    page,
    browser,
  }) => {
    test.setTimeout(180_000);
    const account = await registerAccount(page.request, 'identity');
    const newEmail = account.email.replace('e2e-identity-', 'e2e-identity-moved-');
    await login(page, account.email, account.password);
    const link = await requestAddressChange(page, account, newEmail);
    await followConfirmation(page, link, newEmail);

    // The old address no longer opens the account; the new one does, with the same password.
    expect(await signInAnswers(account.email, account.password)).not.toBe(200);
    const moved = await signedInElsewhere(browser, { ...account, email: newEmail });
    try {
      const reread = moved.page.waitForResponse(
        (r) => r.url().endsWith('/api/v1/me') && r.request().method() === 'GET',
        { timeout: 60_000 },
      );
      await gotoRoute(moved.page, '/settings/emails');
      const me = (await (await reread).json()) as { email: string; pendingEmail: string | null };
      expect(me.email).toBe(newEmail);
      expect(me.pendingEmail).toBeNull();
      await expect(moved.page.getByText(newEmail, { exact: true })).toBeVisible();
      await expect(moved.page.getByText('Confirmed', { exact: true })).toBeVisible();
    } finally {
      await moved.context.close();
    }
  });

  test('a member changes their user name and asks for, then downloads, a copy of their data', async ({
    page,
    browser,
    consoleErrors,
  }) => {
    test.setTimeout(180_000);
    // An account that never asked for an export is answered 404 when the page reads its newest
    // one — the page's normal empty state, which the browser still reports as a failed resource.
    // Narrowed to that one address, so a 404 from anything else this flow does is still caught.
    consoleErrors.allow(
      /\/api\/v1\/me\/data-export(\?|:|$)/,
      "a missing data export is the account page's normal empty state, answered 404 and read as none",
    );
    const account = await registerAccount(page.request, 'username');
    const newName = `e2e-renamed-${Date.now()}`;
    await login(page, account.email, account.password);
    await changeUsername(page, newName);

    // Read back in a session begun after the change, so the field shows what the server holds.
    const fresh = await signedInElsewhere(browser, account);
    try {
      await gotoRoute(fresh.page, '/settings/account');
      await expect(fresh.page.getByLabel('Username')).toHaveValue(newName);

      // The export is built in the background; the page shows Download once it is ready.
      const requested = fresh.page.waitForResponse(
        (r) => r.url().endsWith('/api/v1/me/data-export') && r.request().method() === 'POST',
      );
      await fresh.page.getByRole('button', { name: 'Prepare an export' }).click();
      expect((await requested).ok()).toBeTruthy();
      const download = fresh.page.getByRole('button', { name: 'Download' });
      await expect(download).toBeVisible({ timeout: 120_000 });

      const saving = fresh.page.waitForEvent('download');
      await download.click();
      const saved = await saving;
      expect(saved.suggestedFilename().length).toBeGreaterThan(0);
      const path = await saved.path();
      expect(path, 'the export was handed to the browser as a file').toBeTruthy();
    } finally {
      await fresh.context.close();
    }
  });
});

test.describe('the session a member is in, after an identity change', () => {
  test('outlives a confirmed address change', async ({ page }) => {
    test.setTimeout(180_000);
    const account = await registerAccount(page.request, 'stay-moved');
    const newEmail = account.email.replace('e2e-stay-moved-', 'e2e-stay-moved-new-');
    await login(page, account.email, account.password);
    const link = await requestAddressChange(page, account, newEmail);
    await followConfirmation(page, link, newEmail);
    // A fresh round trip through the authorization server lands on the page, still signed in.
    await gotoRoute(page, '/settings/emails');
  });

  test('outlives a user-name change', async ({ page, consoleErrors }) => {
    test.setTimeout(180_000);
    // The account page reads the newest data export, and an account that never asked for one is
    // answered 404 — the page's normal empty state.
    consoleErrors.allow(
      /\/api\/v1\/me\/data-export(\?|:|$)/,
      "a missing data export is the account page's normal empty state, answered 404 and read as none",
    );
    const account = await registerAccount(page.request, 'stay-renamed');
    await login(page, account.email, account.password);
    await changeUsername(page, `e2e-stay-renamed-${Date.now()}`);
    await gotoRoute(page, '/settings/account');
  });
});
