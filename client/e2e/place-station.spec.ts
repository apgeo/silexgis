// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHash } from 'node:crypto';
import { expect, type Locator, type Page } from '@playwright/test';
import { asPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A place a phone mapped to a station of a survey, in the browser. The place, the phone's survey
 * file and the two mappings — one at a station, one at the far end of a splay — are sent the way a
 * phone sends them, through the sync upload. The browser lists both on the place's page with the
 * station they name, offers no edit, and removes one; the removal is checked on the survey-models
 * stream the phones read.
 *
 * The survey file is invented bytes and the places are invented names: real surveys and real cave
 * registers never enter test data.
 */

interface PhoneStations {
  setId: string;
  placeId: string;
  atStation: string;
  atSplay: string;
}

async function arrange(page: Page, stamp: string): Promise<PhoneStations> {
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const cave = (
    await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
      name: `E2E Station Cave ${stamp}`,
      description: null,
      caveTypeId: caveTypes[0].id,
      visibility: 'authenticated',
      locationProtected: false,
      explorationStatus: 'unknown',
      isShowCave: false,
    })
  ).id;
  const set = await asPerson<{ id: string }>(page, 'POST', '/api/v1/sync/sets/', {
    name: `E2E stations ${stamp}`.slice(0, 30),
    uploadVisibility: 'private',
    rootFeatureIds: [cave],
    settings: {},
  });

  // The survey file staged first, as a phone stages it, then sent as a document filed on the cave
  // as survey data, beside the place.
  const bytes = Buffer.from(`invented survey file ${stamp}`);
  const staged = await asPerson<{ id: string }>(page, 'POST', '/api/v1/files/uploads', {
    fileName: 'telefon.lox',
    sizeBytes: bytes.length,
    stageForSync: true,
  });
  const token = await bearerToken(page);
  const sent = await page.request.put(`/api/v1/files/uploads/${staged.id}?offset=0`, {
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/octet-stream' },
    data: bytes,
  });
  expect(sent.ok(), `staging answered ${sent.status()}`).toBeTruthy();
  await asPerson(page, 'POST', `/api/v1/files/uploads/${staged.id}/complete`);

  const placeId = crypto.randomUUID();
  const documentId = crypto.randomUUID();
  const first = await asPerson<{ rows: { status: string }[]; documentLinks: { status: string }[] }>(
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
          parentId: cave,
          baseRevision: null,
          deleted: false,
          name: `Sala Statiei ${stamp}`,
          featureTypeCode: 'cave_place',
          isMain: false,
        },
      ],
      documents: [
        {
          id: documentId,
          fileName: 'telefon.lox',
          sha256: createHash('sha256').update(bytes).digest('hex'),
          sizeBytes: bytes.length,
          stagedUploadId: staged.id,
        },
      ],
      documentLinks: [{ documentId, target: { kind: 'feature', id: cave }, role: 'surveyData' }],
    },
  );
  expect(first.rows.map((row) => row.status)).toEqual(['created']);
  expect(first.documentLinks.map((row) => row.status)).toEqual(['created']);

  // A place carries one station per survey, so the second mapping names a second place.
  const otherPlace = crypto.randomUUID();
  await asPerson(page, 'POST', `/api/v1/sync/sets/${set.id}/upload`, {
    batchId: crypto.randomUUID(),
    contractVersion: 1,
    rows: [
      {
        id: otherPlace,
        kind: 'generic',
        parentId: cave,
        baseRevision: null,
        deleted: false,
        name: `Puțul Statiei ${stamp}`,
        featureTypeCode: 'cave_place',
        isMain: false,
      },
    ],
  });
  const atStation = crypto.randomUUID();
  const atSplay = crypto.randomUUID();
  const mapped = await asPerson<{ placeStations: { status: string }[] }>(
    page,
    'POST',
    `/api/v1/sync/sets/${set.id}/upload`,
    {
      batchId: crypto.randomUUID(),
      contractVersion: 1,
      placeStations: [
        { id: atStation, placeId, documentId, station: 'A.12' },
        { id: atSplay, placeId: otherPlace, documentId, station: 'A.13', splay: [1.5, -2, 0.25] },
      ],
    },
  );
  expect(mapped.placeStations.map((row) => row.status)).toEqual(['created', 'created']);
  return { setId: set.id, placeId, atStation, atSplay };
}

function linksCard(page: Page): Locator {
  return page.locator('.ant-card').filter({ has: page.getByText(/^Linked items \(\d+\)$/) });
}

function linkRow(scope: Locator, text: string | RegExp): Locator {
  return scope.locator('.ant-flex > .ant-flex').filter({ hasText: text }).first();
}

test("a place a phone mapped to a station is listed on its page with the station, and removed there for the phones", async ({
  page,
}) => {
  test.setTimeout(180_000);
  const stamp = String(Date.now());
  await login(page);
  const phone = await arrange(page, stamp);

  await gotoRoute(page, `/features/${phone.placeId}`);
  const card = linksCard(page);
  await expect(card).toBeVisible({ timeout: 30_000 });
  const row = linkRow(card, 'station A.12');
  await expect(row).toBeVisible();
  await expect(row).toContainText('At station');

  // Set on the phones: removable here, never edited as a link.
  await row.getByRole('button', { name: 'Link actions' }).click();
  await expect(page.getByRole('menuitem', { name: 'Delete link' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Edit link' })).toHaveCount(0);
  const removed = page.waitForResponse(
    (r) => r.request().method() === 'DELETE' && new URL(r.url()).pathname === `/api/v1/reslinks/${phone.atStation}`,
    { timeout: 15_000 },
  );
  await page.getByRole('menuitem', { name: 'Delete link' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete link' }).click();
  expect((await removed).ok()).toBeTruthy();
  await expect(linkRow(card, 'station A.12')).toHaveCount(0);

  // The phones are told: the removed mapping is a tombstone, the other still a mapping, read as a
  // splay end from its station.
  const stream = await asPerson<{
    placeStations: { id: string; station: string; splay?: number[] }[];
    placeStationTombstones: { id: string }[];
  }>(page, 'GET', `/api/v1/sync/sets/${phone.setId}/survey-models`);
  expect(stream.placeStationTombstones.map((t) => t.id)).toEqual([phone.atStation]);
  expect(stream.placeStations.map((s) => s.id)).toEqual([phone.atSplay]);
  expect(stream.placeStations[0].splay).toEqual([1.5, -2, 0.25]);
});
