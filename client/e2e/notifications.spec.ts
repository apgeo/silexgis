// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The inbox, driven the way a person reaches it: something happens to somebody, the header
 * counts it, the page says what it was, reading it takes the count away — and when the thing it
 * was about is taken back, the line stays and says so instead of going blank.
 *
 * The notification is caused by an act the application already offers rather than written into
 * the database behind its back. A row inserted directly would prove the table renders and prove
 * nothing about the pipeline that fills it, which is the half that can silently stop working.
 */
test('a grant is announced in the inbox, counted in the header, and degrades when it is taken back', async ({
  page,
  browser,
  request,
}) => {
  const stamp = Date.now();
  const granteeEmail = `e2e-grantee-${stamp}@dev.local`;
  const granteePassword = 'e2e-grantee-pass-1';
  const granteeName = `E2E Grantee ${stamp}`;
  const caveName = 'Peștera Demo Mare';
  // Two people, two full sign-ins, and a permissions round trip between them. It needs room.
  test.slow();

  // Somebody has to be told, and the person doing the telling is never told: a grant to
  // yourself is not news, so one account cannot stage this at all. Self-registration is the
  // only way an installation mints a second account without an operator, so a run that cannot
  // do it is stated as a missing prerequisite rather than skipped — a skip reads as a
  // non-failure, and this flow is the only cover the inbox has outside the component tests.
  const config = (await (await request.get('/api/v1/auth/config')).json()) as {
    openRegistration?: boolean;
  };
  expect(
    config.openRegistration,
    'this flow needs a second account: start the API with SILEXGIS__Auth__OpenRegistration=true',
  ).toBe(true);
  const registered = await request.post('/api/v1/auth/register', {
    data: { email: granteeEmail, password: granteePassword, displayName: granteeName },
  });
  expect(registered.ok()).toBeTruthy();

  // The administrator works in a browser of their own. The watched page is the one the person
  // reading their inbox uses, because that is the surface this flow is about.
  const adminContext = await browser.newContext();
  const adminPage = await adminContext.newPage();
  const modal = adminPage.getByRole('dialog');

  /** Opens the demo cave's permissions editor, whichever side of the grant we are on. */
  const openPermissions = async () => {
    await gotoRoute(adminPage, '/caves');
    await adminPage.getByText(caveName).click();
    await adminPage.getByRole('button', { name: /Permissions/ }).click();
    await expect(modal).toBeVisible({ timeout: 15_000 });
  };

  try {
    await login(adminPage);
    await openPermissions();

    // User is the subject kind the editor opens on, so only the picker beside it is touched.
    // By display name, not by address: an account's address is private by default and the
    // search refuses to match one — the same refusal that stops the picker being used to ask
    // whether an address has an account here. The name carries the same stamp.
    // The picker beside the subject kind, addressed by position: antd puts the field a person
    // types into behind the shown placeholder, so asking for it by that placeholder finds a box
    // no click can reach and waits out the test rather than failing at the mistake.
    await modal.locator('.ant-select').nth(1).click();
    await adminPage.keyboard.type(granteeName);
    const option = adminPage.locator('.ant-select-item-option').filter({ hasText: granteeName });
    await expect(option).toBeVisible({ timeout: 15_000 });
    await option.click();
    await modal.getByRole('button', { name: 'Add rule' }).click();
    await expect(modal.getByRole('row', { name: new RegExp(granteeName) })).toBeVisible();
    await modal.getByRole('button', { name: 'OK' }).click();
    await expect(adminPage.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });

    // Now the person who was told. A fresh sign-in, so the header's count is asked for once on
    // the way in rather than waited for on its timer.
    await login(page, granteeEmail, granteePassword);
    const bell = page.getByRole('button', { name: 'Notifications' });
    const count = page.locator('.ant-badge').filter({ has: bell }).locator('.ant-badge-count');
    await expect(count).toHaveText('1', { timeout: 15_000 });

    await bell.click();
    await page.waitForURL((url) => url.pathname === '/notifications', { timeout: 30_000 });
    const line = page.getByRole('row', { name: new RegExp(`given access to ${caveName}`) });
    await expect(line).toBeVisible({ timeout: 15_000 });
    // Unread until it is opened, and the mark is the row's own rather than the header's.
    await expect(line.getByTestId('notification-unread')).toBeVisible();

    // Opening the line is what reads it. The header follows without a reload, which is the
    // claim worth driving in a browser: a count that only settles on its next poll would pass
    // every test that stubs the query and none that watches the real invalidation.
    const marked = page.waitForResponse(
      (r) => /\/api\/v1\/notifications\/\d+\/read$/.test(r.url()) && r.request().method() === 'POST',
    );
    await line.click();
    await marked;
    await expect(line.getByTestId('notification-unread')).toBeHidden({ timeout: 15_000 });
    await expect(count).toBeHidden({ timeout: 15_000 });

    // Now take the reading away. The grant alone cannot be the thing that gives it: the demo
    // cave is readable to any signed-in account, so removing the rule leaves the reader exactly
    // where they started and the line would still open. A deny entry is what actually makes this
    // one record unreadable to this one person, which is the state the inbox has to survive.
    await openPermissions();
    // Every grantee this flow has ever left behind, not only this run's: a run that fails
    // between the grant and here leaves its rule on the shared demo cave, and they would
    // otherwise pile up until a bare delete click had no single target.
    const staleGrants = modal.getByRole('row', { name: /E2E Grantee / });
    while ((await staleGrants.count()) > 0) {
      await staleGrants.first().getByRole('button', { name: 'delete' }).click();
    }
    await modal.locator('.ant-select').nth(1).click();
    await adminPage.keyboard.type(granteeName);
    const denySubject = adminPage.locator('.ant-select-item-option').filter({ hasText: granteeName });
    await expect(denySubject).toBeVisible({ timeout: 15_000 });
    await denySubject.click();
    await modal.locator('.ant-select').nth(2).click();
    await adminPage.locator('.ant-select-dropdown:visible').getByText('Deny', { exact: true }).click();
    await modal.getByRole('button', { name: 'Add rule' }).click();
    await modal.getByRole('button', { name: 'OK' }).click();
    await expect(adminPage.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });

    await gotoRoute(page, '/notifications');
    const withheld = page.getByTestId('notification-withheld');
    await expect(withheld).toBeVisible({ timeout: 15_000 });
    await expect(withheld).toHaveText('The thing this is about is no longer available to you.');
    // Said in words rather than left blank, and still one line rather than none.
    await expect(page.getByText(caveName)).toHaveCount(0);
    await expect(page.getByRole('row').filter({ hasText: 'Someone shares something with me' })).toHaveCount(1);
  } finally {
    await adminContext.close();
  }
});
