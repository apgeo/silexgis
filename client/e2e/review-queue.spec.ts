// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, editorOfItsOwn, signedInElsewhere } from './arrange.ts';
import { login } from './helpers.ts';
import { lineNamed, markForReview, newCave, newEntrance, openBatchOf, phoneOf, statusOf } from './reviewArrange.ts';

/**
 * Deciding what an account under review uploaded.
 *
 * The uploads are arranged through the sync route a phone itself uses, by an account an
 * administrator has marked for review, so the held batches and their lines are the ones a real
 * phone writes. What is driven is what a reviewer does: find the batch in the queue, read what it
 * proposes, and accept some of it, one field of it, or none.
 */

interface Cave {
  id: string;
  name: string;
  description: string | null;
}

test('a reviewer accepts one field of a held update and leaves the rest', async ({ page, browser }) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const editor = await editorOfItsOwn(browser, page.request, 'review-field');
  const phone = await signedInElsewhere(browser, editor.account);
  try {
    const before = { name: `E2E Review Cave ${stamp}`, description: `Before ${stamp}` };
    const types = await asPerson<{ id: number }[]>(editor.admin, 'GET', '/api/v1/cave-types');
    const cave = await asPerson<Cave>(editor.admin, 'POST', '/api/v1/caves', {
      ...before,
      caveTypeId: types[0].id,
      visibility: 'authenticated',
      locationProtected: false,
      explorationStatus: 'unknown',
      isShowCave: false,
    });
    await markForReview(editor.admin, editor.account);

    const device = await phoneOf(phone.page, cave.id, stamp);
    const held = (await device.download()).features.find((f) => f.id === cave.id);
    expect(held, 'the set hands the phone the cave it is rooted at').toBeDefined();
    const proposed = { name: `E2E Review Cave renamed ${stamp}`, description: `From the phone ${stamp}` };
    await device.upload([
      {
        id: cave.id,
        kind: 'cave',
        baseRevision: held!.updatedAt,
        deleted: false,
        name: proposed.name,
        description: proposed.description,
        isMain: false,
      },
    ]);

    await login(page);
    await openBatchOf(page, editor.account);
    const line = lineNamed(page, proposed.name);
    await expect(line).toBeVisible();
    await line.getByTestId('review-line-toggle').click();

    // Both fields differ from the cave now, and both start ticked; leaving the name unticked
    // accepts the description alone.
    const diff = line.getByTestId('review-field-diff');
    await expect(diff).toContainText(before.name);
    await expect(diff).toContainText(proposed.name);
    await expect(diff).toContainText(before.description);
    await expect(diff.getByTestId('review-field-name')).toBeChecked();
    await expect(diff.getByTestId('review-field-description')).toBeChecked();
    await diff.getByTestId('review-field-name').uncheck();
    await line.getByTestId('review-line-accept').click();

    await expect(line.getByTestId('review-line-decision')).toHaveText('Accepted', { timeout: 15_000 });
    await expect.poll(async () => (await asPerson<Cave>(page, 'GET', `/api/v1/caves/${cave.id}`)).description).toBe(
      proposed.description,
    );
    expect((await asPerson<Cave>(page, 'GET', `/api/v1/caves/${cave.id}`)).name, 'the name was not accepted').toBe(
      before.name,
    );
    await expect(page.getByTestId('review-batch-state')).toHaveText('Accepted');
  } finally {
    await phone.context.close();
    await editor.release();
  }
});

test('accepting a place pulls in the cave it needs, and rejecting a whole batch writes nothing', async ({
  page,
  browser,
}) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const editor = await editorOfItsOwn(browser, page.request, 'review-all');
  const phone = await signedInElsewhere(browser, editor.account);
  try {
    // The set needs a cave to be rooted at; the uploads below add new ones next to it.
    const types = await asPerson<{ id: number }[]>(editor.admin, 'GET', '/api/v1/cave-types');
    const root = await asPerson<Cave>(editor.admin, 'POST', '/api/v1/caves', {
      name: `E2E Review Root ${stamp}`,
      caveTypeId: types[0].id,
      visibility: 'authenticated',
      locationProtected: false,
      explorationStatus: 'unknown',
      isShowCave: false,
    });
    await markForReview(editor.admin, editor.account);
    const device = await phoneOf(phone.page, root.id, stamp);

    const keptCave = crypto.randomUUID();
    const keptEntrance = crypto.randomUUID();
    const keptName = `E2E Review Kept ${stamp}`;
    await device.upload([newCave(keptCave, keptName), newEntrance(keptEntrance, keptCave, `Gura ${stamp}`)]);

    await login(page);
    await openBatchOf(page, editor.account);

    // Pressing Accept on the entrance says the cave comes with it, and nothing is written until
    // the reviewer agrees.
    await lineNamed(page, `Gura ${stamp}`).getByTestId('review-line-accept').click();
    await expect(page.getByTestId('review-pullIn')).toContainText(keptName);
    await page.locator('.ant-modal-confirm').getByRole('button', { name: 'Accept', exact: true }).click();
    await expect(page.getByTestId('review-batch-state')).toHaveText('Accepted', { timeout: 15_000 });
    await expect.poll(() => statusOf(page, `/api/v1/features/${keptCave}`)).toBe(200);

    // A second upload is turned down whole, with a note; nothing of it is written.
    const turnedCave = crypto.randomUUID();
    const turnedName = `E2E Review Turned ${stamp}`;
    await device.upload([newCave(turnedCave, turnedName)]);
    await openBatchOf(page, editor.account);
    await expect(lineNamed(page, turnedName)).toBeVisible();
    await page.getByTestId('review-note').fill('Not this one');
    await page.getByTestId('review-reject-all').click();
    await page.locator('.ant-modal-confirm').getByRole('button', { name: 'Reject all', exact: true }).click();
    await expect(page.getByTestId('review-batch-state')).toHaveText('Rejected', { timeout: 15_000 });
    expect(await statusOf(page, `/api/v1/features/${turnedCave}`), 'a rejected creation is never written').toBe(404);
  } finally {
    await phone.context.close();
    await editor.release();
  }
});
