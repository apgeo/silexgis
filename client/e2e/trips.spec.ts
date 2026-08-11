// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

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

  await page.getByRole('button', { name: /Delete/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
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
