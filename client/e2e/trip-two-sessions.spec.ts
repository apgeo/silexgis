// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { asPerson, localDay, signedInElsewhere, tripBody, tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

// Two people with the same trip open, each in a form of their own. The first to save wins; the
// second's save carries the version that person opened the form at, the server refuses it as
// stale, and the page says somebody else changed the trip — instead of writing the older form
// over the newer trip, which is what a save without its opening version would do in silence.
//
// The unit tests prove each form sends the version it opened with; only two real browsers against
// one server prove the whole loop: the version read, carried, refused, and worded.

/** Opens the trip's edit form on `page` and answers the title field in it. */
async function openEditForm(page: Page, tripId: string) {
  await gotoRoute(page, `/trip-logs/${tripId}`);
  await page.getByRole('button', { name: 'Edit trip log' }).click();
  const form = page.getByRole('dialog').filter({ hasText: 'Edit trip log' });
  const title = form.getByLabel('Title');
  await expect(title).not.toHaveValue('');
  return { form, title };
}

test('of two people editing the same trip, the second to save is told it changed since they opened it', async ({
  page,
  browser,
  consoleErrors,
}) => {
  await login(page);
  const stamp = Date.now();
  const original = `E2E Two sessions ${stamp}`;
  const trip = await asPerson<{ id: string }>(page, 'POST', '/api/v1/trip-logs', tripBody(original, localDay(0)));

  const other = await signedInElsewhere(browser);
  try {
    // Both forms are open on the same version before either is saved.
    const first = await openEditForm(page, trip.id);
    const second = await openEditForm(other.page, trip.id);

    // The first save is accepted: the positive half, on the same trip and the same server.
    await first.title.fill(`${original} first`);
    await first.form.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText('Saved.')).toBeVisible();
    await expect(first.form).toBeHidden();

    // The second is refused, worded as the conflict it is, and the form keeps what was typed.
    // The browser logs the refused request itself; declared by its address, so any other
    // refusal in this test still counts.
    consoleErrors.allow(
      new RegExp(`/api/v1/trip-logs/${trip.id}(\\?|:|$)`),
      'the second save is refused on purpose: it carries the version its form was opened at',
    );
    await second.title.fill(`${original} second`);
    await second.form.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(
      other.page.getByText('Someone else changed this since you loaded it. Reload and reapply your changes.'),
    ).toBeVisible();
    await expect(second.form).toBeVisible();
    await expect(second.title).toHaveValue(`${original} second`);
    await expect(other.page.getByText('The operation failed. Please try again.')).toHaveCount(0);

    // What the server holds is the first save, untouched by the refused one.
    const stored = await asPerson<{ title: string }>(page, 'GET', `/api/v1/trip-logs/${trip.id}`);
    expect(stored.title).toBe(`${original} first`);
  } finally {
    await other.context.close();
    await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
  }
});
