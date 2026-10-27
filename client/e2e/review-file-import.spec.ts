// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, editorOfItsOwn, signedInElsewhere } from './arrange.ts';
import { login } from './helpers.ts';
import { lineNamed, markForReview, openBatchOf, statusOf } from './reviewArrange.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * A file import from an account under review waits like a phone's upload does. The file here is a
 * GPX of two invented waypoints, written by the spec; what is driven is the reviewer taking one
 * candidate and turning the other down.
 */

function gpxOf(stamp: string): string {
  const point = (name: string, lat: number, lon: number) =>
    `<wpt lat="${lat}" lon="${lon}"><name>${name}</name></wpt>`;
  return (
    '<?xml version="1.0" encoding="UTF-8"?>' +
    '<gpx version="1.1" creator="e2e" xmlns="http://www.topografix.com/GPX/1/1">' +
    point(`E2E waypoint one ${stamp}`, 45.4, 22.4) +
    point(`E2E waypoint two ${stamp}`, 45.41, 22.41) +
    '</gpx>'
  );
}

/** Uploads the file as the signed-in person, waits for it to be read and answers the ids of its candidates. */
async function uploadedCandidates(page: Page, stamp: string, options: Record<string, unknown>) {
  const token = await bearerToken(page);
  const auth = { Authorization: `Bearer ${token}` };
  const upload = await page.request.post('/api/v1/geofiles', {
    headers: auth,
    multipart: { file: { name: `e2e-held-${stamp}.gpx`, mimeType: 'application/octet-stream', buffer: Buffer.from(gpxOf(stamp)) } },
  });
  expect(upload.status(), await upload.text()).toBe(201);
  const { id } = (await upload.json()) as { id: string };
  await expect
    .poll(async () => ((await (await page.request.get(`/api/v1/geofiles/${id}/status`, { headers: auth })).json()) as { importStatus: string }).importStatus, {
      timeout: 60_000,
    })
    .toBe('imported');
  const preview = await page.request.post(`/api/v1/geofiles/${id}/import/preview`, {
    headers: auth,
    data: { options, pageSize: 200 },
  });
  expect(preview.status(), await preview.text()).toBe(200);
  const { items } = (await preview.json()) as { items: { sourceId: number }[] };
  return { id, ids: items.map((i) => i.sourceId).sort((a, b) => a - b) };
}

test('a reviewer accepts one candidate of a held file import and turns the other down', async ({ page, browser }) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const editor = await editorOfItsOwn(browser, page.request, 'review-file');
  const importer = await signedInElsewhere(browser, editor.account);
  try {
    await markForReview(editor.admin, editor.account);
    const options = {
      termRuleSetId: null,
      languages: [],
      mapping: {},
      elevation: 'keep',
      tracks: 'ignore',
      visibility: 'private',
      duplicateRadiusMeters: 1,
      tagIds: [],
    };
    const file = await uploadedCandidates(importer.page, stamp, options);
    expect(file.ids, 'the file holds the two waypoints').toHaveLength(2);
    const names = file.ids.map((_, i) => `E2E Held ${i === 0 ? 'one' : 'two'} ${stamp}`);
    const decisions = Object.fromEntries(
      file.ids.map((id, i) => [String(id), { action: 'create', kind: 'cave', caveTypeCode: 'cave', name: names[i] }]),
    );
    const committed = await importer.page.request.post(`/api/v1/geofiles/${file.id}/import/commit`, {
      headers: { Authorization: `Bearer ${await bearerToken(importer.page)}` },
      data: { options, selection: file.ids, decisions, withoutReview: false },
    });
    expect(committed.status(), await committed.text()).toBe(202);
    const held = (await committed.json()) as { held: boolean; batchId: string };
    expect(held.held, 'the import waits for a reviewer').toBe(true);

    await login(page);
    await openBatchOf(page, editor.account);

    // Each candidate is shown with the name it would be created under.
    await expect(page.getByTestId('review-line')).toHaveCount(2);
    await expect(lineNamed(page, names[0])).toBeVisible();
    await expect(lineNamed(page, names[1])).toBeVisible();

    // Decided lines are hidden while others wait; keep them in view to read what became of each.
    await page.getByTestId('review-show-decided').check();
    await lineNamed(page, names[0]).getByTestId('review-line-accept').click();
    await expect(lineNamed(page, names[0]).getByTestId('review-line-decision')).toHaveText('Accepted', { timeout: 15_000 });
    await lineNamed(page, names[1]).getByTestId('review-line-reject').click();
    await expect(lineNamed(page, names[1]).getByTestId('review-line-decision')).toHaveText('Rejected', { timeout: 15_000 });
    await expect(page.getByTestId('review-batch-state')).toHaveText('Partly accepted');

    // Only the accepted candidate was written: the batch's provenance names the one row it made.
    const made = await asPerson<{ items: { featureId: string | null; featureName: string | null }[] }>(
      page,
      'GET',
      `/api/v1/import-batches/${held.batchId}`,
    );
    const rows = made.items.filter((i) => i.featureId !== null);
    expect(rows.map((r) => r.featureName)).toEqual([names[0]]);
    expect(await statusOf(page, `/api/v1/features/${rows[0].featureId}`)).toBe(200);
  } finally {
    await importer.context.close();
    await editor.release();
  }
});
