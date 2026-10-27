// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { arrangeRecordings } from './recordingsArrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The phone binding of the recordings flow: on a phone profile — touch, coarse pointer, 412 CSS
 * px — the cave's recordings can be read, a waiting one opens its review, and the notice on a
 * flagged trip can be cleared with a tap. Runs under the phone project; the recordings are
 * arranged exactly as the desktop spec arranges them.
 */
test('recordings on the cave page and the attention notice work by finger', async ({ page }) => {
  test.setTimeout(240_000);

  await login(page);
  const { cave, waiting, waitingTitle, flaggedTitle } = await arrangeRecordings(page, `${Date.now()}`);

  await gotoRoute(page, `/caves/${cave.id}`);
  const card = page.getByTestId('cave-recordings');
  const flaggedRow = card.locator('tr', { hasText: flaggedTitle });
  await expect
    .poll(
      async () => {
        await page.reload();
        await expect(card.getByText(waitingTitle)).toBeVisible({ timeout: 30_000 });
        return flaggedRow.getByTestId('recording-standing-needsAttention').count();
      },
      { timeout: 90_000, message: 'the closed recording was never made into a flagged trip' },
    )
    .toBe(1);

  await card.locator('tr', { hasText: waitingTitle }).getByRole('button', { name: 'Convert to a trip' }).tap();
  await expect(page).toHaveURL(new RegExp(`/trip-logs/speleoloc-import/recording/${waiting}$`));
  await expect(page.getByTestId('speleoloc-import-recording-locked')).toContainText(waitingTitle, {
    timeout: 30_000,
  });

  await gotoRoute(page, `/caves/${cave.id}`);
  await page.getByTestId('cave-recordings').locator('tr', { hasText: flaggedTitle }).getByText('Open the trip').tap();
  const notice = page.getByTestId('trip-attention-raised');
  await expect(notice).toBeVisible({ timeout: 30_000 });
  await notice.getByTestId('trip-attention-clear').tap();
  await expect(page.getByTestId('trip-attention-cleared')).toBeVisible();
});
