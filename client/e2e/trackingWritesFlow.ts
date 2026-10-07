// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';
import { chooseOption, gotoRoute, login } from './helpers.ts';
import { apiJson, bearerToken } from './rastermapApi.ts';

/**
 * The three ways a coordinator writes a tracking log other than reporting live: correcting a report
 * in place — on a running watch and again once it is closed — importing a spreadsheet of reports,
 * from a file and from rows pasted in, and declaring what the cave's depths mean so that a report
 * can be made by the name of a place. And what follows from each: a sheet imported twice writes
 * nothing the second time, a sheet that corrects one cell changes that cell and nothing else, the
 * log can be narrowed to one person, and a report added once the watch is closed has to say when
 * it was made.
 *
 * <b>Why this is a browser flow and not only a component suite.</b> Each of these has a component
 * suite, and each suite stubs every write — which is how a sentence looked up with its arguments
 * swapped, a sample sheet downloaded without the caller's token and a deprecated prop spelling all
 * passed them. Here every write goes to a real server and every refusal would be a real one, while
 * the console sweep watches the whole walk.
 *
 * Everything is stood up by this run and taken down after it: a cave with the committed survey
 * fixture, a trip whose two people carry the invented names the sample sheet is written about, and
 * a watch armed on that survey. The sample sheet is the one the dialog itself offers, so the file
 * imported is the one a coordinator would start from.
 */

/** A station of the committed survey fixture, which the cave's declared place stands for. */
const DECLARED_STATION = 'p8.p8.98';
/** The declared place: the name the sample sheet reports by, at a depth of this run's choosing. */
const PLACE = { label: 'Meandru', depthM: 96 };
/** The two people the sample sheet is about — invented names, written into the sheet itself. */
const ION = 'Ion Popescu';
const MARIA = 'Maria Pop';

/**
 * A sheet the way a phone's spreadsheet hands it over: the date and the time in two columns, and
 * the time read off a clock somewhere else. Its one row is somebody going in, because a row has to
 * say a place or a standing to be a report at all.
 *
 * <b>The zone is one nobody running this is in, and one whose clocks never change.</b> A row read
 * on the reader's own clock and a row read in the reader's own zone are the same row, so a zone the
 * machine happens to keep would let the reading pass without ever being applied; and a zone with
 * summer time would make the instant below depend on the date chosen. 14:05 in Tokyo is 05:05 UTC
 * on every day of every year.
 */
const ZONED = {
  zone: 'Asia/Tokyo',
  note: 'E2E sheet kept on Tokyo clocks',
  rows: (person: string, note: string) =>
    ['Data,Ora,Speologi,Stare,Nota', `15.09.2026,14:05,${person},intrare,${note}`].join('\n'),
  /** The sheet's own wording of the moment, in the language this walk runs in. */
  shown: /9\/15\/2026, 2:05:00\sPM/,
  instant: Date.parse('2026-09-15T05:05:00Z'),
};

/**
 * A sheet that writes a time and no date, which cannot be placed until somebody says its day.
 *
 * The time is the first minute of the day on purpose: the day the dialog offers is the trip's own,
 * the trip is dated today, and midnight is the one time of today that is never still to come — a
 * report in the future would be refused, and for a reason that has nothing to do with this walk.
 */
const TIMES_ONLY = {
  note: 'E2E sheet of times with no dates',
  rows: (person: string, note: string) =>
    ['Ora,Speologi,Stare,Nota', `00:00,${person},intrare,${note}`].join('\n'),
};

/**
 * The one cell of the sample sheet this walk rewrites, and what it rewrites it to.
 *
 * No comma in the new wording: the sheet is comma-separated, and a comma here would turn one cell
 * into two and the walk into a test of quoting.
 */
const SHEET_NOTE = { written: 'apa mare in meandru', corrected: 'apa foarte mare in meandru' };
/** The line of the sample sheet that note is on, counted as the dialog counts: the header is 1. */
const SHEET_NOTE_LINE = 4;

/** What is typed on the closed watch, for a call nobody wrote down at the time. */
const LATE_NOTE = 'E2E written up after the watch was closed';

interface Report {
  id: string;
  caverId: string;
  teamId: string | null;
  kind: string;
  surveyModelId: string | null;
  stationName: string | null;
  depthEnteredM: number | null;
  note: string | null;
  recordedAt: string;
  corrected: boolean;
  outsideDeclaredParts: boolean;
}

export async function correctImportAndReportByPlace(page: Page) {
  const stamp = Date.now();
  const caveName = `E2E Tracking Writes Cave ${stamp}`;
  await login(page);

  // ---- A cave of this run's own, with the committed survey fixture on it ----
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

  // ---- What the cave's depths mean, declared on the cave's own page ----
  const places = page.getByTestId('cave-depth-places');
  await places.scrollIntoViewIfNeeded();
  await places.getByTestId('cave-depth-place-add').click();
  await places.getByTestId('cave-depth-place-depth').fill(String(PLACE.depthM));
  await places.getByTestId('cave-depth-place-station').fill(DECLARED_STATION);
  await places.getByTestId('cave-depth-place-label').fill(PLACE.label);
  await places.getByTestId('cave-depth-place-save').click();
  await expect(places.getByRole('cell', { name: PLACE.label })).toBeVisible({ timeout: 15_000 });

  const token = await bearerToken(page);
  const models = (await apiJson(page, token, 'GET', `/api/v1/caves/${caveId}/survey-models`)) as {
    id: string;
  }[];
  const modelId = models[0].id;

  // ---- A trip with the sheet's two people, its watch armed on the survey ----
  const participant = (name: string) => ({
    caverId: null, newCaverName: name, roleId: null, entryTime: null, exitTime: null, note: null,
  });
  const tripDate = new Date().toISOString().slice(0, 10);
  // Kept, because the roster is written whole: the same trip is sent again further down with one
  // person left out, and everything else about it has to be what was saved here.
  const tripBody = {
    title: `E2E tracking writes ${stamp}`,
    tripTypeId: null,
    tripDate,
    tripDateEnd: null, entryTime: null, exitTime: null,
    description: null, results: null, weatherConditions: null, locationText: null,
    organizingCavingGroupId: null, geom: null,
    caveIds: [caveId],
    participants: [participant(ION), participant(MARIA)],
    proposers: null, cavingGroupId: null, visibility: null,
    depthReachedM: null, lengthSurveyedM: null, surveyStations: null, ropeMetres: null,
    hadIncident: false, fieldData: null, logistics: null, safety: null,
    maxParticipants: null, meetingGeom: null,
  };
  const trip = (await apiJson(page, token, 'POST', '/api/v1/trip-logs', tripBody)) as {
    id: string;
    participants: { caverId: string; name: string }[];
  };
  const ion = trip.participants.find((p) => p.name === ION)!.caverId;
  const maria = trip.participants.find((p) => p.name === MARIA)!.caverId;

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
  // One report each, with notes that tell them apart: the correction dialog has to open on the
  // row that was pressed, and two rows that looked alike could not show that it did.
  const went = async (caverId: string, note: string) =>
    ((await apiJson(page, token, 'POST', `/api/v1/trip-logs/${trip.id}/tracking/events`, {
      caverIds: [caverId], kind: 'entered', stationName: null, depthM: null, teamId: null,
      note, recordedAt: null,
    })) as Report[])[0];
  const ionWent = await went(ion, 'E2E Ion went in');
  const mariaWent = await went(maria, 'E2E Maria went in');

  const logOf = async () =>
    (
      (await apiJson(
        page,
        token,
        'GET',
        `/api/v1/trip-logs/${trip.id}/tracking/events?page=1&pageSize=200`,
      )) as { items: Report[] }
    ).items;

  // Straight to the tab by its address, which is also how a phone reaches a tab its strip has
  // folded away. Not through `gotoRoute`, whose wait compares the path alone and would never see a
  // query string arrive.
  await page.goto(`/trip-logs/${trip.id}?tab=tracking`);
  await page.waitForURL((url) => url.pathname === `/trip-logs/${trip.id}`, { timeout: 60_000 });
  const log = page.getByTestId('trip-tracking-events');
  await expect(log).toContainText('E2E Maria went in', { timeout: 30_000 });

  // ---- Somebody just heard from is not marked as silent ----
  // The count is on screen, so the mark is in force on this watch and has a threshold; it counts
  // nobody, and neither person carries the tag. Asserted together: a tag that is absent because
  // the mark is switched off would prove nothing about a fresh report.
  const party = page.getByTestId('trip-tracking-participants');
  await expect(page.getByTestId('trip-tracking-count-quiet')).toHaveAttribute('data-quiet', '0');
  await expect(page.getByTestId('trip-tracking-standing-underground')).toHaveCount(2);
  await expect(page.getByTestId(`trip-tracking-quiet-${ion}`)).toHaveCount(0);
  await expect(page.getByTestId(`trip-tracking-quiet-${maria}`)).toHaveCount(0);

  // ---- Nobody the log speaks of is taken off a running watch ----
  // The trip's form sends its two lists as the whole roster, so this is what a save with Maria's
  // row deleted sends. She has gone in and the watch is running: the write is refused by name,
  // and she is still on the trip and on the watch.
  const asRostered = (caverId: string) => ({
    caverId, newCaverName: null, roleId: null, entryTime: null, exitTime: null, note: null,
  });
  const tripRead = await page.request.fetch(`/api/v1/trip-logs/${trip.id}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(tripRead.ok()).toBeTruthy();
  const tripTag = tripRead.headers()['etag'];
  const leaving = await page.request.fetch(`/api/v1/trip-logs/${trip.id}`, {
    method: 'PUT',
    headers: { Authorization: `Bearer ${token}`, ...(tripTag ? { 'If-Match': tripTag } : {}) },
    data: { ...tripBody, participants: [asRostered(ion)] },
  });
  expect(leaving.status()).toBe(400);
  expect(((await leaving.json()) as { code?: string }).code).toBe('trip_log.participant_tracked');
  // The same write with both people kept is taken, so what was refused was the departure and
  // not the shape of the request.
  await apiJson(
    page,
    token,
    'PUT',
    `/api/v1/trip-logs/${trip.id}`,
    { ...tripBody, participants: [asRostered(ion), asRostered(maria)] },
    tripTag ? { 'If-Match': tripTag } : {},
  );
  await page.reload();
  await expect(log).toContainText('E2E Maria went in', { timeout: 30_000 });
  await expect(party.getByRole('row', { name: new RegExp(MARIA) })).toBeVisible();
  await expect(page.getByTestId(`trip-tracking-off-roster-${maria}`)).toHaveCount(0);

  // ---- Correcting a report in place ----
  const correction = page.getByRole('dialog', { name: 'Correct this report' });
  const note = correction.getByTestId('trip-tracking-edit-note');

  // Opened on each row in turn, it carries that row's own report — not the last one it was filled
  // from.
  await page.getByTestId(`trip-tracking-event-edit-${ionWent.id}`).click();
  await expect(note).toHaveValue('E2E Ion went in');
  await page.keyboard.press('Escape');
  await expect(correction).toBeHidden();
  await page.getByTestId(`trip-tracking-event-edit-${mariaWent.id}`).click();
  await expect(note).toHaveValue('E2E Maria went in');

  // Escape leaves the report as it was: a change typed and abandoned is not written anywhere.
  await note.fill('E2E typed and abandoned');
  await page.keyboard.press('Escape');
  await expect(correction).toBeHidden();
  await page.getByTestId(`trip-tracking-event-edit-${mariaWent.id}`).click();
  await expect(note).toHaveValue('E2E Maria went in');

  await note.fill('E2E Maria went in, corrected');
  await correction.getByRole('button', { name: 'Save the correction' }).click();
  await expect(correction).toBeHidden();
  await expect(log).toContainText('E2E Maria went in, corrected');
  expect((await logOf()).find((row) => row.id === mariaWent.id)?.note).toBe(
    'E2E Maria went in, corrected',
  );
  // The log says which row no longer reads as it was first written — and says it of that row only.
  await expect(page.getByTestId(`trip-tracking-event-corrected-${mariaWent.id}`)).toBeVisible();
  await expect(page.getByTestId(`trip-tracking-event-corrected-${ionWent.id}`)).toHaveCount(0);

  // ---- Reporting by the name of a declared place ----
  await page
    .getByTestId('trip-tracking-participants')
    .getByRole('row', { name: new RegExp(ION) })
    .getByRole('checkbox')
    .check();
  await page.getByTestId('trip-tracking-kind').click();
  await page.locator('.ant-select-item-option[title="At a depth"]').click();
  // Found by its label: the chooser is the one field on the card the form does not hold, and it is
  // reached here the way a screen reader reaches it.
  await page.getByRole('combobox', { name: 'Place' }).click();
  await page
    .locator(`.ant-select-item-option[title="${PLACE.label} — ${PLACE.depthM} m"]`)
    .click();
  await expect(page.getByTestId('trip-tracking-depth')).toHaveValue(String(PLACE.depthM));
  await page.getByTestId('trip-tracking-record').click();
  await expect(page.getByText('Recorded for 1.')).toBeVisible({ timeout: 15_000 });
  // The server read the depth through the very declaration that was chosen, so the station on the
  // log is the one the cave declared rather than a second guess at it.
  await expect
    .poll(async () =>
      (await logOf()).find((row) => row.caverId === ion && row.kind === 'atDepth')?.stationName,
    )
    .toBe(DECLARED_STATION);

  // ---- Taking a report off the log, and putting it back ----
  // The report just made, because it is the one with the most to lose: a station, the depth that
  // was asked for, and the survey both were read on. Held whole, as the log answers it, so that
  // "back as it was" below is a comparison of every member rather than of the ones listed here.
  const placed = (await logOf()).find((row) => row.caverId === ion && row.kind === 'atDepth')!;
  const ionOnWatch = party.getByRole('row', { name: new RegExp(ION) });
  const binOf = page.getByTestId(`trip-tracking-event-delete-${placed.id}`);
  const removedFold = page.getByTestId('trip-tracking-removed');
  // The watch places Ion where that report said, and nothing has been taken off this log yet.
  await expect(ionOnWatch).toContainText(DECLARED_STATION, { timeout: 15_000 });
  await expect(removedFold).toHaveCount(0);

  const takeOff = async () => {
    await binOf.click();
    const asked = page.locator('.ant-popconfirm:visible');
    // The confirmation says where the report goes, and no longer that it is gone for good.
    await expect(asked).toContainText('can be put back');
    await expect(asked).not.toContainText('for good');
    await asked.getByRole('button', { name: 'OK' }).click();
    // Gone from the log on the screen, from the log as the server answers it, and from the fold of
    // the watch: Ion is no longer placed by a report that is not there.
    await expect(binOf).toHaveCount(0, { timeout: 15_000 });
    expect((await logOf()).some((row) => row.id === placed.id)).toBe(false);
    await expect(ionOnWatch).not.toContainText(DECLARED_STATION, { timeout: 15_000 });
  };
  const isBackAsItWas = async () => {
    await expect(binOf).toBeVisible({ timeout: 15_000 });
    // The same report under the same id, every member as it was — its place, its moment, and not
    // marked as corrected, since nobody corrected it.
    expect((await logOf()).find((row) => row.id === placed.id)).toEqual(placed);
    await expect(page.getByTestId(`trip-tracking-event-corrected-${placed.id}`)).toHaveCount(0);
    await expect(ionOnWatch).toContainText(DECLARED_STATION, { timeout: 15_000 });
  };

  // Once by the Undo on the notice that says it went…
  await takeOff();
  await page.getByTestId('trip-tracking-event-undo').click();
  await isBackAsItWas();
  await expect(removedFold).toHaveCount(0, { timeout: 15_000 });

  // …and once from the removed reports under the log, where it waits after the notice has gone.
  await takeOff();
  await expect(page.getByTestId('trip-tracking-removed-count')).toHaveText('Removed reports: 1', {
    timeout: 15_000,
  });
  await page.getByTestId('trip-tracking-removed-count').click();
  const waiting = page.getByTestId(`trip-tracking-removed-${placed.id}`);
  // Shown there with the place it had: the station and the depth that was asked for.
  await expect(waiting).toContainText(DECLARED_STATION);
  await expect(waiting).toContainText(`Reported as ${PLACE.depthM} m down`);
  // Destroying it is offered beside putting it back, as an act of its own. Not pressed here.
  await expect(
    page.getByTestId(`trip-tracking-removed-destroy-${placed.id}`),
  ).toHaveText('Delete for good');
  await page.getByTestId(`trip-tracking-removed-restore-${placed.id}`).click();
  await isBackAsItWas();
  // Nothing is left waiting, so the fold is gone with its last report.
  await expect(removedFold).toHaveCount(0, { timeout: 15_000 });

  // ---- Importing the sample sheet the dialog offers ----
  const beforeImport = await logOf();
  await page.getByTestId('trip-tracking-csv-open').click();
  const importing = page.getByRole('dialog', { name: 'Import reports from a spreadsheet' });
  await expect(importing).toBeVisible();
  const download = page.waitForEvent('download');
  await importing.getByTestId('trip-tracking-csv-template').click();
  const sample = await download;
  // Saved under a name a spreadsheet opens, and holding the sample rather than a refusal: fetched
  // without the caller's token, the same button saved a 401 problem document under a .csv name.
  expect(sample.suggestedFilename()).toBe('tracking-reports-sample.csv');
  const sampleBytes = readFileSync((await sample.path())!);
  expect(sampleBytes.toString('utf8')).toContain('Data si ora');

  await importing.locator('input[type="file"]').setInputFiles({
    name: sample.suggestedFilename(),
    mimeType: 'text/csv',
    buffer: sampleBytes,
  });
  await expect(importing.getByTestId('trip-tracking-csv-read-as')).toContainText('UTF-8');
  await importing.getByTestId('trip-tracking-csv-preview').click();
  const rows = importing.getByTestId('trip-tracking-csv-rows');
  await expect(rows).toBeVisible({ timeout: 15_000 });
  // The sample's dates are all twelve or under, so nothing in the file settles which is the day,
  // and the dialog says the reading is the usual one rather than one the file proved.
  await expect(importing.getByTestId('trip-tracking-csv-moments-rule')).toContainText(
    'the usual reading was used',
  );

  // The last line — both of them coming out — is left out by unticking it. It names two people,
  // so it is two rows here, and both go.
  const lastLine = importing.getByRole('checkbox', { name: 'Import line 8' });
  await expect(lastLine).toHaveCount(2);
  await lastLine.first().uncheck();
  await expect(lastLine.nth(1)).not.toBeChecked();

  await importing.getByTestId('trip-tracking-csv-commit').click();
  await expect(importing).toBeHidden({ timeout: 15_000 });
  await expect(log).toContainText('intrat in pestera', { timeout: 15_000 });
  // The line named by the declared place landed at the declared station.
  await expect(log).toContainText('apa mare in meandru');
  const afterImport = await logOf();
  expect(
    afterImport.find((row) => row.note === 'apa mare in meandru' && row.caverId === maria)
      ?.stationName,
  ).toBe(DECLARED_STATION);
  // …and the unticked line did not land for either of them.
  expect(afterImport.filter((row) => row.kind === 'exited')).toEqual([]);
  const landed = afterImport.length - beforeImport.length;
  expect(landed).toBeGreaterThan(0);

  /** Reads a sheet into the dialog and leaves its last line out, as the first import did. */
  const readSheet = async (bytes: Buffer) => {
    await page.getByTestId('trip-tracking-csv-open').click();
    await expect(importing).toBeVisible();
    await importing.locator('input[type="file"]').setInputFiles({
      name: sample.suggestedFilename(),
      mimeType: 'text/csv',
      buffer: bytes,
    });
    await importing.getByTestId('trip-tracking-csv-preview').click();
    await expect(rows).toBeVisible({ timeout: 15_000 });
    await lastLine.first().uncheck();
    await expect(lastLine.nth(1)).not.toBeChecked();
  };
  const overwrite = importing.getByRole('checkbox', {
    name: /Overwrite what the log already holds/,
  });
  const before = importing.getByTestId(`trip-tracking-csv-row-before-${SHEET_NOTE_LINE}`);
  const after = importing.getByTestId(`trip-tracking-csv-row-after-${SHEET_NOTE_LINE}`);

  // ---- The same sheet a second time ----
  // Every row left ticked is one the log already holds, and none of them is new. Counted from what
  // the first import actually wrote, so a sheet that landed fewer rows than it showed cannot pass
  // here by agreeing with itself.
  await readSheet(sampleBytes);
  await expect(importing.getByTestId('trip-tracking-csv-creates')).toHaveText(/\D0$/);
  await expect(importing.getByTestId('trip-tracking-csv-replaces')).toHaveText(
    new RegExp(`\\D${landed}$`),
  );
  // A row that would replace a report shows the report as it stands and as it would be left — the
  // place in the survey's word on one line, and in the sheet's word beside the station it became
  // on the other.
  await expect(before).toContainText('In the log now:');
  await expect(before).toContainText(DECLARED_STATION);
  await expect(before).toContainText(SHEET_NOTE.written);
  await expect(after).toContainText('After the import:');
  await expect(after).toContainText(`${PLACE.label} → ${DECLARED_STATION}`);
  await expect(after).toContainText(SHEET_NOTE.written);

  // Leave to overwrite is given, so nothing stands between the sheet and the log but what the
  // sheet says — and it says what the log says. The answer names every row as already so, and the
  // log is the same log: the same reports under the same ids, none added, none changed, and none
  // newly marked as corrected. Without the count in the answer, an import that was quietly turned
  // away would leave the log just as untouched.
  await overwrite.check();
  await importing.getByTestId('trip-tracking-csv-commit').click();
  await expect(
    page.getByText(`0 recorded, 0 corrected, ${landed} already as the sheet says, 2 left out.`),
  ).toBeVisible({ timeout: 15_000 });
  await expect(importing).toBeHidden({ timeout: 15_000 });
  expect(await logOf()).toEqual(afterImport);

  // ---- The sheet again, one cell corrected ----
  const meandru = afterImport.find(
    (row) => row.note === SHEET_NOTE.written && row.caverId === maria,
  )!;
  await readSheet(
    Buffer.from(
      sampleBytes.toString('utf8').replace(SHEET_NOTE.written, SHEET_NOTE.corrected),
      'utf8',
    ),
  );
  // The two lines now differ, and differ in the note alone.
  await expect(before).toContainText(`“${SHEET_NOTE.written}”`);
  await expect(before).not.toContainText(SHEET_NOTE.corrected);
  await expect(after).toContainText(`“${SHEET_NOTE.corrected}”`);
  await expect(after).not.toContainText(SHEET_NOTE.written);

  await overwrite.check();
  await importing.getByTestId('trip-tracking-csv-commit').click();
  await expect(
    page.getByText(
      `0 recorded, 1 corrected, ${landed - 1} already as the sheet says, 2 left out.`,
    ),
  ).toBeVisible({ timeout: 15_000 });
  await expect(importing).toBeHidden({ timeout: 15_000 });
  await expect(log).toContainText(SHEET_NOTE.corrected, { timeout: 15_000 });
  // One report changed, in the one thing the sheet changed about it, and it is the only one the
  // import marked: every other report is exactly as it was.
  expect(await logOf()).toEqual(
    afterImport.map((row) =>
      row.id === meandru.id ? { ...row, note: SHEET_NOTE.corrected, corrected: true } : row,
    ),
  );
  await expect(page.getByTestId(`trip-tracking-event-corrected-${meandru.id}`)).toBeVisible();
  // Proved on a report the same sheet named and left alone, in the same import.
  const untouched = afterImport.find(
    (row) => row.note === 'intrat in pestera' && row.caverId === ion,
  )!;
  await expect(page.getByTestId(`trip-tracking-event-corrected-${untouched.id}`)).toHaveCount(0);

  // ---- Pasted rows, the date and the time in two columns, read on another zone's clocks ----
  await page.getByTestId('trip-tracking-csv-open').click();
  await expect(importing).toBeVisible();
  await importing.getByTestId('trip-tracking-csv-source').getByText('Pasted rows').click();
  await importing.getByTestId('trip-tracking-csv-paste').fill(ZONED.rows(ION, ZONED.note));
  await expect(importing.getByTestId('trip-tracking-csv-pasted-as')).toContainText('Comma');

  // Whose clock the times are on is a file setting, behind its fold. Found by searching, the way
  // somebody finds one zone among hundreds, and matched by the end of its name so that the walk
  // reads the same on a machine that keeps this very zone and is offered it as its own.
  await importing.getByRole('button', { name: 'File settings' }).click();
  const zone = importing.getByRole('combobox', { name: "Sheet's time zone" });
  await zone.click();
  await zone.fill('Tokyo');
  await page.locator(`.ant-select-item-option[title$="${ZONED.zone}"]`).click();

  await importing.getByTestId('trip-tracking-csv-preview').click();
  await expect(rows).toBeVisible({ timeout: 15_000 });
  // The dialog names the zone the server says it applied…
  await expect(importing.getByTestId('trip-tracking-csv-moments-rule')).toContainText(
    `read on the clocks of ${ZONED.zone}`,
  );
  // …and the row is shown as the sheet wrote it — the day from one column and the hour from the
  // other, on that zone's clocks — so the preview can be checked against the paper line by line
  // whatever zone the reviewer's own machine keeps.
  await expect(rows).toContainText(ZONED.shown);

  await importing.getByTestId('trip-tracking-csv-commit').click();
  await expect(importing).toBeHidden({ timeout: 15_000 });
  await expect(log).toContainText(ZONED.note, { timeout: 15_000 });
  // What was stored is the instant those clocks showed that time at, not the time as written.
  const zoned = (await logOf()).find((row) => row.note === ZONED.note && row.caverId === ion);
  expect(zoned).toBeTruthy();
  expect(Date.parse(zoned!.recordedAt)).toBe(ZONED.instant);

  // ---- A sheet of times with no dates: the dialog asks for its day and offers the trip's ----
  await page.getByTestId('trip-tracking-csv-open').click();
  await expect(importing).toBeVisible();
  await importing.getByTestId('trip-tracking-csv-source').getByText('Pasted rows').click();
  await importing
    .getByTestId('trip-tracking-csv-paste')
    .fill(TIMES_ONLY.rows(ION, TIMES_ONLY.note));
  const askedForDay = importing.getByTestId('trip-tracking-csv-day-ask');
  // Not asked before the sheet has been read: a sheet that writes its dates is never asked.
  await expect(askedForDay).toBeHidden();
  await importing.getByTestId('trip-tracking-csv-preview').click();
  // The first reading is refused, and the question it raises is on the screen with the trip's own
  // date already in the field — read from the real trip, in the form a date field takes.
  await expect(askedForDay).toBeVisible({ timeout: 15_000 });
  await expect(askedForDay).toContainText('This sheet writes times with no dates');
  await expect(importing.getByTestId('trip-tracking-csv-day')).toHaveValue(tripDate);
  await expect(importing.getByTestId('trip-tracking-csv-moments-day')).toBeHidden();

  // Read again with the day that was offered: the row is placed, and the dialog says on which day.
  await importing.getByTestId('trip-tracking-csv-preview').click();
  await expect(importing.getByTestId('trip-tracking-csv-moments-day')).toContainText(
    'Times with no date were put on',
    { timeout: 15_000 },
  );
  await expect(importing.getByTestId('trip-tracking-csv-creates')).toContainText('1');

  await importing.getByTestId('trip-tracking-csv-commit').click();
  await expect(importing).toBeHidden({ timeout: 15_000 });
  await expect(log).toContainText(TIMES_ONLY.note, { timeout: 15_000 });
  // What was stored is that day at that time, exactly as written: no zone was named for this sheet.
  const timed = (await logOf()).find((row) => row.note === TIMES_ONLY.note && row.caverId === ion);
  expect(timed).toBeTruthy();
  expect(Date.parse(timed!.recordedAt)).toBe(Date.parse(`${tripDate}T00:00:00Z`));

  // ---- One person's reports ----
  // The log is everybody's, newest first, and the report to put right is nearly always one
  // person's. Both people have a report with their name in its note, so the narrowed log is shown
  // to hold the one and to have dropped the other — and to give it back when the choice is cleared.
  const whose = page.getByTestId('trip-tracking-events-caver');
  await chooseOption(page, whose, MARIA);
  await expect(log).not.toContainText('E2E Ion went in', { timeout: 15_000 });
  await expect(log).toContainText('E2E Maria went in, corrected');
  await whose.locator('.ant-select-clear').click();
  await expect(log).toContainText('E2E Ion went in', { timeout: 15_000 });

  // ---- Correcting a report once the watch is closed ----
  // A trip is written up after everybody is out, so a closed log takes a correction exactly as a
  // running one does. Closed here the way a coordinator closes it, from the card, so the tab under
  // test is the one a closed watch actually draws rather than one that was told so by the API.
  await page.getByTestId('trip-tracking-close').click();
  await page.locator('.ant-popconfirm:visible').getByRole('button', { name: 'OK' }).click();
  await expect(page.getByTestId('trip-tracking-state')).toContainText('Tracking closed', {
    timeout: 15_000,
  });
  // The report card stays, and now says the trip is being written up afterwards: the moment is
  // asked for outright, and "now" — which on a finished trip is never when anything was said — is
  // not among the answers.
  await expect(page.getByTestId('trip-tracking-record-after')).toBeVisible();
  await expect(page.getByTestId('trip-tracking-recorded-at')).toBeVisible();
  await expect(page.getByTestId('trip-tracking-when-now')).toHaveCount(0);

  // A call nobody wrote down at the time. Sent with no moment it is refused on the page, with a
  // sentence saying what is wanted…
  await page
    .getByTestId('trip-tracking-participants')
    .getByRole('row', { name: new RegExp(MARIA) })
    .getByRole('checkbox')
    .check();
  await page.getByTestId('trip-tracking-kind').click();
  await page.locator('.ant-select-item-option[title="Note"]').click();
  await page.getByTestId('trip-tracking-note').fill(LATE_NOTE);
  await page.getByTestId('trip-tracking-record').click();
  await expect(page.getByText(/Say when this was said/)).toBeVisible();

  // …and with one it lands at that moment. Three hours back, worked out on the page's own clock
  // and typed the way the field shows a moment, so the instant expected is the instant typed
  // whatever zone this browser keeps — and far enough from the present that a report stamped
  // "now" could not be mistaken for it.
  const late = await page.evaluate(() => {
    const at = new Date(Date.now() - 3 * 60 * 60 * 1000);
    at.setSeconds(0, 0);
    const two = (value: number) => String(value).padStart(2, '0');
    return {
      typed:
        `${at.getFullYear()}-${two(at.getMonth() + 1)}-${two(at.getDate())} ` +
        `${two(at.getHours())}:${two(at.getMinutes())}:00`,
      instant: at.getTime(),
    };
  });
  const saidAt = page.getByTestId('trip-tracking-recorded-at');
  await saidAt.click();
  await saidAt.fill(late.typed);
  await saidAt.press('Enter');
  await expect(saidAt).toHaveValue(late.typed);
  await page.getByTestId('trip-tracking-record').click();
  await expect(page.getByText('Recorded for 1.')).toBeVisible({ timeout: 15_000 });
  await expect(log).toContainText(LATE_NOTE, { timeout: 15_000 });
  // Once, and at the moment named: the press that was refused wrote nothing.
  const writtenUp = (await logOf()).filter((row) => row.note === LATE_NOTE);
  expect(writtenUp).toHaveLength(1);
  expect(Date.parse(writtenUp[0].recordedAt)).toBe(late.instant);

  await page.getByTestId(`trip-tracking-event-edit-${ionWent.id}`).click();
  await expect(note).toHaveValue('E2E Ion went in');
  await note.fill('E2E Ion went in, corrected after closing');
  await correction.getByRole('button', { name: 'Save the correction' }).click();
  await expect(correction).toBeHidden();
  await expect(log).toContainText('E2E Ion went in, corrected after closing');
  expect((await logOf()).find((row) => row.id === ionWent.id)?.note).toBe(
    'E2E Ion went in, corrected after closing',
  );

  // Off the page before its rows go, so its own polling never reads a deleted trip and files a 404
  // with the console guard.
  await gotoRoute(page, '/trip-logs');
  await apiJson(page, token, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
  await apiJson(page, token, 'DELETE', `/api/v1/survey-models/${modelId}`);
  await apiJson(page, token, 'DELETE', `/api/v1/caves/${caveId}`);
}
