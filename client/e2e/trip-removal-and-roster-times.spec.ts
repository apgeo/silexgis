// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { surveyRead, tripBody } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';

/**
 * Two acts a coordinator performs on a tracked trip once the party is out, each against a real
 * server: taking the roster's entry and exit times from the tracking log, and removing what the
 * log says about somebody who has asked to be removed.
 *
 * <b>Why a browser walk.</b> Each has a component suite with its writes stubbed, and the second
 * act starts from a refusal: the people page has to read the trips out of a real refused delete,
 * open the list on them, and send the removal for the trip the reader pressed. A stub answers
 * whatever the test wrote; only a server can say that the report taken off the log earlier and
 * kept is really gone with the rest.
 *
 * Everything is stood up by this run — a cave with the committed survey fixture, a trip of three
 * invented people, a watch armed on that survey, and a log in which two came out and one's exit
 * was taken off the log again — and the trip, the survey and the cave are taken down after it.
 */

/**
 * The zone the roster's times are read on: one nobody running this is in, and one whose clocks
 * never change. A reading on the reader's own clock and a reading in the reader's own zone are the
 * same reading, so a zone the machine happens to keep would let the review pass without the zone
 * ever having been applied.
 */
const ZONE = { name: 'Asia/Tokyo', wentIn: '11:00', cameOut: '14:00' };

test('roster times are taken from the log after a look, and a person held by a trip has their reports removed from the refusal', async ({
  page,
  consoleErrors,
}) => {
  test.setTimeout(180_000);
  consoleErrors.allow(
    /Failed to load resource.*400/,
    'the refused delete of a person a trip still holds, which this walk asks for on purpose',
  );

  const stamp = Date.now();
  const caveName = `E2E Removal Cave ${stamp}`;
  const tripTitle = `E2E removal and roster times ${stamp}`;
  const names = { ana: `E2E Ana ${stamp}`, bogdan: `E2E Bogdan ${stamp}`, cristi: `E2E Cristi ${stamp}` };
  await login(page);

  // A cave of this run's own with the committed survey fixture on it: a watch is armed on a survey.
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())?.[1];
  expect(caveId).toBeTruthy();

  await page.getByRole('button', { name: 'Upload model' }).click();
  const uploadModal = page.getByRole('dialog');
  await expect(uploadModal.locator('.ant-upload-drag')).toBeVisible({ timeout: 15_000 });
  const chooser = page.waitForEvent('filechooser');
  await uploadModal.locator('.ant-upload-drag').click();
  await (await chooser).setFiles('e2e/fixtures/P8_Master.3d');
  await uploadModal.getByRole('button', { name: 'Upload model' }).click();
  await expect(page.getByText('Survex .3d')).toBeVisible({ timeout: 30_000 });

  const token = await bearerToken(page);
  const models = (await apiJson(page, token, 'GET', `/api/v1/caves/${caveId}/survey-models`)) as {
    id: string;
  }[];
  const modelId = models[0].id;
  await surveyRead(page, modelId);

  // Yesterday, by the calendar the server keeps: a report may not be dated ahead of now, and the
  // hours below are then in the past whatever time of day this runs at. 02:00 and 05:00 UTC are
  // 11:00 and 14:00 of the same day in the zone the review is made on.
  const day = new Date(stamp - 86_400_000).toISOString().slice(0, 10);
  const at = (clock: string) => `${day}T${clock}:00Z`;
  const person = (name: string, entryTime: string | null = null, exitTime: string | null = null) => ({
    caverId: null, newCaverName: name, roleId: null, entryTime, exitTime, note: null,
  });
  const trip = (await apiJson(
    page,
    token,
    'POST',
    '/api/v1/trip-logs',
    tripBody(tripTitle, day, {
      caveIds: [caveId],
      // Bogdan's times were typed on the trip's form: the review must not replace them unasked.
      participants: [
        person(names.ana),
        person(names.bogdan, '01:30:00', '04:30:00'),
        person(names.cristi),
      ],
    }),
  )) as { id: string; participants: { caverId: string; name: string }[] };
  const idOf = (name: string) => trip.participants.find((p) => p.name === name)!.caverId;
  const ana = idOf(names.ana);
  const bogdan = idOf(names.bogdan);
  const cristi = idOf(names.cristi);

  const watchRead = await page.request.fetch(`/api/v1/trip-logs/${trip.id}/tracking`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(watchRead.ok()).toBeTruthy();
  const watchTag = watchRead.headers()['etag'];
  await apiJson(
    page,
    token,
    'PUT',
    `/api/v1/trip-logs/${trip.id}/tracking`,
    { state: 'armed', surveyModelId: modelId, referenceStationName: null, depthFilter: [] },
    watchTag ? { 'If-Match': watchTag } : {},
  );
  const report = async (caverIds: string[], kind: string, clock: string) =>
    (await apiJson(page, token, 'POST', `/api/v1/trip-logs/${trip.id}/tracking/events`, {
      caverIds, kind, stationName: null, depthM: null, teamId: null, note: null,
      recordedAt: at(clock),
    })) as { id: string; caverId: string }[];
  const wentIn = await report([ana, bogdan, cristi], 'entered', '02:00');
  await report([ana, bogdan], 'exited', '05:00');
  const cristiOut = (await report([cristi], 'exited', '05:10'))[0];
  // Cristi's exit is taken off the log and kept — the state a removal on request has to reach
  // past: one report of his on the log, one waiting under 'Removed reports'.
  await apiJson(page, token, 'DELETE', `/api/v1/trip-logs/${trip.id}/tracking/events/${cristiOut.id}`);
  const onLog = (caverId: string) => wentIn.find((row) => row.caverId === caverId)!.id;

  const logOf = async () =>
    (
      (await apiJson(
        page,
        token,
        'GET',
        `/api/v1/trip-logs/${trip.id}/tracking/events?page=1&pageSize=100`,
      )) as { items: { caverId: string }[] }
    ).items;
  const keptOf = async () =>
    (
      (await apiJson(
        page,
        token,
        'GET',
        `/api/v1/trip-logs/${trip.id}/tracking/events/removed?page=1&pageSize=100`,
      )) as { items: { report: { caverId: string } }[] }
    ).items.map((row) => row.report);
  const reportsAbout = (rows: { caverId: string }[], caverId: string) =>
    rows.filter((row) => row.caverId === caverId).length;

  // What the server holds before either act — the numbers the end of this walk is read against.
  expect(reportsAbout(await logOf(), cristi)).toBe(1);
  expect(reportsAbout(await keptOf(), cristi)).toBe(1);

  // ---- The tracking tab as it stands before either act ----
  await gotoRoute(page, `/trip-logs/${trip.id}`);
  await page.getByRole('tab', { name: 'Tracking' }).click();
  await expect(page.getByTestId(`trip-tracking-event-delete-${onLog(cristi)}`)).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.getByTestId('trip-tracking-removed-count')).toHaveText('Removed reports: 1', {
    timeout: 15_000,
  });

  // ---- Roster times from the log: proposed, looked at, then written ----
  await page.getByTestId('trip-tracking-roster-times-open').click();
  const review = page.getByRole('dialog', { name: 'Entry and exit times from the tracking log' });
  await expect(review).toBeVisible();
  const zone = review.getByRole('combobox', { name: "Time zone of the roster's times" });
  await zone.click();
  await zone.fill('Tokyo');
  await page.locator(`.ant-select-item-option[title$="${ZONE.name}"]`).click();
  await expect(review.getByTestId('roster-times-zone-applied')).toContainText(ZONE.name, {
    timeout: 15_000,
  });

  // Ana: the log's two readings on that zone's clocks, nothing of her own on the roster, ticked.
  await expect(review.getByTestId(`roster-times-log-${ana}`)).toHaveText(
    `${ZONE.wentIn} – ${ZONE.cameOut}`,
  );
  await expect(review.getByTestId(`roster-times-take-${ana}`)).toBeChecked();
  // Bogdan: the same readings, but somebody typed his times — said so, and left unticked.
  await expect(review.getByTestId(`roster-times-log-${bogdan}`)).toHaveText(
    `${ZONE.wentIn} – ${ZONE.cameOut}`,
  );
  await expect(review.getByTestId(`roster-times-current-${bogdan}`)).toHaveText('01:30 – 04:30');
  await expect(review.getByTestId(`roster-times-overwrites-${bogdan}`)).toBeVisible();
  await expect(review.getByTestId(`roster-times-take-${bogdan}`)).not.toBeChecked();
  // Cristi: his exit was taken off the log, and a removed report says nothing — no exit to take.
  await expect(review.getByTestId(`roster-times-problem-${cristi}`)).toHaveText(
    'No report says they came out.',
  );
  await expect(review.getByTestId(`roster-times-take-${cristi}`)).toBeDisabled();

  const write = page.getByTestId('roster-times-write');
  await expect(write).toHaveText('Write to the roster: 1');
  await write.click();
  await expect(page.getByText('Times written to the roster. People: 1')).toBeVisible({
    timeout: 15_000,
  });
  await expect(review).not.toBeVisible();

  // What the server now holds: Ana's row carries the readings, Bogdan's typed times stand.
  const rosterNow = (
    (await apiJson(page, token, 'GET', `/api/v1/trip-logs/${trip.id}`)) as {
      participants: { caverId: string; entryTime: string | null; exitTime: string | null }[];
    }
  ).participants;
  const rowOf = (caverId: string) => rosterNow.find((row) => row.caverId === caverId)!;
  expect(rowOf(ana).entryTime).toMatch(new RegExp(`^${ZONE.wentIn}`));
  expect(rowOf(ana).exitTime).toMatch(new RegExp(`^${ZONE.cameOut}`));
  expect(rowOf(bogdan).entryTime).toMatch(/^01:30/);
  expect(rowOf(bogdan).exitTime).toMatch(/^04:30/);
  expect(rowOf(cristi).entryTime).toBeNull();

  // ---- The people page refuses to remove Cristi, and says which trip holds him ----
  await gotoRoute(page, '/cavers');
  await page.getByPlaceholder('Search by name…').fill(names.cristi);
  // The list is asked again a moment after the typing stops, and its rows are taken down while the
  // answer is on its way — a confirmation opened on the list as it was goes with them. So the
  // narrowed list is waited for: this run made three people, and only one row means it has arrived.
  await expect(page.locator('.ant-table-tbody tr.ant-table-row')).toHaveCount(1, { timeout: 15_000 });
  await page.getByTestId(`caver-delete-${cristi}`).click();
  await page.locator('.ant-popconfirm:visible .ant-btn-primary').click();

  const held = page.getByTestId('caver-held-by-trips');
  await expect(held).toBeVisible({ timeout: 15_000 });
  const heldTrip = held.getByTestId(`caver-held-trip-${trip.id}`);
  await expect(heldTrip).toContainText(tripTitle);
  await expect(held.getByTestId(`caver-held-roster-${trip.id}`)).toBeVisible();
  // Both of his reports are counted — the one on the log and the one kept after it was taken off.
  await expect(held.getByTestId(`caver-held-reports-${trip.id}`)).toHaveText('Tracking reports: 2');
  // Nothing offers to remove the person while the list still names something.
  await expect(page.getByTestId('caver-held-delete')).toHaveCount(0);

  // ---- His reports are removed from the list itself, after a confirmation that says what goes ----
  await held.getByTestId(`caver-held-remove-${trip.id}`).click();
  const confirmation = page.getByTestId('remove-reports-of-body');
  await expect(confirmation).toContainText('It cannot be undone');
  await expect(confirmation).toContainText("their name on the trip's roster");
  await page.getByTestId('remove-reports-of-confirm').click();
  await expect(page.getByText('Reports removed for good: 2')).toBeVisible({ timeout: 15_000 });
  // The trip is still listed, for the one thing that still holds him there: his name on its roster.
  await expect(held.getByTestId(`caver-held-reports-${trip.id}`)).toHaveCount(0);
  await expect(held.getByTestId(`caver-held-roster-${trip.id}`)).toBeVisible();
  await expect(page.getByTestId('caver-held-delete')).toHaveCount(0);

  // The server agrees, and nobody else's reports went with his.
  const log = await logOf();
  const kept = await keptOf();
  expect(reportsAbout(log, cristi)).toBe(0);
  expect(reportsAbout(kept, cristi)).toBe(0);
  expect(reportsAbout(log, ana)).toBe(2);
  expect(reportsAbout(log, bogdan)).toBe(2);

  // ---- Back on the trip: gone from the log, and gone from 'Removed reports' ----
  await gotoRoute(page, `/trip-logs/${trip.id}`);
  await page.getByRole('tab', { name: 'Tracking' }).click();
  // The log has been read — Ana's report is on it — before anything is said to be absent from it.
  await expect(page.getByTestId(`trip-tracking-event-delete-${onLog(ana)}`)).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.getByTestId(`trip-tracking-event-delete-${onLog(cristi)}`)).toHaveCount(0);
  // The fold that said "Removed reports: 1" at the start of this walk has nothing left to list.
  // Absence alone would also be what a fold still loading shows, which is why the server was
  // asked the same question above and answered none.
  await expect(page.getByTestId('trip-tracking-removed')).toHaveCount(0);
  // He is still one of the party: the removal took his reports, not his place on the roster.
  await expect(page.getByTestId(`trip-tracking-person-actions-${cristi}`)).toBeVisible();

  // Off the page before its rows go, so the page's own polling never reads a deleted trip.
  await gotoRoute(page, '/trip-logs');
  await apiJson(page, token, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
  await apiJson(page, token, 'DELETE', `/api/v1/survey-models/${modelId}`);
  await apiJson(page, token, 'DELETE', `/api/v1/caves/${caveId}`);
});
