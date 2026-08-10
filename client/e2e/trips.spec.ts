// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
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
