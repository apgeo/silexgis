// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect } from '@playwright/test';
import { asPerson, localDay, tripBody, tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { chooseOption, gotoRoute, login } from './helpers.ts';
import { zipEntries, zipEntryBytes } from './reportMap.ts';

/**
 * A club's own write-up layout, saved on the administration page and then chosen on a trip's
 * report page, reaches the document that is downloaded.
 *
 * Every other flow writes a trip up from the layout the product ships, so a club layout that is
 * saved and then quietly ignored — the chooser sending nothing, or the server falling back to the
 * shipped one — passes all of them. The layout here carries a line nothing else writes, and the
 * downloaded document is opened to find it.
 *
 * The layout is not made the default, so other flows writing a trip up in the same run are not
 * handed it; it and the trip are removed afterwards.
 */
test('a layout saved on the administration page is the one a chosen write-up is built from', async ({ page }) => {
  test.slow();
  const run = `${Date.now()}`;
  const layoutName = `E2E layout ${run}`;
  const marker = `Written to the E2E layout ${run}`;
  const tripTitle = `E2E Layout Trip ${run}`;
  await login(page);

  let layoutId: string | undefined;
  let tripId: string | undefined;
  try {
    await gotoRoute(page, '/admin/report-templates');
    await expect(page.getByRole('heading', { name: 'Report layouts' })).toBeVisible({ timeout: 45_000 });
    await page.getByRole('button', { name: /Add a layout/ }).click();
    const editor = page.getByRole('dialog');
    await editor.getByLabel('Name').fill(layoutName);
    await editor.getByTestId('report-template-body').fill(`title: {title}\ntext: ${marker}\n`);
    const created = page.waitForResponse(
      (r) => new URL(r.url()).pathname.replace(/\/$/, '') === '/api/v1/report-templates'
        && r.request().method() === 'POST',
    );
    await editor.getByRole('button', { name: 'Save' }).click();
    const answer = await created;
    expect(answer.ok(), `saving the layout answered ${answer.status()}`).toBeTruthy();
    layoutId = ((await answer.json()) as { id: string }).id;
    await expect(page.getByRole('row').filter({ hasText: layoutName })).toHaveCount(1, { timeout: 15_000 });

    const trip = await asPerson<{ id: string }>(page, 'POST', '/api/v1/trip-logs/', tripBody(tripTitle, localDay(1)));
    tripId = trip.id;

    await gotoRoute(page, `/trip-logs/${trip.id}/report`);
    await expect(page.getByTestId('trip-report')).toBeVisible({ timeout: 30_000 });
    await chooseOption(page, page.getByTestId('trip-report-template'), layoutName);

    // A trip with a map sends its picture along by POST; one without is asked for by GET. Either
    // way the layout chosen travels in the query.
    const asked = page.waitForRequest(
      (request) => new RegExp(`/api/v1/trip-logs/${trip.id}/report(/download)?(\\?|$)`).test(request.url()),
    );
    const arriving = page.waitForEvent('download');
    await page.getByTestId('trip-report-download').click();
    expect(new URL((await asked).url()).searchParams.get('templateId')).toBe(layoutId);
    const file = await arriving;
    const zip = readFileSync(await file.path());
    const entry = zipEntries(zip).find((candidate) => candidate.name === 'word/document.xml');
    expect(entry, 'the download is a word-processor document').toBeTruthy();
    const text = zipEntryBytes(zip, entry!).toString('utf8').replace(/<[^>]+>/g, ' ');
    expect(text).toContain(marker);
    expect(text).toContain(tripTitle);
  } finally {
    if (tripId !== undefined) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    if (layoutId !== undefined) {
      await tryAsPerson(page, 'DELETE', `/api/v1/report-templates/${layoutId}`);
    }
  }
});
