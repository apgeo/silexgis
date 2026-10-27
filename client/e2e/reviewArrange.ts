// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { asPerson, type Account } from './arrange.ts';
import { gotoRoute } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * What the review flows arrange: an account marked for review, the phone it uploads from, and the
 * rows such a phone sends. Everything is written through the routes a real phone and a real
 * administrator use, so the held batches are the ones production holds.
 */

/** A set the uploader keeps, rooted at a cave they may edit, and a function to send a held upload to it. */
export async function phoneOf(uploader: Page, caveId: string, stamp: string) {
  const set = await asPerson<{ id: string }>(uploader, 'POST', '/api/v1/sync/sets/', {
    name: `E2E review phone ${stamp}`,
    uploadVisibility: 'private',
    rootFeatureIds: [caveId],
    settings: {},
  });
  return {
    async download() {
      return asPerson<{ features: { id: string; updatedAt: string }[] }>(
        uploader,
        'GET',
        `/api/v1/sync/sets/${set.id}/download`,
      );
    },
    async upload(rows: unknown[]) {
      return asPerson<{ importBatchId: string }>(uploader, 'POST', `/api/v1/sync/sets/${set.id}/upload`, {
        batchId: crypto.randomUUID(),
        contractVersion: 1,
        clientFeatures: ['held'],
        rows,
      });
    },
  };
}

export async function markForReview(admin: Page, account: Account, needsReview = true) {
  await asPerson(admin, 'PUT', `/api/v1/users/${account.id}/upload-review`, { uploadsNeedReview: needsReview });
}

export const newCave = (id: string, name: string) => ({
  id,
  kind: 'cave',
  baseRevision: null,
  deleted: false,
  name,
  caveTypeCode: 'cave',
  isMain: false,
});

export const newEntrance = (id: string, caveId: string, name: string) => ({
  id,
  kind: 'caveEntrance',
  parentId: caveId,
  baseRevision: null,
  deleted: false,
  name,
  entranceTypeCode: 'natural',
  isMain: true,
  geometry: { type: 'Point', coordinates: [22.51, 45.61] },
});

/** A cave the uploader's set can be rooted at; the uploads add new rows next to it. */
export async function rootCave(admin: Page, name: string) {
  const types = await asPerson<{ id: number }[]>(admin, 'GET', '/api/v1/cave-types');
  return asPerson<{ id: string; name: string }>(admin, 'POST', '/api/v1/caves', {
    name,
    caveTypeId: types[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
}

/** Opens the one waiting batch the uploader's name stands against in the queue. */
export async function openBatchOf(page: Page, account: Account) {
  await gotoRoute(page, '/review');
  const row = page.getByTestId('review-queue-row').filter({ hasText: account.displayName });
  await expect(row).toHaveCount(1, { timeout: 30_000 });
  await row.click();
  await expect(page.getByTestId('review-batch')).toBeVisible();
}

export const lineNamed = (page: Page, name: string) => page.getByTestId('review-line').filter({ hasText: name });

/** The status the API gives for a row, as the signed-in person asks. */
export async function statusOf(page: Page, path: string): Promise<number> {
  const response = await page.request.fetch(path, { headers: { Authorization: `Bearer ${await bearerToken(page)}` } });
  return response.status();
}
