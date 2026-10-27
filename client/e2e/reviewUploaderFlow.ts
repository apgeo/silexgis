// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Browser, type Page } from '@playwright/test';
import { editorOfItsOwn, signedInElsewhere } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';
import { lineNamed, markForReview, newCave, newEntrance, openBatchOf, phoneOf, rootCave, statusOf } from './reviewArrange.ts';

/**
 * What an uploader under review sees of their own uploads, and what a reviewer does to produce it.
 *
 * Shared by the desktop spec and the phone spec: the reviewer's page is the one passed in, so the
 * same walk runs in whichever viewport the project gives it, while the uploader reads their own
 * list in a browser of their own.
 */

/** Looks for a page that grew sideways: on a phone, the sign that something was drawn too wide. */
async function expectNoSidewaysScroll(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow, 'the page is wider than the screen').toBeLessThanOrEqual(1);
}

export async function uploaderSeesEachDecision(page: Page, browser: Browser, { phone }: { phone: boolean }) {
  const stamp = `${Date.now()}`;
  const editor = await editorOfItsOwn(browser, page.request, 'review-mine');
  const uploader = await signedInElsewhere(browser, editor.account);
  try {
    const root = await rootCave(editor.admin, `E2E Mine Root ${stamp}`);
    await markForReview(editor.admin, editor.account);
    const device = await phoneOf(uploader.page, root.id, stamp);

    // One upload turned down whole, with a note.
    const turnedName = `E2E Mine Turned ${stamp}`;
    const turned = crypto.randomUUID();
    await device.upload([newCave(turned, turnedName)]);

    // The uploader sees it waiting, and nothing of it written.
    await gotoRoute(uploader.page, '/geodata?tab=batches');
    const mine = uploader.page.getByTestId('review-mine-row');
    await expect(mine).toHaveCount(1, { timeout: 30_000 });
    await expect(mine.getByTestId('review-mine-state')).toHaveText('Waiting');
    expect(await statusOf(uploader.page, `/api/v1/features/${turned}`), 'a held creation is not written').toBe(404);

    await login(page);
    await openBatchOf(page, editor.account);
    if (phone) {
      await expectNoSidewaysScroll(page);
    }
    await page.getByTestId('review-note').fill('Not this one');
    await page.getByTestId('review-reject-all').click();
    await page.locator('.ant-modal-confirm').getByRole('button', { name: 'Reject all', exact: true }).click();
    await expect(page.getByTestId('review-batch-state')).toHaveText('Rejected', { timeout: 15_000 });

    // The uploader reads the decision and the reviewer's note in their own list.
    await gotoRoute(uploader.page, '/geodata?tab=batches');
    await expect(mine).toHaveCount(1, { timeout: 30_000 });
    await expect(mine.getByTestId('review-mine-state')).toHaveText('Rejected');
    await expect(mine.getByTestId('review-mine-note')).toHaveText('Not this one');

    // Opening it shows what was turned down, and gives the uploader nothing to decide with.
    await mine.click();
    await expect(uploader.page.getByTestId('review-batch')).toBeVisible();
    await expect(uploader.page.getByTestId('review-own')).toBeVisible();
    await expect(uploader.page.getByTestId('review-controls')).toHaveCount(0);
    await expect(lineNamed(uploader.page, turnedName)).toBeVisible();

    // A second upload, a cave with its entrance, accepted whole in one press.
    const keptCave = crypto.randomUUID();
    const keptEntrance = crypto.randomUUID();
    await device.upload([newCave(keptCave, `E2E Mine Kept ${stamp}`), newEntrance(keptEntrance, keptCave, `Gura ${stamp}`)]);
    await openBatchOf(page, editor.account);
    await page.getByTestId('review-accept-all').click();
    await expect(page.getByTestId('review-batch-state')).toHaveText('Accepted', { timeout: 15_000 });
    await expect.poll(() => statusOf(page, `/api/v1/features/${keptCave}`)).toBe(200);
    await expect.poll(() => statusOf(page, `/api/v1/features/${keptEntrance}`)).toBe(200);
    expect(await statusOf(page, `/api/v1/features/${turned}`), 'the turned-down creation is still not written').toBe(404);

    // Both uploads are listed, newest first, each in the state it ended in.
    await gotoRoute(uploader.page, '/geodata?tab=batches');
    await expect(mine).toHaveCount(2, { timeout: 30_000 });
    await expect(mine.first().getByTestId('review-mine-state')).toHaveText('Accepted');
    await expect(mine.last().getByTestId('review-mine-state')).toHaveText('Rejected');
  } finally {
    await uploader.context.close();
    await editor.release();
  }
}
