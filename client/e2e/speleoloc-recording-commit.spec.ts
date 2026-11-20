// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, surveyRead } from './arrange.ts';
import { bearerToken } from './rastermapApi.ts';
import { gotoRoute, login } from './helpers.ts';

const fixture = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'P8_Master.3d');

/** How deep the marked place the scans were made at lies, as the phone measured it. */
const PLACE_DEPTH_M = 20;

/**
 * A phone recording made into a trip by hand, from the review to the rows the confirmation writes.
 *
 * Everything the phone sends is sent the way a phone sends it — the marked place, the recording
 * and its scans through the sync upload — so the rows reviewed are the ones a real upload writes.
 * The survey is the committed fixture, on a cave of this run's own. The recording is still open on
 * the phone, so the installation leaves it for a person and converts nothing on its own. What is
 * driven is the review: where the positions go, which survey they are positions in, how many
 * stations each scan is offered, whose scans they are, and the confirmation; what is asserted at
 * the end is the server's record, not the page's.
 */
test('a phone recording is reviewed and recorded onto a trip the import creates', async ({ page }) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const caveName = `E2E Recording Commit Cave ${stamp}`;
  const caverName = `E2E Recording Commit Caver ${stamp}`;
  const recordingTitle = `E2E Recording To Commit ${stamp}`;

  await login(page);

  // ---- The cave, its survey and the caver the scans are about ----
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: caveName,
    description: null,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
  const uploaded = await page.request.post(`/api/v1/caves/${cave.id}/survey-models`, {
    headers: { Authorization: `Bearer ${await bearerToken(page)}` },
    multipart: {
      file: { name: `Recording commit ${stamp}.3d`, mimeType: 'application/octet-stream', buffer: readFileSync(fixture) },
    },
  });
  expect(uploaded.status(), await uploaded.text()).toBe(201);
  const modelId = ((await uploaded.json()) as { id: string }).id;
  await surveyRead(page, modelId);
  const caver = await asPerson<{ id: string }>(page, 'POST', '/api/v1/cavers', {
    fullName: caverName,
    email: null,
    phone: null,
    notes: null,
  });

  // ---- What the phone sends: a marked place in the cave, then a recording with two scans there ----
  const set = await asPerson<{ id: string }>(page, 'POST', '/api/v1/sync/sets/', {
    name: `E2E recording commit ${stamp}`,
    uploadVisibility: 'private',
    rootFeatureIds: [cave.id],
    settings: {},
  });
  const placeId = crypto.randomUUID();
  const places = await asPerson<{ rows: { id: string; status: string }[] }>(
    page,
    'POST',
    `/api/v1/sync/sets/${set.id}/upload`,
    {
      batchId: crypto.randomUUID(),
      contractVersion: 1,
      rows: [
        {
          id: placeId,
          kind: 'generic',
          baseRevision: null,
          deleted: false,
          parentId: cave.id,
          name: `E2E marked place ${stamp}`,
          featureTypeCode: 'cave_place',
          isMain: false,
          geometry: { type: 'Point', coordinates: [25.5, 45.5] },
          properties: { speleolocSchemaVersion: 1, speleolocDepthInCave: PLACE_DEPTH_M },
          clientUpdatedAt: null,
        },
      ],
    },
  );
  expect(places.rows.map((row) => `${row.id} ${row.status}`)).toEqual([`${placeId} created`]);

  const deviceUserId = crypto.randomUUID();
  const recordingId = crypto.randomUUID();
  const started = new Date(Date.now() - 6 * 3_600_000);
  const scanIds = [crypto.randomUUID(), crypto.randomUUID()];
  await asPerson(page, 'POST', `/api/v1/sync/sets/${set.id}/upload`, {
    batchId: crypto.randomUUID(),
    contractVersion: 1,
    tripRecordings: [
      {
        id: recordingId,
        baseRevision: null,
        deleted: false,
        caveId: cave.id,
        title: recordingTitle,
        description: 'Invented for the test',
        log: null,
        startedAt: started.toISOString(),
        // Still open on the phone, so it waits for a person rather than being converted.
        endedAt: null,
        createdByDeviceUserId: deviceUserId,
      },
    ],
    tripPoints: scanIds.map((id, index) => ({
      id,
      recordingId,
      placeId,
      scannedAt: new Date(started.getTime() + (index + 1) * 15 * 60_000).toISOString(),
      notes: `scan ${index + 1}`,
      createdByDeviceUserId: deviceUserId,
    })),
  });

  // ---- The review ----
  await gotoRoute(page, `/trip-logs/speleoloc-import/recording/${recordingId}`);
  await expect(page.getByTestId('speleoloc-import-recording-locked')).toContainText(recordingTitle, {
    timeout: 30_000,
  });
  const commit = page.getByTestId('speleoloc-import-commit');
  await expect(commit).toBeDisabled();

  // Onto an existing trip, nothing is confirmable until the trip is named; into a new one, the
  // trip is the import's to make.
  const destination = page.getByTestId('speleoloc-import-destination');
  await expect(page.getByTestId('speleoloc-import-trip')).toBeVisible();
  await destination.getByText('Into a trip this import creates').click();
  await expect(page.getByTestId('speleoloc-import-trip')).toHaveCount(0);
  await expect(page.getByTestId('speleoloc-import-visibility')).toBeVisible();

  // The survey, reached through its cave.
  await page.getByTestId('speleoloc-import-cave').click();
  await page.getByTestId('speleoloc-import-cave-input').fill(caveName);
  await page.getByTestId('speleoloc-import-cave-row').filter({ hasText: caveName }).click();
  await expect(page.getByTestId('speleoloc-import-cave')).toContainText(caveName);
  const model = page.getByTestId('speleoloc-import-survey-model');
  await expect(model).toBeEnabled();
  await model.click();
  await page
    .locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: `Recording commit ${stamp}` })
    .click();
  await expect(model).toContainText(`Recording commit ${stamp}`);

  // Each scan is offered as many stations as asked for, the nearest one standing as the answer.
  const count = page.getByTestId('speleoloc-import-candidate-count');
  await count.fill('3');
  await count.press('Tab');
  const firstStation = page.getByTestId(`speleoloc-import-station-${scanIds[0]}`);
  await expect(firstStation).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId(`speleoloc-import-state-${scanIds[0]}`)).toHaveText(
    /^(Proposed|Several stations at that depth)$/,
  );
  await firstStation.click();
  await expect(page.locator('.ant-select-dropdown:visible .ant-select-item-option')).toHaveCount(3, {
    timeout: 15_000,
  });
  await page.keyboard.press('Escape');
  await expect(page.locator('.ant-select-dropdown:visible')).toHaveCount(0);
  const proposed = (await firstStation.locator('.ant-select-content').innerText()).split(' · ')[0].trim();
  expect(proposed).not.toBe('');

  // Nobody has said whose scans these are: nothing can be recorded until somebody does.
  await expect(page.getByTestId(`speleoloc-import-unmapped-${scanIds[0]}`)).toBeVisible();
  await expect(commit).toBeDisabled();
  const who = page.getByTestId(`speleoloc-import-map-${deviceUserId.slice(0, 8)}`);
  await who.click();
  await page.keyboard.type(caverName);
  await page.locator('.ant-select-dropdown:visible').getByText(caverName, { exact: true }).click();
  await expect(page.getByTestId(`speleoloc-import-unmapped-${scanIds[0]}`)).toHaveCount(0);
  for (const scanId of scanIds) {
    await expect(page.getByTestId(`speleoloc-import-take-${scanId}`)).toBeChecked();
  }

  // ---- The confirmation ----
  await expect(commit).toBeEnabled();
  await expect(commit).toHaveText('Record 2 positions');
  const answered = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname === `/api/v1/speleoloc-imports/recordings/${recordingId}/commit`,
  );
  await commit.click();
  const response = await answered;
  expect(response.status()).toBe(200);
  const outcome = (await response.json()) as {
    tripLogId: string | null;
    createdTrip: boolean;
    createdEventCount: number;
    failures: unknown[];
    held: boolean;
  };
  expect(outcome).toMatchObject({ createdTrip: true, createdEventCount: 2, failures: [], held: false });
  const result = page.getByRole('dialog', { name: 'The recording has been imported' });
  await expect(result).toBeVisible();
  // The review behind it is not asked again: it would only be refused as already converted.
  await expect(page.getByTestId('speleoloc-import-preview-error')).toHaveCount(0);
  await expect(result.getByTestId('speleoloc-import-result-destination')).toContainText(
    'The trip was created by this import',
  );

  // ---- The server's record: both scans on the new trip, as the caver, at the station shown ----
  const tripId = outcome.tripLogId!;
  const events = await asPerson<{
    items: { caverId: string; surveyModelId: string | null; stationName: string | null }[];
  }>(page, 'GET', `/api/v1/trip-logs/${tripId}/tracking/events?page=1&pageSize=50`);
  expect(events.items).toHaveLength(2);
  for (const event of events.items) {
    expect(event).toMatchObject({ caverId: caver.id, surveyModelId: modelId, stationName: proposed });
  }

  // Closing the result opens the trip it went onto.
  await result.locator('.ant-modal-footer').getByRole('button', { name: 'Close' }).click();
  await expect(page).toHaveURL(new RegExp(`/trip-logs/${tripId}$`), { timeout: 15_000 });
});
