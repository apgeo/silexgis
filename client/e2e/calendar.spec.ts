// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The record of everything dated, driven the way somebody reads it.
 *
 * The window is required by the answer, so the first thing worth proving is that arriving at the
 * page with nothing in mind still produces one and still asks for it — a page that opened with no
 * window would be refused and would look, to whoever opened it, like a calendar that is broken.
 *
 * The toggles are the second thing. Every one of them is a display decision: what they narrow
 * away stays exactly as readable as it was on its own page, and none of them is ever a
 * permission. What is checked here is that they reach the server as the words the answer knows,
 * and that a pair of them asking for no kind of record at all says so instead of asking.
 */

/** What the page asked for on its last request, read off the wire. */
async function askedFor(page: import('@playwright/test').Page): Promise<URLSearchParams> {
  const request = await page.waitForRequest(
    (r) => r.url().includes('/api/v1/calendar?'),
    { timeout: 30_000 },
  );
  return new URL(request.url()).searchParams;
}

test('the calendar opens on a window of its own and asks for both ends of it', async ({ page }) => {
  await login(page);

  const asked = askedFor(page);
  await gotoRoute(page, '/calendar');
  const query = await asked;

  expect(query.get('from')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  expect(query.get('to')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  // Both families are on, so no family is named.
  expect(query.get('source')).toBeNull();
  await expect(page.getByRole('heading', { name: 'Calendar' })).toBeVisible();
});

test('the sidebar offers the calendar and lands on it', async ({ page }) => {
  await login(page);

  await page.getByRole('menuitem', { name: 'Calendar' }).click();
  await page.waitForURL((url) => url.pathname === '/calendar', { timeout: 60_000 });
  await expect(page.getByRole('heading', { name: 'Calendar' })).toBeVisible();
});

test('turning a family off narrows the question rather than the reader', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/calendar');
  await expect(page.getByTestId('calendar-toggle-trips')).toBeVisible();

  const asked = askedFor(page);
  await page.getByTestId('calendar-toggle-other').click();
  expect((await asked).get('source')).toBe('tripLog');

  // The other way round names every family that is not a trip, one by one. "Not the trips" is
  // more than one family, and a narrowing that could only name one would drop the family it left
  // out of a record that says nothing is missing.
  await page.getByTestId('calendar-toggle-other').click();
  const askedForTheRest = askedFor(page);
  await page.getByTestId('calendar-toggle-trips').click();
  expect((await askedForTheRest).get('source')?.split(',').sort()).toEqual([
    'event',
    'expedition',
  ]);

  // Neither family wanted is not a question the answer can be asked, so nothing is asked.
  await page.getByTestId('calendar-toggle-other').click();
  await expect(page.getByTestId('calendar-empty')).toBeVisible();
});

test('a row clicks through to the record it came from', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/calendar');

  const rows = page.locator('.ant-table-tbody tr.ant-table-row');
  const empty = page.getByTestId('calendar-empty');
  // Wait for the answer to land before counting anything. Until it does the table is in its
  // loading state and holds neither a row nor the empty placeholder, so a count taken then is
  // zero for the one reason that says nothing at all about what the window holds — and the
  // branch below would then assert an empty record against a page that is merely still asking.
  await expect(rows.or(empty).first()).toBeVisible({ timeout: 60_000 });
  // The seeded data may hold nothing in the opening window, and an empty record is a correct
  // answer rather than a failure — so this proves the click-through only where there is a row.
  if ((await rows.count()) === 0) {
    await expect(empty).toBeVisible();
    return;
  }

  await rows.first().click();
  await page.waitForURL(/\/(trip-logs|expeditions|events)\/[0-9a-f-]{36}$/, { timeout: 60_000 });
});

/**
 * An event is answered the way a trip is, and the answering is reached from the event's own page.
 *
 * This is the flow the whole mechanism exists for and the only place a person meets it: a table
 * two subjects share is worth nothing until somebody can open one of them and say they are coming.
 * It is driven end to end — the event is written, its page is opened, somebody is asked, and what
 * the server concluded about the limit is read back off the page — because every step between the
 * form and the list is a place the wrong subject could be named without anything failing.
 */
test('an event says who is coming, and a deadline is not asked', async ({ page }) => {
  const title = `E2E Event ${Date.now()}`;
  const deadlineTitle = `E2E Deadline ${Date.now()}`;
  await login(page);

  // Typed rather than clicked out of the calendar panel: which cell is where is a fact about
  // today's date rather than about the event, and Enter is also what moves the picker along.
  const fillDay = async (value: string) => {
    // Named by the form's own field rather than by the placeholder: the list behind the dialog
    // filters by date too, so a placeholder matches two controls on this page.
    const from = page.getByTestId('event-form').getByPlaceholder('Start date');
    await from.click();
    await from.fill(value);
    await page.keyboard.press('Enter');
    await expect(from).toHaveValue(value);
  };
  const today = new Date();
  const day = `${today.getFullYear()}-${`${today.getMonth() + 1}`.padStart(2, '0')}-${`${today.getDate()}`.padStart(2, '0')}`;

  await gotoRoute(page, '/events');
  await page.getByTestId('event-create').click();
  await page.getByTestId('event-title').fill(title);
  // The kind the form opens on is one people come to, so nothing is chosen here.
  await fillDay(day);
  // How many places it has. Left empty an event turns nobody away; a number is what makes the
  // people past it a waiting list, and what the line above the list counts up to.
  await page.getByTestId('event-max-participants').fill('2');
  await page.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByTestId('event-title')).toHaveText(title, { timeout: 15_000 });
  await page.getByRole('tab', { name: 'Who is coming', exact: true }).click();
  await expect(page.getByTestId('event-invitations-limit')).toHaveText(
    '0 of 2 places taken, 0 waiting.',
  );

  // Chosen with the keyboard rather than clicked: the suggestion list commits on mousedown, and a
  // click that straddles a re-render loses the choice with the list left open.
  const picker = page.getByTestId('event-invite-name');
  await picker.click();
  await picker.fill('Ana Demo');
  // The suggestions are fetched, so they are not on screen the moment the text is, and waiting for
  // the option itself is the only thing that says they have arrived. Pressing before then costs
  // more than a lost keystroke: with no option under it the highlight moves over nothing, and the
  // text box reads Enter as "ask" — so the form asks with nobody chosen, is correctly refused for
  // naming nobody, and the failure lands much later on an empty list.
  //
  // The value read back afterwards cannot stand in for this. The text is whatever was typed
  // whether or not anybody was chosen, so it says the same thing in both cases; what distinguishes
  // them is the option going active, which is asserted before the key that depends on it.
  const suggestion = page.locator('.ant-select-item-option').filter({ hasText: 'Ana Demo' }).first();
  await expect(suggestion).toBeVisible({ timeout: 15_000 });
  await page.keyboard.press('ArrowDown');
  await expect(suggestion).toHaveClass(/ant-select-item-option-active/);
  await page.keyboard.press('Enter');
  await expect(picker).toHaveValue('Ana Demo');
  await page.getByTestId('event-invite').click();

  const list = page.getByTestId('event-invitations');
  await expect(list.getByText('Ana Demo')).toBeVisible({ timeout: 15_000 });
  // Nothing said yet is not a "no", and the row says so in words rather than in the stored token
  // — twice over, on the tag beside the name and in the control that would change it, which is
  // why this takes the first of the two rather than asserting there is only one.
  await expect(list.getByText('Not answered').first()).toBeVisible();
  // An event keeps no list of who turned up, so nothing here turns the answers into one.
  await expect(page.getByTestId('event-invitations-promote')).toHaveCount(0);

  // Nobody comes to a deadline, so a deadline is not asked who is coming — and the tab is not
  // offered rather than offered and refused.
  await gotoRoute(page, '/events');
  await page.getByTestId('event-create').click();
  await page.getByTestId('event-title').fill(deadlineTitle);
  const kind = page.getByTestId('event-kind');
  await kind.click();
  // Chosen with the keyboard for the same reason the person above is, and the cost of losing this
  // one is worse: the option list commits on mousedown, so a click straddling a re-render selects
  // nothing and the form keeps the kind it opened on — an ordinary event under a title saying
  // "deadline". Nothing fails there. What fails is the assertion below, which then reads as this
  // page failing to hide a tab rather than as the kind never having been chosen.
  const deadlineOption = page.locator('.ant-select-item-option').filter({ hasText: 'Deadline' });
  await expect(deadlineOption).toBeVisible({ timeout: 15_000 });
  // Walked to rather than counted to. Which row a kind sits on is a fact about the vocabulary, and
  // a fixed number of presses would silently choose its neighbour the day a kind is added.
  for (let step = 0; step < 12; step++) {
    const active = await deadlineOption.evaluate((el) =>
      el.classList.contains('ant-select-item-option-active'),
    );
    if (active) break;
    await page.keyboard.press('ArrowDown');
  }
  await expect(deadlineOption).toHaveClass(/ant-select-item-option-active/);
  await page.keyboard.press('Enter');
  // Read back, so a lost choice fails here at its cause rather than downstream on the tab.
  await expect(kind).toContainText('Deadline');
  await fillDay(day);
  await page.getByRole('button', { name: 'OK' }).click();

  await expect(page.getByTestId('event-title')).toHaveText(deadlineTitle, { timeout: 15_000 });
  await expect(page.getByRole('tab', { name: 'Who is coming', exact: true })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'History', exact: true })).toBeVisible();
});

/**
 * A club date that comes round again is written as the evenings it is.
 *
 * Driven end to end because the whole design rests on what happens after the form closes: the run
 * is materialised as ordinary events, so each occurrence must be a page of its own that the
 * existing machinery opens, and the only account of why the same evening appears repeatedly is the
 * sentence its author wrote. Both halves are checked here — that more than one evening was really
 * written, and that opening one says it is part of a run and offers the two ways of calling it off.
 */
test('a repeating event is written as the evenings it is, and calling one off asks which', async ({
  page,
}) => {
  const title = `E2E Series ${Date.now()}`;
  await login(page);

  const fillDay = async (value: string) => {
    const from = page.getByTestId('event-form').getByPlaceholder('Start date');
    await from.click();
    await from.fill(value);
    await page.keyboard.press('Enter');
    await expect(from).toHaveValue(value);
  };
  const today = new Date();
  const day = `${today.getFullYear()}-${`${today.getMonth() + 1}`.padStart(2, '0')}-${`${today.getDate()}`.padStart(2, '0')}`;

  await gotoRoute(page, '/events');
  await page.getByTestId('event-create').click();
  await page.getByTestId('event-title').fill(title);
  await fillDay(day);

  // The repetition is asked for only while the event is being written: from the moment the run
  // exists it is ordinary events, each edited as itself.
  await page.getByTestId('event-repeats').click();
  await expect(page.getByTestId('event-repeat-rule')).toBeVisible();
  // The words and the repetition are two different fields. This sentence is for a person and
  // nothing parses it; the frequency beside it is stepped by once, now, and is not stored at all.
  await page.getByTestId('event-repeat-rule').fill('every week, while the season lasts');
  await page.getByTestId('event-repeat-count').fill('3');
  await page.getByRole('button', { name: 'OK' }).click();

  // The create answers with the first occurrence, so the page that opens is an ordinary event page.
  await expect(page.getByTestId('event-title')).toHaveText(title, { timeout: 15_000 });
  await expect(page.getByTestId('event-series-banner')).toBeVisible();
  await expect(page.getByTestId('event-series-banner')).toContainText(
    'every week, while the season lasts',
  );

  // The other occurrences are ordinary events on the ordinary list, found by the ordinary search —
  // which is the whole claim of the design: nothing between the form and this list knows a series
  // exists.
  await gotoRoute(page, '/events');
  // The test id sits on the search control's wrapper rather than on its field, so the box inside
  // it is what takes the text.
  await page.getByTestId('event-search').locator('input').fill(title);
  const rows = page.getByTestId('event-table').getByText(title, { exact: true });
  await expect(rows).toHaveCount(3, { timeout: 15_000 });

  // Calling off an occurrence of a run has two outcomes, so it is asked in a dialog with the
  // choice in it rather than in the yes/no popover a single event gets.
  await rows.first().click();
  await expect(page.getByTestId('event-series-banner')).toBeVisible({ timeout: 15_000 });
  await page.getByTestId('event-delete').click();
  await expect(page.getByTestId('event-delete-scope')).toBeVisible();
  await expect(page.getByTestId('event-delete-scope-following')).toBeVisible();
  // What is kept is said before anything is chosen: an occurrence that has already begun is the
  // record of an evening that happened, and is never removed by an act aimed at the rest of the run.
  await expect(page.getByText(/already begun are kept/)).toBeVisible();
});
