// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHmac } from 'node:crypto';
import { expect, type Browser, type Page } from '@playwright/test';
import { ownContext, test } from './consoleGuard.ts';
import { registerAccount, type Account } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * Two-factor sign-in walked from the member's side: an authenticator turned on in the account's
 * security settings, then a sign-in that is refused for want of a code and completed with one, and
 * a sign-in completed with a recovery code instead.
 *
 * The page and the server are each tested on their own against hand-written bodies — the login
 * page against a refusal it was told to expect, the server against its own property names — so
 * only a browser shows that the two still agree. When they stop agreeing, members with a second
 * factor cannot sign in at all, which is the worst way this can fail.
 *
 * The authenticator's code is computed here from the shared key the page shows (RFC 6238: HMAC-SHA1
 * over the 30-second step, six digits), exactly as an app on a phone computes it.
 */

const step = 30;

function base32Decode(text: string): Buffer {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const clean = text.replace(/[\s=]/g, '').toUpperCase();
  let bits = 0;
  let value = 0;
  const bytes: number[] = [];
  for (const char of clean) {
    const index = alphabet.indexOf(char);
    if (index < 0) throw new Error(`not a base32 character: ${char}`);
    value = (value << 5) | index;
    bits += 5;
    if (bits >= 8) {
      bytes.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return Buffer.from(bytes);
}

function totp(sharedKey: string, at: number): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 1000 / step)));
  const digest = createHmac('sha1', base32Decode(sharedKey)).update(counter).digest();
  const offset = digest[digest.length - 1] & 0x0f;
  const binary = digest.readUInt32BE(offset) & 0x7fffffff;
  return String(binary % 1_000_000).padStart(6, '0');
}

/**
 * A code from a time step no earlier code was taken from. A server may refuse a code it has
 * already accepted once, and the code that switched the authenticator on would otherwise be the
 * one offered again at sign-in when both fall in the same thirty seconds.
 */
async function codeFromAFreshStep(sharedKey: string, after: number): Promise<string> {
  const used = Math.floor(after / 1000 / step);
  while (Math.floor(Date.now() / 1000 / step) <= used) {
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  return totp(sharedKey, Date.now());
}

/** Opens the sign-in form in a browser of its own and submits the password, expecting the challenge. */
async function signInUpToTheChallenge(browser: Browser, account: Account) {
  const context = await ownContext(browser);
  const page = await context.newPage();
  await page.goto('/');
  await page.waitForURL(/\/login\?returnUrl=/, { timeout: 60_000 });
  await page.getByLabel('Email').fill(account.email);
  await page.getByLabel('Password').fill(account.password);
  const refused = page.waitForResponse(
    (r) => r.url().endsWith('/api/v1/auth/login') && r.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Sign in' }).click();
  const answer = await refused;
  expect(answer.status(), 'a password alone does not open an account with a second factor').toBe(401);
  expect(((await answer.json()) as { code?: string }).code).toBe('auth.mfa_required');
  await expect(page.getByText('Enter your second-factor code.')).toBeVisible();
  return { context, page };
}

async function finishSignIn(page: Page) {
  await page.getByRole('button', { name: 'Sign in' }).click();
  // The authorization round trip resumes and the workspace is drawn: the member is in.
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
  await gotoRoute(page, '/settings/security');
}

test.describe('two-factor sign-in', () => {
  test('a member turns on an authenticator, then signs in with its code and with a recovery code', async ({
    page,
    browser,
    consoleErrors,
  }) => {
    test.setTimeout(300_000);
    // Every sign-in here is first refused for want of a second factor, and the last one is refused
    // on purpose for a spent recovery code. Narrowed to the sign-in address, so a refusal from
    // anything else this flow does is still caught.
    consoleErrors.allow(
      /\/api\/v1\/auth\/login(\?|:|$)/,
      'a password alone is refused with the second-factor challenge, and a spent recovery code is refused',
    );
    const account = await registerAccount(page.request, 'twofactor');
    await login(page, account.email, account.password);
    await gotoRoute(page, '/settings/security');

    const authenticator = page.locator('.silex-list-item').filter({ hasText: 'Authenticator app' });
    const enrolled = page.waitForResponse(
      (r) => r.url().endsWith('/api/v1/me/mfa/enroll') && r.request().method() === 'POST',
    );
    await authenticator.getByRole('button', { name: 'Turn on' }).click();
    const enrolment = (await (await enrolled).json()) as { sharedKey: string };
    // The key a member types by hand is the one on screen, not only the one in the answer.
    await expect(page.getByText(enrolment.sharedKey, { exact: true })).toBeVisible();

    const confirmedAt = Date.now();
    await page.getByLabel('6-digit code').fill(totp(enrolment.sharedKey, confirmedAt));
    await page.getByRole('button', { name: 'Confirm' }).click();
    await expect(page.getByText('Two-factor authentication is on.')).toBeVisible();
    await expect(page.getByText('Store these recovery codes now — they are shown only once.')).toBeVisible();
    const recoveryCodes = ((await page.locator('pre').first().textContent()) ?? '')
      .split('\n')
      .map((code) => code.trim())
      .filter((code) => code.length > 0);
    expect(recoveryCodes.length, 'recovery codes are shown once, at enrolment').toBeGreaterThan(1);

    // Signing in with the code the app shows.
    const withCode = await signInUpToTheChallenge(browser, account);
    try {
      await withCode.page
        .getByLabel('Two-factor code')
        .fill(await codeFromAFreshStep(enrolment.sharedKey, confirmedAt));
      await finishSignIn(withCode.page);
      await expect(
        withCode.page.locator('.silex-list-item').filter({ hasText: 'Authenticator app' }).getByText('Enabled', { exact: true }),
      ).toBeVisible();
    } finally {
      await withCode.context.close();
    }

    // Signing in with a recovery code, for the day the phone is lost.
    const withRecovery = await signInUpToTheChallenge(browser, account);
    try {
      await withRecovery.page.getByRole('button', { name: 'Use a recovery code instead' }).click();
      await withRecovery.page.getByLabel('Recovery code').fill(recoveryCodes[0]);
      await finishSignIn(withRecovery.page);
    } finally {
      await withRecovery.context.close();
    }

    // A recovery code is spent once used: the same one does not open the account a second time.
    const again = await signInUpToTheChallenge(browser, account);
    try {
      await again.page.getByRole('button', { name: 'Use a recovery code instead' }).click();
      await again.page.getByLabel('Recovery code').fill(recoveryCodes[0]);
      const refused = again.page.waitForResponse(
        (r) => r.url().endsWith('/api/v1/auth/login') && r.request().method() === 'POST',
      );
      await again.page.getByRole('button', { name: 'Sign in' }).click();
      expect((await refused).status()).toBe(401);
      await expect(again.page.getByText('The two-factor code is not valid.')).toBeVisible();
    } finally {
      await again.context.close();
    }
  });
});
