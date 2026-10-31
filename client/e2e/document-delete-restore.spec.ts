// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { gotoRoute, login, openCaveFromRegistry, confirmOk } from './helpers.ts';

/**
 * Deleting a document and taking it back, as the person who deleted it sees both.
 *
 * A delete marks the document and removes nothing, so the flow is the round trip: a document is
 * uploaded to the demo cave, deleted from its own page with a confirmation that says it can be
 * restored, is gone from the cave's list of files and from its own address, is found on the list
 * of deleted documents, and is restored from there to the same page. The file is generated here
 * with a name and content of its own, so that no other spec's fixture is touched and the store's
 * duplicate warning has nothing to say.
 */
test.describe.configure({ timeout: 150_000 });

test('a deleted document leaves the cave and its own address, and is restored whole', async ({
  page,
  consoleErrors,
}) => {
  // The page asks for the deleted document by its address, is answered 404, and says so — and
  // the browser writes a console error for every request that answered 404, however well the
  // page handled it.
  consoleErrors.allow(
    /status of 404/,
    'this flow opens the address of a document it has just deleted, which the server answers as not found',
  );

  const stamp = `${Date.now()}`;
  const fileName = `e2e-deletion-${stamp}.txt`;

  await login(page);
  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  const gallery = page.locator('.ant-card', { hasText: 'Photos & documents' });
  await expect(gallery).toBeVisible({ timeout: 15_000 });
  await gallery.locator('input[type=file]').setInputFiles({
    name: fileName,
    mimeType: 'text/plain',
    buffer: Buffer.from(`Notes written by a test, ${stamp}.\n`),
  });
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 30_000 });

  const row = page.locator('.silex-list-item', { hasText: fileName }).first();
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByRole('button', { name: 'Open the document' }).click();
  await page.waitForURL(/\/documents\/[0-9a-f-]{36}/);
  const documentId = page.url().split('/documents/')[1].split('?')[0];
  await expect(page.getByRole('heading', { level: 3, name: fileName })).toBeVisible({
    timeout: 15_000,
  });

  // The confirmation says what is about to happen: nothing is removed, and it can be put back.
  await page.getByTestId('document-delete').click();
  const confirmation = page.getByRole('tooltip');
  await expect(confirmation).toContainText('Delete this document?');
  await expect(confirmation).toContainText(/can be restored from Deleted documents/);
  await confirmOk(page, { scope: confirmation });
  await expect(page).toHaveURL(/\/cabinets$/, { timeout: 30_000 });

  // Gone from the cave's files…
  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  await expect(page.getByText('Photos & documents')).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.silex-list-item', { hasText: fileName })).toHaveCount(0);

  // …and from its own address, which says what it says of a document that never existed, and
  // points whoever could have deleted one at where a deleted document would be.
  await gotoRoute(page, `/documents/${documentId}`);
  await expect(page.getByTestId('document-not-found-deleted')).toBeVisible({ timeout: 30_000 });
  await page.getByTestId('document-not-found-deleted').click();
  await expect(page).toHaveURL(/\/documents\/deleted$/);

  // The deleted list holds it, and restoring is confirmed first.
  const deleted = page.getByTestId('deleted-documents').getByRole('row', { name: new RegExp(fileName) });
  await expect(deleted).toBeVisible({ timeout: 30_000 });
  await deleted.getByTestId('deleted-document-restore').click();
  const restoring = page.getByRole('tooltip');
  await expect(restoring).toContainText('Restore this document?');
  await restoring.getByRole('button', { name: 'Restore' }).click();

  // Back on its own page, and no longer among the deleted ones.
  await expect(page).toHaveURL(new RegExp(`/documents/${documentId}$`), { timeout: 30_000 });
  await expect(page.getByRole('heading', { level: 3, name: fileName })).toBeVisible({
    timeout: 30_000,
  });
  await gotoRoute(page, '/documents/deleted');
  await expect(page.getByTestId('deleted-documents')).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('row', { name: new RegExp(fileName) })).toHaveCount(0);

  // Left as found: detaching the file from the cave, as the other document flows do.
  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  const left = page.locator('.silex-list-item', { hasText: fileName }).first();
  await expect(left).toBeVisible({ timeout: 15_000 });
  await left.getByRole('button', { name: 'delete' }).click();
  await confirmOk(page, { exact: true });
  await expect(page.locator('.silex-list-item', { hasText: fileName })).toHaveCount(0, {
    timeout: 15_000,
  });
});
