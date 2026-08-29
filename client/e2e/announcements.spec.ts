// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The first thing in this application somebody sends on purpose to many people, driven the way a
 * club officer does it: a club with one other person on its roster, a notice written for it, the
 * size of the audience read *before* anything is sent, a second deliberate act to send it, and
 * the member's own bell carrying it afterwards.
 *
 * Every step is done through the interface rather than against the API. A row inserted behind the
 * application's back would prove the modal renders and prove nothing about the half that can
 * silently stop working — whether the count shown is the count sent, and whether what the sender
 * confirmed is what a member actually receives.
 */
test('a club officer writes to the roster, sees who it reaches, confirms, and a member is told', async ({
  page,
  browser,
  request,
}) => {
  const stamp = Date.now();
  const officerEmail = `e2e-club-officer-${stamp}@dev.local`;
  const officerPassword = 'e2e-club-officer-pass-1';
  const officerName = `E2E Club Officer ${stamp}`;
  const memberEmail = `e2e-club-member-${stamp}@dev.local`;
  const memberPassword = 'e2e-club-member-pass-1';
  const memberName = `E2E Club Member ${stamp}`;
  const clubName = `E2E Announce Club ${stamp}`;
  const notice = `The Sunday meet moves to 09:00, run ${stamp}.`;
  // Two full sign-ins, a club created from nothing, a roster edit and a broadcast between them.
  test.slow();

  // Two accounts, both minted for this run. Somebody has to be on the roster other than the
  // sender, because an announcement is never sent back to the person who wrote it — and the
  // sender has to be new too: how often one account may write to a roster is bounded by a
  // durable marker nothing in the interface clears, so a run that reused a standing account
  // would pass once and be refused by that bound ever after. Self-registration is the only way
  // an installation mints an account without an operator, so a run that cannot do it says so as
  // a missing prerequisite rather than skipping — a skip reads as a non-failure, and this is the
  // only cover the composer has outside the component tests.
  const config = (await (await request.get('/api/v1/auth/config')).json()) as {
    openRegistration?: boolean;
  };
  expect(
    config.openRegistration,
    'this flow needs two accounts: start the API with SILEXGIS__Auth__OpenRegistration=true',
  ).toBe(true);
  for (const account of [
    { email: officerEmail, password: officerPassword, displayName: officerName },
    { email: memberEmail, password: memberPassword, displayName: memberName },
  ]) {
    const registered = await request.post('/api/v1/auth/register', { data: account });
    expect(registered.ok()).toBeTruthy();
  }

  // The officer works in a browser of their own. The watched page is the member's, because the
  // question this flow answers last is whether the notice reached somebody.
  const officerContext = await browser.newContext();
  const officer = await officerContext.newPage();

  try {
    // An ordinary account, holding nothing but what every account here holds. What lets it
    // announce at all is that it starts the club below: a club's starter rights give whoever
    // created it the right to write to its roster, and that is the only grant in this flow.
    await login(officer, officerEmail, officerPassword);
    await gotoRoute(officer, '/caving-groups');

    // A club of its own rather than one the sample data ships with: this test writes to
    // everybody on a roster, and a shared club would mean writing to whoever else is on it.
    await officer.getByRole('button', { name: 'New caving group' }).click();
    const create = officer.getByRole('dialog');
    await create.getByLabel('Name').fill(clubName);
    await create.getByRole('button', { name: 'OK' }).click();
    const row = officer.getByRole('row', { name: new RegExp(clubName) });
    await expect(row).toBeVisible({ timeout: 15_000 });

    // The member joins. The picker searches people, not accounts — an account gets its roster
    // entry when it registers, which is what makes the new sign-up findable here at all.
    await row.getByRole('button', { name: 'Manage' }).click();
    // Addressed as the drawer it is rather than by role: a modal opens over it later in this
    // flow and both would answer to the same role.
    const roster = officer.locator('.ant-drawer');
    await roster.locator('.ant-select').first().click();
    await officer.keyboard.type(memberName);
    const candidate = officer.locator('.ant-select-item-option').filter({ hasText: memberName });
    await expect(candidate).toBeVisible({ timeout: 15_000 });
    await candidate.click();
    // Waited for by its own round trip as well as by what it puts on screen: the roster shown in
    // the drawer is refetched when the edit lands, and watching only the screen cannot tell a
    // list that has not refreshed yet from one that never will.
    const joined = officer.waitForResponse(
      (r) => /\/api\/v1\/caving-groups\/[^/]+\/members$/.test(r.url())
        && r.request().method() === 'POST',
    );
    await roster.getByRole('button', { name: 'Add' }).click();
    await joined;
    await expect(roster.getByText(memberName)).toBeVisible({ timeout: 15_000 });
    // Closed by its own control and waited out. The drawer's mask covers the table underneath,
    // so a click on the row's next button while it is still there is swallowed rather than
    // refused — the test would wait for a button it can see and never reach.
    await roster.getByRole('button', { name: 'Close' }).click();
    await expect(roster).toBeHidden({ timeout: 15_000 });

    // Now the composer. The count comes first and is the claim worth driving in a browser: it
    // is one person, not the two on the roster, because the sender is never told their own
    // announcement — a page that showed the roster's size would show 2 here and be believed.
    await row.getByRole('button', { name: 'Announce' }).click();
    const composer = officer.getByRole('dialog').filter({ hasText: clubName });
    const audience = composer.getByTestId('announcement-audience');
    await expect(audience).toBeVisible({ timeout: 15_000 });
    await expect(audience).toContainText('People this reaches: 1');

    await composer.getByLabel('Announcement').fill(notice);

    // Two acts, not one. Continuing only repeats the wording back with the number beside it;
    // nothing has been written until Send, which is the point of the step existing.
    await composer.getByRole('button', { name: 'Continue' }).click();
    await expect(composer.getByText('This goes now to the people counted above.')).toBeVisible();
    await expect(composer.getByText(notice)).toBeVisible();

    await composer.getByRole('button', { name: 'Send' }).click();
    const result = composer.getByTestId('announcement-result');
    await expect(result).toBeVisible({ timeout: 15_000 });
    // What happened, in the same number that was promised beforehand.
    await expect(result).toContainText('Sent. People told: 1');
    // The footer's button, not the modal's own corner cross: both answer to the same name.
    await composer.locator('.ant-modal-footer').getByRole('button', { name: 'Close' }).click();

    // And the member, who chose nothing and did nothing, has it. A fresh sign-in, so the
    // header's count is asked for once on the way in rather than waited for on its timer.
    await login(page, memberEmail, memberPassword);
    const bell = page.getByRole('button', { name: 'Notifications' });
    const count = page.locator('.ant-badge').filter({ has: bell }).locator('.ant-badge-count');
    // Something arrived, rather than exactly one thing: joining a club is itself news, so the
    // roster edit that put this member in the club has already told them once, and that notice is
    // not what this flow is about. Which of them is the announcement is settled below, by reading
    // it.
    await expect(count).toBeVisible({ timeout: 15_000 });

    await bell.click();
    await page.waitForURL((url) => url.pathname === '/notifications', { timeout: 30_000 });
    // The words the officer typed, not a summary of them: an announcement is somebody's own
    // sentence and the whole point is that it arrives as written.
    await expect(
      page.getByRole('row', { name: new RegExp(`run ${stamp}`) }),
    ).toBeVisible({ timeout: 15_000 });
  } finally {
    await officerContext.close();
  }
});
