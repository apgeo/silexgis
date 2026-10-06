// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The calendar subscription feed, from the two screens that govern it: the administrator's switch
 * that offers feeds at all, and the account page where a person mints, reads once and withdraws an
 * address — with the address then polled the way a calendar application polls it, carrying nothing.
 *
 * The API in this stack answers at its default public address, which is not where the browser is,
 * so the minted address is polled by its path against the stack's own API rather than as written.
 */

interface ProtectionSettings {
  revealProtectedAssociations: boolean;
  calendarFeedEnabled: boolean;
}

/** The protection section as the administrator last saved it, so the flow can put it back. */
async function protectionNow(page: Page): Promise<ProtectionSettings> {
  const settings = await asPerson<{ protection: ProtectionSettings }>(page, 'GET', '/api/v1/admin/settings');
  return settings.protection;
}

test.describe('the calendar feed', () => {
  test.beforeEach(async ({ page, consoleErrors }) => {
    // The account page asks for the caller's newest data export on every visit, and an account
    // that never requested one is answered 404 — the page's normal empty state, read as "none",
    // which the browser still reports to its console as a failed resource. The browser's own
    // text names no address, so the match is on the status; nothing else this flow does in the
    // browser is answered 404 (the feed is polled through the request context, not the page).
    consoleErrors.allow(
      /Failed to load resource: .*404/,
      "a missing data export is the account page's normal empty state, answered 404 and read as none",
    );
    await login(page);
  });

  test('an administrator switches feeds on, a member mints an address, polls it without signing in and withdraws it', async ({
    page,
  }) => {
    const before = await protectionNow(page);
    try {
      // The switch is on the protection tab of the installation settings, because offering feeds is
      // a disclosure decision: it lets a member hand a third-party calendar service a window into
      // their own commitments.
      await gotoRoute(page, '/admin/messaging');
      await page.getByRole('tab', { name: 'Location protection' }).click();
      const pane = page.getByRole('tabpanel', { name: /Location protection/ });
      const feedSwitch = pane.getByRole('switch', {
        name: 'Let members subscribe to their calendar from outside',
      });
      await expect(feedSwitch).toBeVisible({ timeout: 15_000 });
      if ((await feedSwitch.getAttribute('aria-checked')) !== 'true') {
        await feedSwitch.click();
      }
      await pane.getByRole('button', { name: 'Save' }).click();
      await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

      // Minting, on the account's own settings page.
      await gotoRoute(page, '/settings/account');
      const stamp = Date.now();
      await page.getByLabel('Name for this address').fill(`E2E phone ${stamp}`);
      await page.getByRole('button', { name: 'Create a feed address' }).click();
      const shown = page.locator('code').filter({ hasText: /\/api\/v1\/calendar\/feed\/.+\.ics$/ });
      await expect(shown).toBeVisible({ timeout: 15_000 });
      const url = (await shown.textContent())?.trim();
      expect(url, 'the mint showed no address').toBeTruthy();
      await expect(page.getByText(/shown only once/)).toBeVisible();
      // The row is listed by its name, and the list carries no address: once minted, the address
      // exists only where the calendar application keeps it.
      await expect(page.getByText(`E2E phone ${stamp}`)).toBeVisible();
      expect(await page.getByTestId('calendar-feed-row').getByText(/calendar\/feed\//).count()).toBe(0);

      // A calendar application polls with nothing: no session, no header.
      const path = new URL(url!).pathname;
      const polled = await page.request.get(path, { headers: {} });
      expect(polled.status(), `polling the feed answered ${polled.status()}`).toBe(200);
      expect(polled.headers()['content-type']).toMatch(/^text\/calendar/);
      const body = await polled.text();
      expect(body).toContain('BEGIN:VCALENDAR');
      expect(body).toContain('METHOD:PUBLISH');
      expect(body).toContain('END:VCALENDAR');

      // Withdrawing it, from the row.
      const row = page.getByTestId('calendar-feed-row').filter({ hasText: `E2E phone ${stamp}` });
      await row.getByRole('button', { name: 'Revoke' }).click();
      // Exact, because the default name match is a substring and "Revoke" contains "ok".
      await page.getByRole('button', { name: 'OK', exact: true }).click();
      await expect(page.getByText('Address revoked.')).toBeVisible({ timeout: 15_000 });
      await expect(row.getByText('Revoked')).toBeVisible();
      await expect(row.getByRole('button', { name: 'Revoke' })).toHaveCount(0);

      // The withdrawn address answers exactly what an invented one does.
      const afterRevoke = await page.request.get(path, { headers: {} });
      expect(afterRevoke.status()).toBe(404);
    } finally {
      // Feeds are off by default, and the other flows in this run expect the installation as it
      // was seeded; the section is put back whole, because saving it replaces it whole.
      await asPerson(page, 'PUT', '/api/v1/admin/settings/protection', before);
    }
  });

  test('the account page offers no feed section while the installation has feeds switched off', async ({
    page,
  }) => {
    const before = await protectionNow(page);
    await asPerson(page, 'PUT', '/api/v1/admin/settings/protection', {
      ...before,
      calendarFeedEnabled: false,
    });
    try {
      await gotoRoute(page, '/settings/account');
      // The page has rendered when its first card is on screen; only then can the absence of the
      // feed card mean anything.
      await expect(page.getByText('Export your data')).toBeVisible({ timeout: 15_000 });
      await expect(page.getByRole('button', { name: 'Create a feed address' })).toHaveCount(0);
    } finally {
      await asPerson(page, 'PUT', '/api/v1/admin/settings/protection', before);
    }
  });
});
