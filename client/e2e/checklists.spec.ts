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
  type PlannedPurpose,
} from './arrange.ts';
import { chooseOption, gotoRoute, login } from './helpers.ts';

/**
 * The lists a party works through before it sets off: written on a page of their own, ticked on
 * the trip that works through one, and read by nobody the list's own audience does not admit.
 *
 * Every list here is written by the run that reads it and carries the run's stamp, so nothing
 * depends on what the demonstration data holds and a list left by an aborted run is never the one
 * a later run finds.
 */

/** A list's card on the page, by its title. */
function listCard(page: Page, title: string): Locator {
  return page.locator('.ant-card').filter({ hasText: title });
}

/** The checklist dialog, by the heading it opens with. */
function listDialog(page: Page, heading: string): Locator {
  return page.getByRole('dialog').filter({ hasText: heading });
}

/**
 * Picks an option of one of the dialog's select controls by its words.
 *
 * Addressed through the form item's own label, so the two selects in the dialog are told apart by
 * what they ask rather than by position.
 */
async function choose(page: Page, dialog: Locator, label: string, option: string) {
  await chooseOption(
    page,
    dialog.locator('.ant-form-item').filter({ hasText: label }).locator('.ant-select'),
    option,
  );
}

/** Saves the dialog and waits for the list to be written, and the dialog to have gone. */
async function saveList(page: Page, dialog: Locator, method: 'POST' | 'PUT') {
  const written = page.waitForResponse(
    (response) =>
      response.request().method() === method && /\/api\/v1\/checklists(\/|$)/.test(response.url()),
  );
  await dialog.getByRole('button', { name: 'OK' }).click();
  expect((await written).ok()).toBeTruthy();
  await expect(dialog).toBeHidden({ timeout: 15_000 });
}

test('a list is written with its lines, reworded and lengthened, and deleted', async ({ page }) => {
  const title = `E2E Checklist ${Date.now()}`;
  await login(page);
  await gotoRoute(page, '/checklists');
  await expect(page.getByRole('heading', { name: 'Checklists' })).toBeVisible();

  await page.getByRole('button', { name: /New checklist$/ }).click();
  const create = listDialog(page, 'New checklist');
  await expect(create).toBeVisible();
  await create.getByLabel('Title').fill(title);
  await create.getByLabel('Description').fill('Before any vertical trip');
  // The widest audience short of the public is how a list is shared with everybody signed in.
  await choose(page, create, 'Who may read it', 'Anyone signed in');
  // The form opens with one empty line, because a list with none is not a list anybody works
  // through; a second is added beside it.
  const lines = create.getByPlaceholder('Something to settle before setting off');
  await expect(lines).toHaveCount(1);
  await lines.nth(0).fill('Rope inspected');
  await create.getByRole('button', { name: /Add a line$/ }).click();
  await expect(lines).toHaveCount(2);
  await lines.nth(1).fill('Callout left with somebody');
  await saveList(page, create, 'POST');

  const card = listCard(page, title);
  await expect(card).toBeVisible({ timeout: 15_000 });
  await expect(card).toContainText('Before any vertical trip');
  await expect(card).toContainText('Anyone signed in');
  await expect(card).toContainText('2 lines');

  // Handing the list to one person by name is the lock on its card, which opens the same
  // permissions dialog a trip has — on the list, as a target the sharing route resolves: the
  // dialog's own read of the rules answering is what says the target is one the server knows.
  const rulesRead = page.waitForResponse(
    (response) =>
      response.request().method() === 'GET' &&
      /\/api\/v1\/objects\/checklist\/[^/]+\/access$/.test(response.url()),
  );
  await card.getByRole('button', { name: 'Permissions' }).click();
  const permissions = page.getByRole('dialog').filter({ hasText: 'Permissions' });
  await expect(permissions).toBeVisible();
  expect((await rulesRead).ok()).toBeTruthy();
  await permissions.getByRole('button', { name: 'Cancel' }).click();
  await expect(permissions).toBeHidden();

  // Reworded and lengthened. The lines come back into the form as they were written, in order.
  await card.getByRole('button', { name: 'Edit' }).click();
  const edit = listDialog(page, 'Edit checklist');
  await expect(edit).toBeVisible();
  await expect(edit.getByLabel('Title')).toHaveValue(title);
  const editLines = edit.getByPlaceholder('Something to settle before setting off');
  await expect(editLines).toHaveCount(2);
  await expect(editLines.nth(0)).toHaveValue('Rope inspected');
  await expect(editLines.nth(1)).toHaveValue('Callout left with somebody');
  await editLines.nth(0).fill('Rope inspected and bagged');
  await edit.getByRole('button', { name: /Add a line$/ }).click();
  await editLines.nth(2).fill('Spare light packed');
  await saveList(page, edit, 'PUT');
  await expect(card).toContainText('3 lines');

  // The rewording is stored, not held by the page.
  await page.reload();
  await page.waitForURL((url) => url.pathname === '/checklists', { timeout: 60_000 });
  await expect(listCard(page, title)).toContainText('3 lines', { timeout: 30_000 });
  await listCard(page, title).getByRole('button', { name: 'Edit' }).click();
  await expect(
    listDialog(page, 'Edit checklist').getByPlaceholder('Something to settle before setting off').nth(0),
  ).toHaveValue('Rope inspected and bagged');
  await listDialog(page, 'Edit checklist').getByRole('button', { name: 'Cancel' }).click();

  // Deleting asks first, because trips lose what was confirmed against its lines.
  const removed = page.waitForResponse(
    (response) =>
      response.request().method() === 'DELETE' && /\/api\/v1\/checklists\//.test(response.url()),
  );
  await listCard(page, title).getByRole('button', { name: 'Delete this checklist' }).click();
  await expect(page.getByText('Delete this checklist? Trips lose the confirmations')).toBeVisible();
  await page.getByRole('tooltip').getByRole('button', { name: 'OK' }).click();
  expect((await removed).ok()).toBeTruthy();
  await expect(listCard(page, title)).toHaveCount(0, { timeout: 15_000 });
});

/**
 * Ticked on the trip, and the tick belongs to the line rather than to its words.
 *
 * The list itself is arranged through the API — writing one is the flow above — and the trip with
 * it, because what is driven here is the trip's side: working the list through, the reading that
 * follows, and what a rewording on the list's own page does to what a party already confirmed.
 */
test('a trip works its list through, and a line reworded on the list keeps what was confirmed against it', async ({
  page,
}) => {
  const stamp = Date.now();
  const title = `E2E Planned Trip ${stamp}`;
  await login(page);

  let purpose: PlannedPurpose | undefined;
  let tripId: string | undefined;
  try {
    purpose = await purposeWithChecklist(page, stamp, {
      lines: ['Rope checked', 'Callout set'],
    });
    const trip = await asPerson<{ id: string }>(
      page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(title, localDay(6), { tripTypeId: purpose.tripTypeId }),
    );
    tripId = trip.id;

    await gotoRoute(page, `/trip-logs/${tripId}`);
    await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 30_000 });
    await page.getByRole('tab', { name: 'Checklist', exact: true }).click();

    const panel = page.getByRole('tabpanel');
    await expect(panel.getByText(purpose.checklistTitle)).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByText('0 of 2 settled')).toBeVisible();
    // Said in words, because a bar that fills up reads like a gate whatever the code does.
    await expect(panel.getByText('This is a reading for the party, not a rule.')).toBeVisible();

    const tick = async (line: string, ticked: boolean) => {
      const written = page.waitForResponse(
        (response) =>
          response.request().method() === (ticked ? 'PUT' : 'DELETE') &&
          /\/checklist\/items\//.test(response.url()),
      );
      await panel.getByRole('checkbox', { name: line, exact: true }).click();
      expect((await written).ok()).toBeTruthy();
    };

    await tick('Rope checked', true);
    await expect(panel.getByText('1 of 2 settled')).toBeVisible({ timeout: 15_000 });
    await expect(panel.getByRole('checkbox', { name: 'Rope checked', exact: true })).toBeChecked();
    // When it was confirmed is kept with the tick and said under the line.
    await expect(panel.getByText(/^Confirmed /)).toHaveCount(1);

    // A tick is a stored fact about the trip, not a state of the page: the tab is in the address,
    // so the reload lands back on it.
    await page.reload();
    await page.waitForURL((url) => url.pathname === `/trip-logs/${tripId}`, { timeout: 60_000 });
    await expect(page.getByRole('tabpanel').getByText('1 of 2 settled')).toBeVisible({
      timeout: 30_000,
    });

    // Reworded on the list's own page. The line keeps its identity through the edit, so the party's
    // confirmation stays with it — a line rewritten the morning of the trip is the same line.
    await gotoRoute(page, '/checklists');
    const card = listCard(page, purpose.checklistTitle);
    await card.getByRole('button', { name: 'Edit' }).click();
    const edit = listDialog(page, 'Edit checklist');
    const lines = edit.getByPlaceholder('Something to settle before setting off');
    await expect(lines.nth(0)).toHaveValue('Rope checked');
    await lines.nth(0).fill('Rope checked and bagged');
    await saveList(page, edit, 'PUT');

    await gotoRoute(page, `/trip-logs/${tripId}`);
    await page.getByRole('tab', { name: 'Checklist', exact: true }).click();
    const reread = page.getByRole('tabpanel');
    await expect(
      reread.getByRole('checkbox', { name: 'Rope checked and bagged', exact: true }),
    ).toBeChecked({ timeout: 15_000 });
    await expect(reread.getByText('1 of 2 settled')).toBeVisible();

    // Settled in full, and the reading says so; then taken back, and it says that too.
    await tick('Callout set', true);
    await expect(reread.getByText('2 of 2 settled')).toBeVisible({ timeout: 15_000 });
    await tick('Callout set', false);
    await expect(reread.getByText('1 of 2 settled')).toBeVisible({ timeout: 15_000 });
  } finally {
    if (tripId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    if (purpose) {
      await removePurpose(page, purpose);
    }
  }
});

/**
 * A list kept to its author is the author's, and the refusal is drawn the way the server draws it:
 * the list is not offered on the page, and a trip working through it says it works through no list
 * this reader can see — the same words it says for a trip whose purpose names none, so that the
 * page cannot tell somebody that a list exists which nobody meant to tell them about.
 */
test('a list kept to its author is not shown to anybody else, on the lists or on a trip that works through it', async ({
  page,
  browser,
  request,
}) => {
  test.slow();
  const stamp = Date.now();
  const title = `E2E Shared Trip ${stamp}`;
  const ownList = `E2E Own List ${stamp}`;
  const reader = await registerAccount(request, 'listreader');
  const admin = await signedInElsewhere(browser);

  let purpose: PlannedPurpose | undefined;
  let tripId: string | undefined;
  try {
    purpose = await purposeWithChecklist(admin.page, stamp, { visibility: 'private' });
    const trip = await asPerson<{ id: string }>(
      admin.page,
      'POST',
      '/api/v1/trip-logs',
      tripBody(title, localDay(5), { tripTypeId: purpose.tripTypeId }),
    );
    tripId = trip.id;

    await login(page, reader.email, reader.password);
    await gotoRoute(page, '/checklists');

    // Anybody may write a list of their own, so the reader writes one: it is what proves the page
    // has its answer, and therefore that the administrator's list is missing from it rather than
    // merely not arrived yet.
    await page.getByRole('button', { name: /New checklist$/ }).click();
    const create = listDialog(page, 'New checklist');
    await create.getByLabel('Title').fill(ownList);
    await create.getByPlaceholder('Something to settle before setting off').fill('Helmet');
    await saveList(page, create, 'POST');
    await expect(listCard(page, ownList)).toBeVisible({ timeout: 15_000 });
    // The form opens on the narrowest audience, so a list nobody chose to share stays its author's.
    await expect(listCard(page, ownList)).toContainText('Only me');
    await expect(listCard(page, purpose.checklistTitle)).toHaveCount(0);

    // The trip is readable by everybody signed in; its plan is not.
    await gotoRoute(page, `/trip-logs/${tripId}`);
    await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 30_000 });
    await page.getByRole('tab', { name: 'Checklist', exact: true }).click();
    await expect(
      page.getByRole('tabpanel').getByText('This trip works through no checklist you can see.'),
    ).toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('tabpanel').getByText(purpose.checklistTitle)).toHaveCount(0);
    await expect(page.getByRole('tabpanel').getByRole('checkbox')).toHaveCount(0);

    // The reader's own list is theirs to remove.
    await gotoRoute(page, '/checklists');
    await listCard(page, ownList).getByRole('button', { name: 'Delete this checklist' }).click();
    await page.getByRole('tooltip').getByRole('button', { name: 'OK' }).click();
    await expect(listCard(page, ownList)).toHaveCount(0, { timeout: 15_000 });
  } finally {
    if (tripId) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    if (purpose) {
      await removePurpose(admin.page, purpose);
    }
    await admin.context.close();
  }
});
