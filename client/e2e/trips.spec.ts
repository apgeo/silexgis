// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { uniquePng } from './png.ts';

/**
 * A trip written up the way a caver writes one up: several days long, with a shape drawn on a
 * map of its own to say roughly where the party was, and a note on that shape saying it is not
 * approximated for anybody.
 *
 * Every flow here brings its own subject. A run stamps the title of the trip it creates and
 * deletes it again at the end, so nothing depends on the database being empty, two runs never
 * contend for the same row, and a leftover from an aborted run cannot make a later run pass.
 */

/** A calendar day the picker's own format, built locally so it means the same day the app reads. */
function day(offsetDays: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${`${date.getDate()}`.padStart(2, '0')}`;
}

/**
 * Types both ends of the trip's date range.
 *
 * Typed rather than clicked out of the calendar: a range that crosses a month boundary needs the
 * panel paged, and which cell is where is a fact about today's date rather than about the trip.
 * Each end is committed with Enter, which is also what moves the picker on to the next one.
 */
async function fillRange(page: Page, start: string, end: string) {
  const from = page.getByPlaceholder('Start date');
  const to = page.getByPlaceholder('End date');
  await from.click();
  await from.fill(start);
  await page.keyboard.press('Enter');
  await to.fill(end);
  await page.keyboard.press('Enter');
  await expect(from).toHaveValue(start);
  await expect(to).toHaveValue(end);
}

/** Opens one of the trip report's sections by its collapse header. */
async function openSection(page: Page, name: string) {
  // By role, not by text: the change history names the same sections in its own list, so a
  // plain text match would be ambiguous the moment a trip has been edited once.
  await page.getByRole('button', { name: new RegExp(`${name}$`) }).click();
}

/** Draws the trip's one shape as a single point on the form's embedded map. */
async function drawPoint(page: Page) {
  const map = page.getByTestId('trip-geometry-map');
  // The map is built once the dialog's open transition has put the container in the document,
  // so the canvas appearing is the signal that there is something to draw on.
  await expect(map.locator('canvas')).toBeVisible({ timeout: 30_000 });
  // Anchored at the end: each shape button carries its icon's label ahead of its own word.
  await page.getByRole('button', { name: /Point$/ }).click();
  await map.click({ position: { x: 220, y: 120 } });
  // A shape can only be cleared once one exists, so the control enabling is the drawing landing.
  await expect(page.getByRole('button', { name: 'Clear shape' })).toBeEnabled();
}

/**
 * Deleting a trip from its own page leaves the page refetching the trip it just removed, and a
 * browser writes a console error for every request that answered 404 however well the page
 * handled it. Declared with its reason rather than left to the console sweep, which would
 * otherwise report the flow doing exactly what it was written to do.
 */
function allowDeletedTripRefetch(consoleErrors: { allow: (p: RegExp, reason: string) => void }) {
  consoleErrors.allow(
    /status of 404/,
    'this flow deletes its own trip from the trip page, which refetches it once on the way out',
  );
}

/**
 * Picks an existing thing in the record-a-link dialog by typing part of its name.
 *
 * Typed rather than scrolled to: the picker asks the server as the reader types, and which
 * things are within reach is a fact about the installation's data rather than about the trip.
 * Addressed by test id rather than by the row's own text, because the picker draws its results
 * as a list of rows whose visible words are the thing's name and its kind, and the kind is what
 * two different installations are least likely to agree on.
 */
async function pickExistingItem(dialog: Locator, name: string, query: string) {
  await dialog.getByTestId('reslink-target-picker-input').fill(query);
  const row = dialog.getByTestId('reslink-target-picker-row').filter({ hasText: name });
  await expect(row.first()).toBeVisible({ timeout: 15_000 });
  await row.first().click();
}

/** One role's field on the trip page, addressed by the role it stands for. */
function roleField(page: Page, code: string): Locator {
  return page.getByTestId(`role-field-${code}`);
}

test('a trip records what it worked in, and the record survives a reload and can be struck out', async ({
  page,
  consoleErrors,
}) => {
  const title = `E2E Role Trip ${Date.now()}`;
  allowDeletedTripRefetch(consoleErrors);
  await login(page);

  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  const tripUrl = page.url();

  // A trip that has recorded nothing still offers the two fields a report is expected to
  // state, and does not open onto a wall of the other eight.
  await expect(page.getByText('What this trip did')).toBeVisible();
  const workAreas = roleField(page, 'trip-work-area');
  await expect(workAreas.getByText('Work areas')).toBeVisible();
  await expect(roleField(page, 'trip-surveyed')).toHaveCount(0);

  // Recording a work area is one act in the field itself: the role is not a question the
  // dialog asks, because the field it was opened from is the answer.
  await workAreas.getByText('Add', { exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('combobox', { name: 'Relation' })).toHaveCount(0);
  await pickExistingItem(dialog, 'Falia Demo', 'Falia');
  await dialog.getByRole('button', { name: 'OK' }).click();

  await expect(workAreas.getByText('Falia Demo')).toBeVisible({ timeout: 15_000 });

  // And it appears once. The general links card below shows everything else the trip is tied
  // to, but not the roles — those have a field each, saying what was done as well as where, so
  // a bare chip repeating one underneath is a duplicate a reader cannot recognise as one. The
  // trip is tied to nothing else, so the card has nothing to head at all.
  await expect(page.getByText(/^Linked items \(/)).toHaveCount(0);

  // The role is a link on the server, not a thing the page was holding: it comes back the
  // same on a page that was loaded fresh.
  await page.goto(tripUrl);
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  const reloaded = roleField(page, 'trip-work-area');
  await expect(reloaded.getByText('Falia Demo')).toBeVisible({ timeout: 15_000 });

  // Striking a chip out removes that one membership from the link it belongs to, leaving the
  // field standing and ready to record another. It asks first: the act is a hard delete of
  // somebody's record of what the trip did, on a target the size of a close icon.
  await reloaded.getByLabel('Remove from link').click();
  // Scoped to the popover, and waited out afterwards. Its OK is an ordinary button that also
  // answers the page-wide "OK" the trip's own delete asks for a few lines below, and antd fades
  // the popover rather than removing it at once — so an unscoped click there resolves to this
  // button while it is detaching, and fails as unstable rather than confirming anything.
  const removeConfirm = page.locator('.ant-popover:visible');
  await removeConfirm.getByRole('button', { name: 'OK' }).click();
  await expect(page.locator('.ant-popover:visible')).toHaveCount(0, { timeout: 15_000 });
  await expect(reloaded.getByText('Falia Demo')).toHaveCount(0, { timeout: 15_000 });
  await expect(reloaded.getByText('Work areas')).toBeVisible();

  // Removing a membership invalidates every role field's query at once, so the page refetches a
  // handful of them. A confirmation opened while those are landing is unmounted under the click,
  // and the flow times out on a button that keeps detaching — so wait for the page to go quiet
  // first, and answer inside the popover that asked.
  await page.waitForLoadState('networkidle');

  // Then check this is still the trip's own page before asking anything to delete itself.
  // A chip names a feature and is a link to it, so a click that lands beside the close icon
  // navigates; "Delete" then means the feature's delete, and the confirmation says something
  // else while looking the same. That has already removed a shared demo feature twice, and it
  // is silent — the run fails later, somewhere unrelated, and the data is simply gone.
  await expect(page).toHaveURL(tripUrl);
  await expect(page.getByRole('heading', { name: title })).toBeVisible();

  await page.getByRole('button', { name: /Delete/ }).click();
  const confirm = page.locator('.ant-popover:visible');
  await expect(confirm.getByText('Delete this trip log?')).toBeVisible();
  await confirm.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.').first()).toBeVisible({ timeout: 15_000 });
});

test('a trip spans several days, carries a shape of its own, and says the shape is exact', async ({
  page,
  consoleErrors,
}) => {
  const title = `E2E Trip ${Date.now()}`;
  const start = day(0);
  const end = day(2);
  allowDeletedTripRefetch(consoleErrors);
  await login(page);

  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  await fillRange(page, start, end);
  await drawPoint(page);
  await page.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  const tripUrl = page.url();

  // The label itself is the assertion that the trip was stored as spanning days: a trip that ran
  // on one day is labelled "Date", and only a trip with an end date past its start reads "Dates".
  await expect(page.getByText('Dates', { exact: true })).toBeVisible();
  await expect(page.getByText(/\d.+ – .+\d/)).toBeVisible();

  // The shape survives the round trip through the server rather than only the form's own state.
  await page.goto(tripUrl);
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Trip sketch')).toBeVisible();
  const sketchMap = page.getByTestId('trip-geometry-map');
  await expect(sketchMap.locator('canvas')).toBeVisible({ timeout: 30_000 });

  // A reader who meets the shape is told what it is: exact for everyone who may read the trip,
  // whatever the caves it names are protected by.
  await page.getByTestId('trip-geometry-warning').click();
  await expect(page.getByText(/never approximated/)).toBeVisible();
  await expect(page.getByText(/discloses that entrance/)).toBeVisible();

  await page.getByRole('button', { name: /Delete/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.').first()).toBeVisible({ timeout: 15_000 });
});

test('a trip is written as a draft and stays one until it is published', async ({
  page,
  consoleErrors,
}) => {
  const title = `E2E Draft Trip ${Date.now()}`;
  allowDeletedTripRefetch(consoleErrors);
  await login(page);

  // Creating a trip no longer announces it. Whoever is named on it hears about it when the
  // write-up is ready and not before, so a new trip arrives as a draft with nothing sent.
  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  const tripUrl = page.url();
  await expect(page.getByTestId('trip-state')).toHaveText('Draft');
  // Said in words as well, so the author is not left to read a grey tag.
  await expect(page.getByTestId('trip-draft-notice')).toBeVisible();

  // Publishing is confirmed first: it is the moment the people on the trip are told.
  await page.getByRole('button', { name: /Publish$/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByTestId('trip-state')).toHaveText('Published', { timeout: 15_000 });
  await expect(page.getByTestId('trip-draft-notice')).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Publish$/ })).toHaveCount(0);

  // The state is a stored fact about the row, not a thing the page was holding: it survives a
  // reload, and the list shows the same answer the detail page does.
  await page.goto(tripUrl);
  await expect(page.getByTestId('trip-state')).toHaveText('Published', { timeout: 15_000 });

  // And the reverse takes it back for more work without pretending the announcement never
  // happened — the date it first went out is kept.
  await page.getByRole('button', { name: /Back to draft$/ }).click();
  await expect(page.getByTestId('trip-state')).toHaveText('Draft', { timeout: 15_000 });
  await expect(page.getByText('Published on')).toBeVisible();

  await page.getByRole('button', { name: /Delete/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.').first()).toBeVisible({ timeout: 15_000 });
});

test('a trip logged without touching the date control is a day trip today', async ({
  page,
  consoleErrors,
}) => {
  const title = `E2E Day Trip ${Date.now()}`;
  allowDeletedTripRefetch(consoleErrors);
  await login(page);

  // The create form pre-fills both ends of the range with today, so a trip can still be written
  // up by naming it and nothing else — and it is stored as one day rather than as a range of
  // itself, which is what makes the detail page label it "Date".
  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Date', { exact: true })).toBeVisible();
  await expect(page.getByText('Dates', { exact: true })).toHaveCount(0);
  // Nothing was drawn, so the trip carries no sketch and the page shows no map at all.
  await expect(page.getByTestId('trip-geometry-map')).toHaveCount(0);

  await page.getByRole('button', { name: /Delete/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.').first()).toBeVisible({ timeout: 15_000 });
});

test('a photograph attached to a trip appears in the trip’s own gallery, and the person on it is counted', async ({
  page,
  consoleErrors,
}) => {
  const stamp = `${Date.now()}`;
  const title = `E2E Gallery Trip ${stamp}`;
  const person = `E2E Counted ${stamp}`;
  allowDeletedTripRefetch(consoleErrors);
  await login(page);

  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  // One person, named for this run only, so the totals below are about a caver whose whole
  // history is the trip this flow just wrote — an assertion of exactly one, rather than of
  // "more than before", which would pass on a page that had stopped filtering entirely.
  await page.getByRole('button', { name: /Add participant/ }).click();
  await page.getByPlaceholder('Participant name').first().fill(person);
  await page.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  const tripUrl = page.url();

  // Nothing is filed against the trip yet, and the section says so rather than drawing an
  // empty grid that looks like something failed to load.
  const gallery = page.getByTestId('trip-gallery');
  await expect(gallery).toContainText('No photographs are filed against this trip yet.');

  // ---- a photograph, attached to the trip through the trip's own attachments
  // Pixels unique to the run: the archive refuses content it already holds, so a fixed fixture
  // would be accepted the first time the suite ever ran and refused every time after.
  // Waited on the write rather than on the confirmation, and on the SECOND of the two writes
  // the drop makes: the bytes are stored first and tied to the trip afterwards, so leaving the
  // page when the upload answers abandons the request that makes it the trip's picture — and
  // the toast says the same word an earlier save may still be showing, so it cannot stand in
  // for either of them.
  const attached = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' && /\/api\/v1\/attachments(\?|$)/.test(response.url()),
  );
  await page
    .locator('.ant-upload input[type=file]')
    .last()
    .setInputFiles({ name: `trip-${stamp}.png`, mimeType: 'image/png', buffer: uniquePng() });
  expect((await attached).status()).toBe(201);

  // ---- and it is the trip's photograph, on a page loaded fresh
  await page.goto(tripUrl);
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  const tiles = page.getByTestId('trip-gallery').getByTestId('photo-tile');
  await expect(tiles).toHaveCount(1, { timeout: 30_000 });
  // A rendering, never the upload: a trip page must not hand out the original bytes of a
  // picture whose subject the reader may not be allowed to place.
  await expect(tiles.locator('img')).toHaveAttribute('src', /\/thumbnail\?/);
  // The section says whose photographs these are, for the same reason the totals below do.
  await expect(page.getByTestId('trip-gallery')).toContainText('The photographs you may see');

  // ---- the person on it, counted across the trips this reader may read
  await gotoRoute(page, '/cavers');
  await page.getByPlaceholder('Search by name…').fill(person);
  const row = page.getByRole('row', { name: new RegExp(person) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByRole('button', { name: 'Statistics' }).click();

  const totals = page.getByTestId('trip-statistics');
  await expect(totals).toBeVisible({ timeout: 15_000 });
  // Nothing is stored: this figure is worked out from the trip written a moment ago, and this
  // person went on exactly one. Matched on the tile's own heading rather than on the word
  // anywhere inside it — "Trips with an incident" is a second tile that also says "Trips".
  const tripsTile = totals.locator('.ant-statistic').filter({
    has: page.locator('.ant-statistic-title', { hasText: /^Trips$/ }),
  });
  await expect(tripsTile).toContainText('1', { timeout: 15_000 });
  // The sentence that stops two colleagues comparing screens from filing the difference as a
  // bug — and stops the repair being to take the filter off.
  await expect(totals).toContainText('Counted over the trips you may read');

  await page.goto(tripUrl);
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  await page.getByRole('button', { name: /Delete/ }).click();
  const confirm = page.locator('.ant-popover:visible');
  await confirm.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.').first()).toBeVisible({ timeout: 15_000 });
});

test('what a trip measured and what it found are stored on it, not held by the page', async ({
  page,
  consoleErrors,
}) => {
  const title = `E2E Report ${Date.now()}`;
  allowDeletedTripRefetch(consoleErrors);
  await login(page);

  // A purpose is what carries the questions: the three sections are drawn from schemas the
  // trip's purpose holds, so a trip with no purpose has nothing to be asked.
  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByLabel('Trip type').click();
  await page.getByTitle('Survey / mapping').click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });

  // A counted fact: a column on the trip, so a club can count it across trips rather than dig
  // it out of prose.
  await openSection(page, 'Measured');
  await page.getByTestId('trip-measure-depthReachedM').fill('218');
  await page.getByTestId('trip-section-save-measured').click();
  await expect(page.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });

  // And a value in a section whose field this client never named: the form is built from the
  // purpose's schema, so what is asked for is the installation's decision.
  await openSection(page, 'Field data');
  await page.getByTestId('trip-section-field-instrument').fill('DistoX2');
  // Waited for on the write itself rather than on the confirmation: the save a few lines above
  // puts the same word on screen for a few seconds, so matching that text again can match the
  // earlier save's and let the reload below cancel this one while it is still in flight.
  const written = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && /\/api\/v1\/trip-logs\//.test(response.url()),
  );
  await page.getByTestId('trip-section-save-fieldData').click();
  expect((await written).status()).toBe(200);

  // Both survive a reload, which is the whole claim: they are on the row, not in the page.
  await page.reload();
  await expect(page.getByTestId('trip-depth-reached')).toContainText('218', { timeout: 15_000 });
  await openSection(page, 'Field data');
  await expect(page.getByTestId('trip-section-field-instrument')).toHaveValue('DistoX2');

  await page.getByRole('button', { name: /Delete/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.').first()).toBeVisible({ timeout: 15_000 });
});

test('a trip records who was there, what one of them did, and when they came out', async ({
  page,
  consoleErrors,
}) => {
  const title = `E2E Roster Trip ${Date.now()}`;
  allowDeletedTripRefetch(consoleErrors);
  await login(page);

  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);

  // Two people, and the ordinary one is a name and nothing else — the row asks for nothing
  // more, which is the bar this control has to keep: most rows are exactly this.
  await page.getByRole('button', { name: /Add participant/ }).click();
  await page.getByPlaceholder('Participant name').first().fill('E2E Roster One');
  await page.getByRole('button', { name: /Add participant/ }).click();
  await page.getByPlaceholder('Participant name').nth(1).fill('E2E Roster Two');

  // The second did a job and came out on her own schedule. Both are behind the row's own
  // control rather than in front of everybody: opening it is what the flow has to do because
  // it is what a person has to do.
  await page.getByRole('button', { name: 'Role, times and note' }).nth(1).click();
  // Scoped to the row that was opened: the form draws several selects and two times of its
  // own, and the party's hours are a different question from this person's.
  const details = page.getByTestId('roster-row-details');
  await details.getByRole('combobox').click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option[title="Leader"]').click();
  const exit = details.getByPlaceholder('Exit time');
  await exit.fill('18:45');
  await page.keyboard.press('Enter');
  await expect(exit).toHaveValue('18:45');
  // The time panel carries an OK of its own, which answers to the same name as the dialog's and
  // sits above it. Moving on to the note is what dismisses it, and the flow waits for that.
  await details.getByPlaceholder('Note').click();
  await expect(page.locator('.ant-picker-dropdown:visible')).toHaveCount(0);
  // Why the hours read the way they do. It is not a job, and it has to be readable by somebody
  // who cannot open this form at all — which is what the assertion after the reload checks.
  await details.getByPlaceholder('Note').fill('turned back at the pitch head');

  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  const tripUrl = page.url();

  // Loaded fresh, because the claim is about rows on the server and not about what the form
  // was still holding. Having simply been there is what the roster already says, so the plain
  // attendee is a bare name; the other carries the job and the hour that set her apart.
  await page.goto(tripUrl);
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  const roster = page.locator('.ant-tag');
  await expect(roster.filter({ hasText: 'E2E Roster One' })).toHaveText('E2E Roster One');
  await expect(roster.filter({ hasText: 'E2E Roster Two' })).toContainText('Leader');
  await expect(roster.filter({ hasText: 'E2E Roster Two' })).toContainText('18:45');
  // Read from the page itself, not from a hover: the note is the explanation of the hours, and
  // a reader without the edit button must be able to see it.
  await expect(page.getByTestId('roster-note')).toHaveText('turned back at the pitch head');

  await page.getByRole('button', { name: /Delete/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.').first()).toBeVisible({ timeout: 15_000 });
});
