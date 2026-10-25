// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * A change a phone uploaded, read in the cave's history and taken back from there.
 *
 * The upload is arranged through the sync route — the route a phone itself uses — so the history
 * row is the one a real upload writes, with its source and the import it belongs to. What is
 * driven is the part a person does: finding the entry, seeing where it came from, following it to
 * its import, and putting the earlier value back.
 */
test('a field a phone uploaded is named as such in the history and is put back from there', async ({ page }) => {
  test.slow();

  const stamp = `${Date.now()}`;
  const before = `Before the phone ${stamp}`;
  const fromPhone = `From the phone ${stamp}`;

  await login(page);
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E History Cave ${stamp}`,
    description: before,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
  const set = await asPerson<{ id: string }>(page, 'POST', '/api/v1/sync/sets/', {
    name: `E2E phone ${stamp}`,
    uploadVisibility: 'private',
    rootFeatureIds: [cave.id],
    settings: {},
  });

  // A phone reads the cave and sends it back with a new description. The revision it quotes is
  // the one the download gave it, exactly as a device's would be.
  const download = await asPerson<{ features: { id: string; name: string; updatedAt: string }[] }>(
    page,
    'GET',
    `/api/v1/sync/sets/${set.id}/download`,
  );
  const held = download.features.find((f) => f.id === cave.id);
  expect(held, 'the set hands the phone the cave it is rooted at').toBeDefined();
  const upload = await asPerson<{ importBatchId: string }>(page, 'POST', `/api/v1/sync/sets/${set.id}/upload`, {
    batchId: crypto.randomUUID(),
    contractVersion: 1,
    rows: [
      {
        id: cave.id,
        kind: 'cave',
        baseRevision: held!.updatedAt,
        deleted: false,
        name: held!.name,
        description: fromPhone,
        caveTypeCode: 'cave',
        isMain: false,
      },
    ],
  });

  await gotoRoute(page, `/caves/${cave.id}`);

  // The entry says where it came from, and the import it belongs to is one click away.
  const entry = page
    .locator('.ant-timeline-item')
    .filter({ has: page.getByTestId('history-source').filter({ hasText: 'Phone upload' }) });
  await expect(entry).toHaveCount(1, { timeout: 30_000 });
  await expect(entry.getByText(fromPhone)).toBeVisible();
  await expect(entry.getByTestId('history-batch-link')).toHaveAttribute(
    'href',
    new RegExp(`batch=${upload.importBatchId}`),
  );

  // Putting the earlier value back goes through the server and leaves an entry of its own.
  await entry.getByRole('button', { name: 'Restore this value' }).click();
  await expect(page.getByText('Value restored.')).toBeVisible();
  await expect
    .poll(async () => (await asPerson<{ description: string | null }>(page, 'GET', `/api/v1/caves/${cave.id}`)).description)
    .toBe(before);
  await expect(
    page.locator('.ant-timeline-item').filter({ has: page.getByTestId('history-source').filter({ hasText: 'Restored from history' }) }),
  ).toHaveCount(1);

  // The link leads to the import: the batches tab opens with that batch's detail.
  await entry.getByTestId('history-batch-link').click();
  await expect(page).toHaveURL(new RegExp(`/geodata\\?tab=batches&batch=${upload.importBatchId}`));
  await expect(page.getByTestId('import-batch-detail')).toBeVisible();
});
