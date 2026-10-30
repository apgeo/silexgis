// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { gotoRoute, login, confirmOk } from './helpers.ts';

/**
 * Taking a phone's upload back from the imports tab.
 *
 * Uploads are arranged through the sync route a phone itself uses, so the batches, their lines and
 * the history rows are the ones a real upload writes. What is driven is what a person does: open
 * the upload, read what it changed, and undo it — with one field changed again afterwards by a
 * second upload, which is the case an undo has to be honest about.
 */

interface Arranged {
  caveId: string;
  firstBatch: string;
  before: { name: string; description: string };
  firstUpload: { name: string; description: string };
  later: string;
}

/** A cave, and two uploads to it: the first changes name and description, the second description again. */
async function arrange(page: Page, stamp: string): Promise<Arranged> {
  const before = { name: `E2E Undo Cave ${stamp}`, description: `Before ${stamp}` };
  const firstUpload = { name: `E2E Undo Cave renamed ${stamp}`, description: `First upload ${stamp}` };
  const later = `Second upload ${stamp}`;

  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: before.name,
    description: before.description,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
  const set = await asPerson<{ id: string }>(page, 'POST', '/api/v1/sync/sets/', {
    name: `E2E undo phone ${stamp}`,
    uploadVisibility: 'private',
    rootFeatureIds: [cave.id],
    settings: {},
  });

  const send = async (name: string, description: string) => {
    const download = await asPerson<{ features: { id: string; updatedAt: string }[] }>(
      page,
      'GET',
      `/api/v1/sync/sets/${set.id}/download`,
    );
    const held = download.features.find((f) => f.id === cave.id);
    expect(held, 'the set hands the phone the cave it is rooted at').toBeDefined();
    return asPerson<{ importBatchId: string }>(page, 'POST', `/api/v1/sync/sets/${set.id}/upload`, {
      batchId: crypto.randomUUID(),
      contractVersion: 1,
      rows: [
        {
          id: cave.id,
          kind: 'cave',
          baseRevision: held!.updatedAt,
          deleted: false,
          name,
          description,
          caveTypeCode: 'cave',
          isMain: false,
        },
      ],
    });
  };

  const first = await send(firstUpload.name, firstUpload.description);
  await send(firstUpload.name, later);
  return { caveId: cave.id, firstBatch: first.importBatchId, before, firstUpload, later };
}

const cave = (page: Page, id: string) =>
  asPerson<{ name: string; description: string | null }>(page, 'GET', `/api/v1/caves/${id}`);

async function confirmPopover(page: Page) {
  await confirmOk(page, { scope: page.locator('.ant-popover:visible'), exact: true });
}

test('an upload is undone, and a field a later upload changed is left and said', async ({ page }) => {
  test.slow();
  const stamp = `${Date.now()}`;
  await login(page);
  const a = await arrange(page, stamp);

  await gotoRoute(page, `/geodata?tab=batches&batch=${a.firstBatch}`);
  const drawer = page.getByTestId('import-batch-detail');
  await expect(drawer).toBeVisible({ timeout: 30_000 });

  // The line names the cave, and opening it shows what the upload changed in it: both fields.
  // Which one an undo leaves is the server's answer, read below, not a tag on the line.
  await expect(drawer.getByRole('link', { name: a.firstUpload.name })).toBeVisible();
  await drawer.locator('.ant-table-row-expand-icon').first().click();
  const fields = drawer.getByTestId('upload-row-field');
  await expect(fields).toHaveCount(2, { timeout: 15_000 });
  await expect(fields.filter({ hasText: 'Description' })).toHaveCount(1);
  await expect(fields.filter({ hasText: 'Name' })).toHaveCount(1);

  // Undoing puts the name back and leaves the description, and the result says which and why.
  await drawer.getByTestId('upload-undo').click();
  await confirmPopover(page);
  const result = drawer.getByTestId('upload-undo-result');
  await expect(result).toContainText('Partly undone', { timeout: 15_000 });
  await expect(result).toContainText('Description was left: it has been changed since');
  await expect.poll(async () => (await cave(page, a.caveId)).name).toBe(a.before.name);
  expect((await cave(page, a.caveId)).description, 'the later upload’s description is left alone').toBe(a.later);

  // The line now says it was undone, and the upload cannot be undone a second time.
  await expect(drawer.getByText('Undone', { exact: true }).first()).toBeVisible();
  await expect(drawer.getByTestId('upload-undo')).toHaveCount(0);
});

test('"undo anyway" puts back a field a later upload changed', async ({ page }) => {
  test.slow();
  const stamp = `${Date.now()}`;
  await login(page);
  const a = await arrange(page, stamp);

  await gotoRoute(page, `/geodata?tab=batches&batch=${a.firstBatch}`);
  const drawer = page.getByTestId('import-batch-detail');
  await expect(drawer).toBeVisible({ timeout: 30_000 });

  await drawer.getByTestId('upload-undo-anyway').click();
  await confirmPopover(page);
  await expect(drawer.getByTestId('upload-undo-result')).toContainText('Undone', { timeout: 15_000 });

  await expect.poll(async () => (await cave(page, a.caveId)).description).toBe(a.before.description);
  expect((await cave(page, a.caveId)).name).toBe(a.before.name);
});

test('one row of an upload can be undone on its own', async ({ page }) => {
  test.slow();
  const stamp = `${Date.now()}`;
  await login(page);
  const a = await arrange(page, stamp);

  await gotoRoute(page, `/geodata?tab=batches&batch=${a.firstBatch}`);
  const drawer = page.getByTestId('import-batch-detail');
  await expect(drawer).toBeVisible({ timeout: 30_000 });

  await drawer.getByTestId('upload-undo-row').click();
  await confirmPopover(page);
  await expect(drawer.getByTestId('upload-undo-result')).toBeVisible({ timeout: 15_000 });
  await expect.poll(async () => (await cave(page, a.caveId)).name).toBe(a.before.name);
});
