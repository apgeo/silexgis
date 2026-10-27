// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Page } from '@playwright/test';
import { asPerson } from './arrange.ts';

/**
 * A cave with two phone recordings, arranged through the sync route a phone itself uses so the
 * rows are the ones a real upload writes: one still open on the phone, which waits for it, and one
 * closed, which the installation makes into a trip on its own and flags, because its scans are at
 * no marked place. The caller is signed in.
 */
export async function arrangeRecordings(page: Page, stamp: string) {
  const waitingTitle = `E2E Waiting Recording ${stamp}`;
  const flaggedTitle = `E2E Flagged Recording ${stamp}`;
  const started = new Date(Date.now() - 36 * 3_600_000);
  const ended = new Date(started.getTime() + 4 * 3_600_000);

  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: `E2E Recordings Cave ${stamp}`,
    description: null,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
  const set = await asPerson<{ id: string }>(page, 'POST', '/api/v1/sync/sets/', {
    name: `E2E recordings ${stamp}`,
    uploadVisibility: 'private',
    rootFeatureIds: [cave.id],
    settings: {},
  });

  const recording = (id: string, title: string, closed: boolean) => ({
    id,
    baseRevision: null,
    deleted: false,
    caveId: cave.id,
    title,
    description: 'Invented for the test',
    log: null,
    startedAt: started.toISOString(),
    endedAt: closed ? ended.toISOString() : null,
    createdByDeviceUserId: crypto.randomUUID(),
  });
  const scan = (recordingId: string, minutes: number) => ({
    id: crypto.randomUUID(),
    recordingId,
    placeId: null,
    scannedAt: new Date(started.getTime() + minutes * 60_000).toISOString(),
    notes: `scan ${minutes}`,
    createdByDeviceUserId: crypto.randomUUID(),
  });

  // One recording still open on the phone, which waits for it; one closed, which the installation
  // makes into a trip on its own and flags, because its scans are at no marked place.
  const waiting = crypto.randomUUID();
  const flagged = crypto.randomUUID();
  await asPerson(page, 'POST', `/api/v1/sync/sets/${set.id}/upload`, {
    batchId: crypto.randomUUID(),
    contractVersion: 1,
    tripRecordings: [recording(waiting, waitingTitle, false), recording(flagged, flaggedTitle, true)],
    tripPoints: [scan(waiting, 10), scan(waiting, 20), scan(flagged, 10)],
  });

  return { cave, waiting, flagged, waitingTitle, flaggedTitle };
}
