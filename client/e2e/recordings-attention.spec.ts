// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { arrangeRecordings } from './recordingsArrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * Phone recordings on the cave page, and the notice on a trip one was made into.
 *
 * Both recordings are arranged through the sync route a phone itself uses, so the rows are the
 * ones a real upload writes; the conversion of the closed one is the installation's own job. What
 * is driven is the part a person does: seeing where each recording stands, opening the review of a
 * waiting one, and reading and clearing the notice on a trip that needs looking at.
 */
test('a waiting recording opens its review from the cave page and a flagged trip is marked resolved', async ({ page }) => {
  test.slow();

  const stamp = `${Date.now()}`;

  await login(page);
  const { cave, waiting, waitingTitle, flaggedTitle } = await arrangeRecordings(page, stamp);

  await gotoRoute(page, `/caves/${cave.id}`);
  const card = page.getByTestId('cave-recordings');
  await expect(card.getByText(waitingTitle)).toBeVisible({ timeout: 60_000 });
  const waitingRow = card.locator('tr', { hasText: waitingTitle });
  await expect(waitingRow.getByTestId('recording-standing-waiting')).toBeVisible();
  await expect(waitingRow.getByText('Still open on the phone')).toBeVisible();

  // The closed recording is made a trip by the installation's job and flagged; the page is read
  // again until it says so.
  const flaggedRow = card.locator('tr', { hasText: flaggedTitle });
  await expect
    .poll(
      async () => {
        await page.reload();
        // The list is drawn after its request returns; counting before that would read zero.
        await expect(card.getByText(waitingTitle)).toBeVisible({ timeout: 30_000 });
        return flaggedRow.getByTestId('recording-standing-needsAttention').count();
      },
      { timeout: 90_000, message: 'the closed recording was never made into a flagged trip' },
    )
    .toBe(1);

  // By hand: the waiting recording opens the review of that recording, named and not chosen from
  // an archive, with its scans listed and nothing confirmable until a trip and a survey are chosen.
  await waitingRow.getByRole('button', { name: 'Convert to a trip' }).click();
  await expect(page).toHaveURL(new RegExp(`/trip-logs/speleoloc-import/recording/${waiting}$`));
  await expect(page.getByTestId('speleoloc-import-recording-locked')).toContainText(waitingTitle, {
    timeout: 30_000,
  });
  await expect(page.getByTestId('speleoloc-import-recording')).toHaveCount(0);
  await expect(page.getByTestId('speleoloc-import-commit')).toBeDisabled();
  await expect(page.getByTestId('speleoloc-import-preview-error')).toHaveCount(0);

  // The flagged trip says why, in words, and is marked as resolved by somebody who may change it.
  await gotoRoute(page, `/caves/${cave.id}`);
  await page.getByTestId('cave-recordings').locator('tr', { hasText: flaggedTitle }).getByText('Open the trip').click();
  const notice = page.getByTestId('trip-attention-raised');
  await expect(notice).toBeVisible({ timeout: 30_000 });
  await expect(notice.getByTestId('trip-attention-unresolved_station')).toContainText('does not give one single survey station');
  await expect(notice.getByTestId('trip-attention-unmapped_person')).toContainText('Phone users page');

  await notice.getByTestId('trip-attention-clear').click();
  await expect(page.getByTestId('trip-attention-cleared')).toBeVisible();
  await expect(page.getByTestId('trip-attention-clear')).toHaveCount(0);

  // The server's answer, not the page's: still cleared after a reload, with the reasons kept, and
  // the cave page now says the recording was made into a trip.
  await page.reload();
  await expect(page.getByTestId('trip-attention-cleared')).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('trip-attention-cleared').getByTestId('trip-attention-unresolved_station')).toBeVisible();
  await gotoRoute(page, `/caves/${cave.id}`);
  await expect(
    page.getByTestId('cave-recordings').locator('tr', { hasText: flaggedTitle }).getByTestId('recording-standing-converted'),
  ).toBeVisible({ timeout: 30_000 });
});
