// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import {
  asPerson,
  localDay,
  purposeWithChecklist,
  registerAccount,
  removePurpose,
  signedInElsewhere,
  tripBody,
  tryAsPerson,
  type Account,
  type PlannedPurpose,
} from './arrange.ts';
import { chooseOption, gotoRoute, login } from './helpers.ts';

/**
 * What a person is going on, read the way they read it: on the board they land on, and on the page
 * of their own that the board points to.
 *
 * Whose trips these are is never asked for — the server works "mine" out from whoever is asking —
 * so the flows here are about somebody other than the administrator, whose diary fills with
 * whatever every other flow writes and could never be shown empty. A person is minted for each
 * run, and everything that puts a trip in front of them is done by the administrator in a browser
 * of their own: being asked on a trip and being written onto its party are both things that happen
 * *to* a person, and the watched page is that person's.
 *
 * The administrator's side is written through the API rather than driven. It is the arrangement,
 * not the subject: the trip form is driven by the trip spec, and asking somebody by the calendar
 * spec through the panel trips and events share. The one form that belongs to this subject —
 * answering an invitation — is driven below, by the person it was sent to.
 */

interface Caver {
  id: string;
  name: string;
  userId: string | null;
}

interface Arranged {
  sooner: { id: string; title: string };
  later: { id: string; title: string };
  caverId: string;
  purpose: PlannedPurpose;
}

/** The person's own entry in the club's records, found the way the invitation picker finds it. */
async function caverOf(admin: Page, account: Account): Promise<Caver> {
  const found = await asPerson<Caver[]>(
    admin,
    'GET',
    `/api/v1/cavers?search=${encodeURIComponent(account.displayName)}`,
  );
  const caver = found.find((row) => row.name === account.displayName);
  expect(caver, 'a registered account gets an entry in the club records').toBeTruthy();
  return caver!;
}

/**
 * Two trips for one person, arranged the two ways a trip reaches somebody's diary.
 *
 * The one further off was written first, so a list that came back in the order rows were written
 * would show it first — the order this page promises is the order of the days, and only a pair
 * written the other way round can tell the two apart. The further one is asked about rather than
 * written onto; the nearer one has the person on its party. And the further one is for a purpose
 * that names a checklist, so it carries a plan for its row to report on.
 */
async function arrange(admin: Page, member: Account, stamp: number): Promise<Arranged> {
  const caver = await caverOf(admin, member);

  // Everybody signed in may read the plan: a reading of a list somebody may not open is no
  // reading at all, and that refusal is what the checklist spec is for.
  const purpose = await purposeWithChecklist(admin, stamp);

  const laterTitle = `E2E Later Trip ${stamp}`;
  const later = await asPerson<{ id: string }>(
    admin,
    'POST',
    '/api/v1/trip-logs',
    tripBody(laterTitle, localDay(9), { tripTypeId: purpose.tripTypeId }),
  );
  await asPerson(admin, 'POST', `/api/v1/trip-logs/${later.id}/invitations`, {
    caverId: caver.id,
  });

  const soonerTitle = `E2E Sooner Trip ${stamp}`;
  const sooner = await asPerson<{ id: string }>(
    admin,
    'POST',
    '/api/v1/trip-logs',
    tripBody(soonerTitle, localDay(4), {
      participants: [
        {
          caverId: caver.id,
          newCaverName: null,
          roleId: null,
          entryTime: null,
          exitTime: null,
          note: null,
        },
      ],
    }),
  );

  return {
    sooner: { id: sooner.id, title: soonerTitle },
    later: { id: later.id, title: laterTitle },
    caverId: caver.id,
    purpose,
  };
}

/** The rows of the page's table, in the order it draws them. */
function tableRows(page: Page): Locator {
  return page.locator('.ant-table-tbody tr.ant-table-row');
}

/** The board's "coming up" card, by its own title. */
function comingUpCard(page: Page): Locator {
  return page.locator('.ant-card').filter({
    has: page.locator('.ant-card-head-title', { hasText: /^Coming up$/ }),
  });
}

test('a person on no trip is told what would put one on their board and on their own list', async ({
  page,
  request,
}) => {
  const member = await registerAccount(request, 'diary');
  await login(page, member.email, member.password);

  // The board first, because it is where somebody lands. An account on nothing is the ordinary case
  // for somebody who has just joined, so the words say what would put a trip here rather than
  // reporting an absence as if something had gone missing.
  await gotoRoute(page, '/dashboard');
  const card = comingUpCard(page);
  await expect(card.getByText(/^Nothing coming up\. A trip appears here once/)).toBeVisible({
    timeout: 30_000,
  });

  // The card says where the whole list is, and the list is a page of its own.
  await card.getByRole('button', { name: 'All my trips' }).click();
  await page.waitForURL((url) => url.pathname === '/trip-logs/mine', { timeout: 30_000 });
  await expect(page.getByRole('heading', { name: 'My trips' })).toBeVisible();
  await expect(page.getByTestId('my-trips-empty')).toContainText(
    'You are not on any trip yet. A trip appears here once somebody invites you to one',
  );

  // Narrowing an empty diary says the narrowing found nothing — not that the diary is empty, which
  // is a different claim and would be the wrong one the moment there is a trip in it.
  await chooseOption(page, page.getByTestId('my-trips-state-filter'), 'Cancelled');
  await expect(page.getByTestId('my-trips-empty')).toContainText(
    'No trip of yours matches these filters.',
  );
});

test('a trip somebody is asked on or put on reaches their board and their list, soonest first, and their own no takes it off', async ({
  page,
  browser,
  request,
}) => {
  // Two sign-ins, a purpose and a plan arranged between them, and a round trip through a trip page.
  test.slow();
  const stamp = Date.now();
  const member = await registerAccount(request, 'traveller');
  const admin = await signedInElsewhere(browser);
  let arranged: Arranged | undefined;

  try {
    arranged = await arrange(admin.page, member, stamp);
    const { sooner, later } = arranged;

    await login(page, member.email, member.password);

    // The board's panel: the next things this person is going on, in the order they happen.
    await gotoRoute(page, '/dashboard');
    const card = comingUpCard(page);
    const items = card.locator('.silex-list-item');
    await expect(items).toHaveCount(2, { timeout: 30_000 });
    await expect(items.nth(0)).toContainText(sooner.title);
    await expect(items.nth(1)).toContainText(later.title);
    // The state rides on the row, so a trip that has been called off cannot read as an ordinary
    // outing. Neither of these has been announced yet, and the row says that too.
    await expect(items.nth(0)).toContainText('Draft');

    // The page the panel points to holds the same two, in the same order — the order of the days,
    // which is the opposite of the order they were written in.
    await card.getByRole('button', { name: 'All my trips' }).click();
    await page.waitForURL((url) => url.pathname === '/trip-logs/mine', { timeout: 30_000 });
    const rows = tableRows(page);
    await expect(rows).toHaveCount(2, { timeout: 30_000 });
    await expect(rows.nth(0)).toContainText(sooner.title);
    await expect(rows.nth(1)).toContainText(later.title);

    // The plan the further trip carries, as one reading on its row: nothing of it is settled yet.
    // The nearer trip's purpose names no list, and its row draws no reading rather than a zero —
    // a zero would say there is a list.
    await expect(rows.nth(1).getByTestId('trip-readiness')).toHaveText('0 of 2 settled');
    await expect(rows.nth(0).getByTestId('trip-readiness')).toHaveCount(0);

    // Being put on the party and being asked both count, and each row counts the people the trip
    // names — one on the nearer trip, nobody yet on the further, where the person is only asked.
    await expect(rows.nth(0).locator('td').last()).toHaveText('1');
    await expect(rows.nth(1).locator('td').last()).toHaveText('0');

    // A window that holds only the further day narrows the list to it.
    const fillDay = async (placeholder: string, value: string) => {
      // The test id is carried by both of the range's fields rather than by a wrapper, so the
      // placeholder is what tells the two apart.
      const field = page.getByTestId('my-trips-date-filter').and(page.getByPlaceholder(placeholder));
      await field.click();
      await field.fill(value);
      await page.keyboard.press('Enter');
      await expect(field).toHaveValue(value);
    };
    await fillDay('Start date', localDay(8));
    await fillDay('End date', localDay(10));
    await expect(rows).toHaveCount(1, { timeout: 15_000 });
    await expect(rows.nth(0)).toContainText(later.title);

    // A row opens the trip it is.
    await rows.nth(0).click();
    await page.waitForURL((url) => url.pathname === `/trip-logs/${later.id}`, { timeout: 30_000 });
    await expect(page.getByRole('heading', { name: later.title })).toBeVisible({ timeout: 15_000 });

    // The person answers for themselves, on the trip's own list of who is coming.
    await page.getByRole('tab', { name: 'Who is coming', exact: true }).click();
    const ownRow = page.getByTestId(`trip-invitation-${arranged.caverId}`);
    await expect(ownRow).toBeVisible({ timeout: 15_000 });
    await expect(ownRow.getByText('Not answered').first()).toBeVisible();
    await chooseOption(
      page,
      page.getByTestId(`trip-invitation-answer-${arranged.caverId}`),
      'Not coming',
    );
    const answered = page.waitForResponse(
      (response) =>
        response.request().method() === 'PUT' &&
        response.url().includes(`/invitations/${arranged!.caverId}/response`),
    );
    await page.getByTestId(`trip-invitation-save-${arranged.caverId}`).click();
    expect((await answered).status()).toBe(200);
    // The tag beside the name follows the answer, not the draft in the control.
    await expect(ownRow.locator('.ant-tag').filter({ hasText: /^Not coming$/ })).toBeVisible({
      timeout: 15_000,
    });

    // Saying no takes the trip off the diary: a list that kept the weekends somebody turned down
    // would push the one they are going on off its end. The trip they are on stays.
    await gotoRoute(page, '/trip-logs/mine');
    await expect(rows).toHaveCount(1, { timeout: 30_000 });
    await expect(rows.nth(0)).toContainText(sooner.title);
  } finally {
    if (arranged) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/trip-logs/${arranged.sooner.id}`);
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/trip-logs/${arranged.later.id}`);
      await removePurpose(admin.page, arranged.purpose);
    }
    await admin.context.close();
  }
});
