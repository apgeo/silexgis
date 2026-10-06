// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, localDay, tripBody, tryAsPerson, versionOf } from './arrange.ts';
import { chooseOption, gotoRoute, login } from './helpers.ts';

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

/**
 * What the page asks the calendar for next, read off the wire.
 *
 * `about` names the request a step is waiting for when the one before it may not have gone out
 * yet. The page redraws after the address has changed, and the router may defer that redraw, so
 * a request caused by the previous click can leave the browser after the next step has begun to
 * listen — and "the next request" would then be the previous step's. Naming what the awaited
 * request is about lets that late one go by; what is asserted afterwards is everything else the
 * request says.
 */
async function askedFor(
  page: import('@playwright/test').Page,
  about: (query: URLSearchParams) => boolean = () => true,
): Promise<URLSearchParams> {
  const request = await page.waitForRequest(
    (r) => r.url().includes('/api/v1/calendar?') && about(new URL(r.url()).searchParams),
    { timeout: 30_000 },
  );
  return new URL(request.url()).searchParams;
}

/**
 * A request that names its families and does not name this one — which is how a step tells the
 * request its own click caused from a late one caused by the click before, whose families still
 * held the one this step turned off.
 */
const without = (family: string) => (query: URLSearchParams): boolean =>
  query.has('source') && !(query.get('source') ?? '').split(',').includes(family);

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

  // The calendar is filed in the rail's Activity group, which stays shut until somebody opens it
  // or arrives on a page inside it — and signing in arrives on the map, which is not.
  await page.getByRole('menuitem', { name: /Activity$/ }).click();
  await page.getByRole('menuitem', { name: 'Calendar' }).click();
  await page.waitForURL((url) => url.pathname === '/calendar', { timeout: 60_000 });
  await expect(page.getByRole('heading', { name: 'Calendar' })).toBeVisible();
});

test('turning a family off narrows the question rather than the reader', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/calendar');
  await expect(page.getByTestId('calendar-toggle-trips')).toBeVisible();

  // Each family has a toggle of its own, and what is left is named family by family.
  const asked = askedFor(page, without('expedition'));
  await page.getByTestId('calendar-toggle-camps').click();
  expect((await asked).get('source')).toBe('tripLog,event');
  const askedForTrips = askedFor(page, without('event'));
  await page.getByTestId('calendar-toggle-events').click();
  expect((await askedForTrips).get('source')).toBe('tripLog');

  // The other way round names every family that is not a trip, one by one. "Not the trips" is
  // more than one family, and a narrowing that could only name one would drop the family it left
  // out of a record that says nothing is missing.
  await page.getByTestId('calendar-toggle-camps').click();
  await page.getByTestId('calendar-toggle-events').click();
  const askedForTheRest = askedFor(page, without('tripLog'));
  await page.getByTestId('calendar-toggle-trips').click();
  expect((await askedForTheRest).get('source')?.split(',').sort()).toEqual([
    'event',
    'expedition',
  ]);

  // No family wanted is not a question the answer can be asked, so nothing is asked — and the
  // page says which of the two kinds of emptiness this is. It is held in the address like every
  // other choice, so it is still what the page shows after a reload.
  await page.getByTestId('calendar-toggle-camps').click();
  await page.getByTestId('calendar-toggle-events').click();
  await expect(page.getByTestId('calendar-empty')).toContainText('No kind of record is selected');
  await expect(page).toHaveURL(/[?&]source=none(&|$)/);
});

test('a row clicks through to the record it came from', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/calendar');

  // The rows that are records. The list also draws lines that are not — a heading over a group
  // of rows, the line marking today — and those lead nowhere.
  const rows = page.locator('.ant-table-tbody tr.calendar-row-entry');
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

/*
 * The same window of days read four more ways: as a month of days, a week of columns, a year of
 * months and an agenda read forwards. Each is driven the way somebody uses it — chosen, moved
 * through a period at a time, and left by opening a record from where it is drawn.
 *
 * Every record read here is written by the run that reads it, through the API, on days chosen
 * relative to today, and carries the run's stamp in its title: the grids draw everything in their
 * window, so a record is found by its own title in its own day and never by being the only one
 * there. What the readings are proved against is where a record is drawn and what the page asks
 * the server for — the window of each period, read off the wire, because a grid drawn over a
 * window that does not cover it would show empty days that are not empty.
 */

/** A club date written for this run, kept so the run can remove it again. */
interface Written {
  id: string;
  title: string;
}

/**
 * Writes one club event through the API, as the signed-in person, and starts organising it.
 *
 * An event is written as a draft, and a draft is somebody's workshop rather than a date anybody is
 * keeping, so the calendar leaves it off. Organising it is what puts it on: the state it is moved
 * to here is the first one a record is shown in as something coming up.
 */
async function writeEvent(
  page: Page,
  written: Written[],
  title: string,
  startDate: string,
  extra: Record<string, unknown> = {},
): Promise<Written> {
  const event = await asPerson<{ id: string }>(page, 'POST', '/api/v1/events', {
    title,
    kind: 'clubMeeting',
    startDate,
    endDate: null,
    startTime: null,
    endTime: null,
    place: null,
    maxParticipants: null,
    description: null,
    visibility: 'authenticated',
    cavingGroupId: null,
    ...extra,
  });
  const version = await versionOf(page, `/api/v1/events/${event.id}`);
  await asPerson(
    page,
    'POST',
    `/api/v1/events/${event.id}/state`,
    { state: 'planned' },
    { 'If-Match': version },
  );
  const row = { id: event.id, title };
  written.push(row);
  return row;
}

async function removeEvents(page: Page, written: Written[]) {
  for (const row of written) {
    await tryAsPerson(page, 'DELETE', `/api/v1/events/${row.id}`);
  }
}

/** Chooses one of the page's readings from the switch above it. */
async function chooseView(page: Page, name: 'Record' | 'Month' | 'Week' | 'Year' | 'Agenda') {
  const views = page.getByTestId('calendar-view');
  await views.getByText(name, { exact: true }).click();
  await expect(views.locator('.ant-segmented-item-selected')).toHaveText(name);
}

/** The window a request asked for, as the pair of days it named. */
async function windowAsked(asked: Promise<URLSearchParams>): Promise<[string | null, string | null]> {
  const query = await asked;
  return [query.get('from'), query.get('to')];
}

/**
 * A record's chip inside one day of whichever grid is on screen, found by what it is called.
 *
 * By its name and not by its words: a day a record only passes through draws its rail and no
 * words at all, so there is nothing in it to read, while every chip is still named — for a reader
 * who cannot see the drawing — by its kind, its title and which of its days it is.
 */
function chipIn(day: Locator, title: string): Locator {
  return day.getByRole('button', { name: title });
}

/** One property of the rail a chip draws along itself, read off the browser's own layout. */
function railOf(chip: Locator, property: 'borderTopWidth' | 'width'): Promise<string> {
  return chip.evaluate((el, name) => getComputedStyle(el, '::after')[name], property);
}

/** Opens an event from wherever it is drawn and checks the page it lands on is that event's. */
async function opensEvent(page: Page, from: Locator, event: Written) {
  await from.click();
  await page.waitForURL((url) => url.pathname === `/events/${event.id}`, { timeout: 60_000 });
  await expect(page.getByTestId('event-title')).toHaveText(event.title, { timeout: 15_000 });
}

/**
 * Moves an open select's highlight one step and takes it, by keyboard.
 *
 * The calendar's own month and year pickers are lists that commit on mousedown, and a click that
 * straddles a re-render loses the choice with the list left open; the highlight is asserted before
 * the key that depends on it, so a lost step fails here rather than as a missing record later.
 */
async function stepSelect(page: Page, select: Locator, key: 'ArrowDown' | 'ArrowUp', label: string) {
  await select.click();
  // The list is open, on the value it holds, before any key moves anything.
  await expect(
    page.locator('.ant-select-dropdown:visible .ant-select-item-option-selected'),
  ).toBeVisible();
  const option = page
    .locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: new RegExp(`^${label}$`) });
  await page.keyboard.press(key);
  await expect(option).toHaveClass(/ant-select-item-option-active/);
  await page.keyboard.press('Enter');
  await expect(select).toContainText(label);
}

test('the month reading draws a record on its day, moves a month at a time, and opens a record from its day', async ({
  page,
}) => {
  const stamp = Date.now();
  const written: Written[] = [];
  await login(page);

  const today = new Date();
  // The neighbouring month the header can reach without changing year: forward, except in
  // December, where the month picker's next step is the year's own first month.
  const forward = today.getMonth() < 11;
  const target = new Date(today.getFullYear(), today.getMonth() + (forward ? 1 : -1), 1);
  // A day of that month the current month's grid does not draw and its window does not reach, so
  // the record can only be on screen once the page has asked for the month it is in.
  const elsewhere = new Date(target.getFullYear(), target.getMonth(), forward ? 20 : 1);

  try {
    const tonight = await writeEvent(page, written, `E2E Month Tonight ${stamp}`, localDay(0), {
      startTime: '19:00:00',
    });
    const later = await writeEvent(
      page,
      written,
      `E2E Month Elsewhere ${stamp}`,
      localDay(0, elsewhere),
    );

    await gotoRoute(page, '/calendar');
    await expect(page.getByTestId('calendar-toggle-trips')).toBeVisible({ timeout: 30_000 });

    // A month's grid asks for the month it shows and a fortnight either side, so the days of the
    // neighbouring months drawn in its corners are answered too — not the window the record used.
    const monthStart = new Date(today.getFullYear(), today.getMonth(), 1);
    const monthEnd = new Date(today.getFullYear(), today.getMonth() + 1, 0);
    const asked = askedFor(page);
    await chooseView(page, 'Month');
    expect(await windowAsked(asked)).toEqual([localDay(-14, monthStart), localDay(14, monthEnd)]);
    await expect(page.getByTestId('calendar-grid')).toBeVisible();
    // The window control belongs to the readings that are lists; a grid's window is its month.
    await expect(page.getByTestId('calendar-window')).toHaveCount(0);

    // Tonight's meeting sits in today's cell, with the time it starts in front of its title.
    const chip = chipIn(page.getByTestId(`calendar-day-${localDay(0)}`), tonight.title);
    await expect(chip).toBeVisible({ timeout: 30_000 });
    await expect(chip).toHaveText(`19:00 ${tonight.title}`);
    await expect(chip).toHaveAttribute('data-source', 'event');
    await expect(page.getByTestId('calendar-chip').filter({ hasText: later.title })).toHaveCount(0);

    // A month at a time, through the grid's own month picker.
    const targetStart = new Date(target.getFullYear(), target.getMonth(), 1);
    const targetEnd = new Date(target.getFullYear(), target.getMonth() + 1, 0);
    const movedTo = askedFor(page);
    await stepSelect(
      page,
      page.locator('.ant-picker-calendar-month-select'),
      forward ? 'ArrowDown' : 'ArrowUp',
      target.toLocaleString('en', { month: 'short' }),
    );
    expect(await windowAsked(movedTo)).toEqual([
      localDay(-14, targetStart),
      localDay(14, targetEnd),
    ]);

    const elsewhereChip = chipIn(
      page.getByTestId(`calendar-day-${localDay(0, elsewhere)}`),
      later.title,
    );
    await expect(elsewhereChip).toBeVisible({ timeout: 30_000 });
    // A record that claims no time of day is drawn as its title alone.
    await expect(elsewhereChip).toHaveText(later.title);

    await opensEvent(page, elsewhereChip, later);
  } finally {
    await removeEvents(page, written);
  }
});

test('the week reading stands the days side by side, steps a week at a time, and opens a record from its day', async ({
  page,
}) => {
  const stamp = Date.now();
  const written: Written[] = [];
  await login(page);

  // The page reads English, whose week begins on a Sunday: the strip and the window it asks for
  // are that week, worked out by the application's date library rather than by this test.
  const today = new Date();
  const sunday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - today.getDay());
  const monday = localDay(1, sunday);
  const tuesday = localDay(2, sunday);
  const nextWednesday = localDay(10, sunday);

  try {
    // Two days long, so it stands in both columns and is marked where it begins and where it ends.
    const twoDays = await writeEvent(page, written, `E2E Week Weekend ${stamp}`, monday, {
      endDate: tuesday,
      startTime: '18:00:00',
      kind: 'training',
    });
    const nextWeek = await writeEvent(page, written, `E2E Week Next ${stamp}`, nextWednesday);

    await gotoRoute(page, '/calendar');
    await expect(page.getByTestId('calendar-toggle-trips')).toBeVisible({ timeout: 30_000 });

    const asked = askedFor(page);
    await chooseView(page, 'Week');
    expect(await windowAsked(asked)).toEqual([localDay(0, sunday), localDay(6, sunday)]);
    await expect(page.getByTestId('calendar-week')).toBeVisible();
    for (let offset = 0; offset < 7; offset++) {
      await expect(page.getByTestId(`calendar-week-day-${localDay(offset, sunday)}`)).toBeVisible();
    }

    // In both of its columns, and one record across them. It is named and timed where it begins
    // and nowhere else — it began at six once, not at six on each day — and the day it ends on
    // draws its rail and no words, because a title in both columns reads as two evenings that
    // happen to share one.
    const first = chipIn(page.getByTestId(`calendar-week-day-${monday}`), twoDays.title);
    const last = chipIn(page.getByTestId(`calendar-week-day-${tuesday}`), twoDays.title);
    await expect(first).toBeVisible({ timeout: 30_000 });
    await expect(first).toHaveAttribute('data-span', 'start');
    await expect(first).toHaveText(`18:00 ${twoDays.title}`);
    await expect(last).toBeVisible();
    await expect(last).toHaveAttribute('data-span', 'end');
    await expect(last).toHaveText('');
    // What the drawing says, in words: its own kind rather than its family, when it starts where
    // it starts, and which of its days each column is. A hover reads the same words out.
    await expect(first).toHaveAccessibleName(`Training — ${twoDays.title}, 18:00, day 1 of 2`);
    await expect(last).toHaveAccessibleName(`Training — ${twoDays.title}, day 2 of 2`);
    await expect(first).toHaveAttribute('title', `Training — ${twoDays.title}, 18:00, day 1 of 2`);
    // And the drawing itself, measured rather than assumed, because the marking this replaced was
    // there in the page and could not be seen: the day it begins is closed on its leading side
    // and open on the other, the day it ends is the reverse, and a rail of some length runs along
    // both. A stylesheet that failed to load would leave every one of these at nothing.
    const edges = (chip: Locator) =>
      chip.evaluate((el) => {
        const style = getComputedStyle(el);
        return [style.borderInlineStartWidth, style.borderInlineEndWidth];
      });
    expect(await edges(first)).toEqual(['3px', '0px']);
    expect(await edges(last)).toEqual(['0px', '3px']);
    for (const chip of [first, last]) {
      expect(await railOf(chip, 'borderTopWidth')).toBe('2px');
      expect(Number.parseFloat(await railOf(chip, 'width'))).toBeGreaterThanOrEqual(8);
    }
    // Pointing at either day marks both, which is what says whose rail the wordless one is.
    await last.hover();
    await expect(first).toHaveAttribute('data-linked', 'true');
    await expect(last).toHaveAttribute('data-linked', 'true');
    await expect(page.getByTestId('calendar-chip').filter({ hasText: nextWeek.title })).toHaveCount(0);

    // A week forward: the strip and its window move together, and next week's record is there.
    const range = page.getByTestId('calendar-week-range');
    const thisWeek = await range.textContent();
    const forwardAsked = askedFor(page);
    await page.getByTestId('calendar-week-next').click();
    expect(await windowAsked(forwardAsked)).toEqual([localDay(7, sunday), localDay(13, sunday)]);
    await expect(range).not.toHaveText(thisWeek ?? '');
    await expect(
      chipIn(page.getByTestId(`calendar-week-day-${nextWednesday}`), nextWeek.title),
    ).toBeVisible({ timeout: 30_000 });

    // Back to this week by its own button, and a week back from there.
    await page.getByTestId('calendar-week-today').click();
    await expect(range).toHaveText(thisWeek ?? '');
    await expect(first).toBeVisible({ timeout: 30_000 });
    const backAsked = askedFor(page);
    await page.getByTestId('calendar-week-previous').click();
    expect(await windowAsked(backAsked)).toEqual([localDay(-7, sunday), localDay(-1, sunday)]);
    await page.getByTestId('calendar-week-today').click();

    await opensEvent(page, first, twoDays);
  } finally {
    await removeEvents(page, written);
  }
});

test('the year reading counts each month, steps a year at a time, and opens a month to the records in it', async ({
  page,
}) => {
  const stamp = Date.now();
  const written: Written[] = [];
  await login(page);

  const today = new Date();
  const year = today.getFullYear();
  const month = `${year}-${`${today.getMonth() + 1}`.padStart(2, '0')}`;

  try {
    const meeting = await writeEvent(page, written, `E2E Year Meeting ${stamp}`, localDay(0));

    await gotoRoute(page, '/calendar');
    await expect(page.getByTestId('calendar-toggle-trips')).toBeVisible({ timeout: 30_000 });

    // A year asks for the whole of itself, and says how much is in each month rather than naming
    // any of it — this month holds at least the meeting just written.
    const asked = askedFor(page);
    await chooseView(page, 'Year');
    expect(await windowAsked(asked)).toEqual([`${year}-01-01`, `${year}-12-31`]);
    const thisMonth = page.getByTestId(`calendar-month-${month}`);
    await expect(thisMonth.getByTestId('calendar-month-count')).toHaveText(/^\d+ records$/, {
      timeout: 30_000,
    });
    // A year's cells name nothing, so no record is drawn as a chip anywhere on it.
    await expect(page.getByTestId('calendar-chip')).toHaveCount(0);

    // A year at a time, through the grid's own year picker — back one, and forward again.
    const yearSelect = page.locator('.ant-picker-calendar-year-select');
    const backAsked = askedFor(page);
    await stepSelect(page, yearSelect, 'ArrowUp', `${year - 1}`);
    expect(await windowAsked(backAsked)).toEqual([`${year - 1}-01-01`, `${year - 1}-12-31`]);
    // Attached rather than visible: a month with nothing in it is an empty cell, drawn at no size.
    await expect(page.getByTestId(`calendar-month-${year - 1}-01`)).toBeAttached();
    await stepSelect(page, yearSelect, 'ArrowDown', `${year}`);
    await expect(thisMonth.getByTestId('calendar-month-count')).toBeVisible({ timeout: 30_000 });

    // Opening a month: choose it in the year, then read it as a month through the grid's own
    // switch. The page follows the grid there, so the reading above it says Month too.
    await thisMonth.click();
    const monthAsked = askedFor(page);
    await page.locator('.ant-picker-calendar-mode-switch').getByText('Month', { exact: true }).click();
    const monthStart = new Date(year, today.getMonth(), 1);
    const monthEnd = new Date(year, today.getMonth() + 1, 0);
    expect(await windowAsked(monthAsked)).toEqual([localDay(-14, monthStart), localDay(14, monthEnd)]);
    await expect(
      page.getByTestId('calendar-view').locator('.ant-segmented-item-selected'),
    ).toHaveText('Month');

    const chip = chipIn(page.getByTestId(`calendar-day-${localDay(0)}`), meeting.title);
    await expect(chip).toBeVisible({ timeout: 30_000 });
    await opensEvent(page, chip, meeting);
  } finally {
    await removeEvents(page, written);
  }
});

test('the agenda reads a window forwards, one entry to a line, and opens a record from its line', async ({
  page,
}) => {
  const stamp = Date.now();
  const written: Written[] = [];
  await login(page);

  // Days of their own, well ahead, so the window chosen below holds these and next to nothing
  // else, and the lines are read in the order the days come.
  const first = localDay(45);
  const second = localDay(46);

  try {
    const training = await writeEvent(page, written, `E2E Agenda Training ${stamp}`, first, {
      kind: 'training',
      startTime: '18:30:00',
      endTime: '21:00:00',
    });
    const meeting = await writeEvent(page, written, `E2E Agenda Meeting ${stamp}`, second);

    await gotoRoute(page, '/calendar');
    await expect(page.getByTestId('calendar-toggle-trips')).toBeVisible({ timeout: 30_000 });
    await chooseView(page, 'Agenda');
    // One column of whole entries has nothing to head.
    await expect(page.locator('.ant-table-thead')).toHaveCount(0);

    // The agenda keeps the window control the record has, and asks for exactly what is picked.
    const pick = async (placeholder: string, value: string) => {
      // The test id is on both of the range's fields; the placeholder tells them apart.
      const field = page.getByTestId('calendar-window').and(page.getByPlaceholder(placeholder));
      await field.click();
      await field.fill(value);
      await page.keyboard.press('Enter');
      await expect(field).toHaveValue(value);
    };
    const asked = askedFor(page);
    await pick('Start date', first);
    await pick('End date', second);
    expect(await windowAsked(asked)).toEqual([first, second]);

    const lines = page.getByTestId('calendar-agenda-row').filter({ hasText: `${stamp}` });
    await expect(lines).toHaveCount(2, { timeout: 30_000 });
    await expect(lines.nth(0)).toContainText(training.title);
    await expect(lines.nth(1)).toContainText(meeting.title);
    // A line says what it is, of which kind, and when — the time only where the record has one.
    await expect(lines.nth(0)).toContainText('Training');
    await expect(lines.nth(0)).toContainText('· 18:30–21:00');
    await expect(lines.nth(1)).toContainText('Club meeting');
    await expect(lines.nth(1)).not.toContainText('·');

    await opensEvent(page, lines.nth(0), training);
  } finally {
    await removeEvents(page, written);
  }
});

/**
 * A kind is something only a club date has, so choosing kinds narrows the club dates and leaves
 * the trips and the camps where their own toggles put them.
 *
 * Driven against three records written for this run on days of their own — a training evening, a
 * club meeting and a trip — so each step is read off exact rows: which of the three are listed,
 * and what the page asked the server for to get them. The choice is carried in the address, which
 * is what makes a narrowed calendar a link; the flow opens that address afresh and expects the
 * same rows.
 */
test('choosing kinds narrows the events, leaves the trips listed, and travels in the address', async ({
  page,
  consoleErrors,
}) => {
  const stamp = Date.now();
  const written: Written[] = [];
  let tripId: string | undefined;
  await login(page);

  // Days of their own, further ahead than any other flow in this file writes on.
  const first = localDay(70);
  const middle = localDay(71);
  const last = localDay(72);

  try {
    const training = await writeEvent(page, written, `E2E Kind Training ${stamp}`, first, {
      kind: 'training',
    });
    const meeting = await writeEvent(page, written, `E2E Kind Meeting ${stamp}`, middle);
    const tripTitle = `E2E Kind Trip ${stamp}`;
    const trip = await asPerson<{ id: string }>(
      page,
      'POST',
      '/api/v1/trip-logs/',
      tripBody(tripTitle, last),
    );
    tripId = trip.id;
    // A trip is written as a draft, which no calendar shows; planning it is what puts it on.
    await asPerson(
      page,
      'POST',
      `/api/v1/trip-logs/${trip.id}/state`,
      { state: 'planned' },
      { 'If-Match': await versionOf(page, `/api/v1/trip-logs/${trip.id}`) },
    );

    // The window is asked for in the address rather than picked: a calendar's view is a link.
    await gotoRoute(page, `/calendar?from=${first}&to=${last}`);
    const mine = page.locator('.ant-table-tbody tr.ant-table-row').filter({ hasText: `${stamp}` });
    await expect(mine).toHaveCount(3, { timeout: 30_000 });
    await expect(page.getByTestId('calendar-kind-note')).toHaveCount(0);

    // One kind: the meeting goes, and the trip — which has no kind to be asked about — stays.
    const kinds = page.getByTestId('calendar-kind-filter');
    const asked = askedFor(page, (asking) => asking.has('kind'));
    await chooseOption(page, kinds, 'Training');
    const query = await asked;
    expect(query.get('kind')).toBe('training');
    // Choosing a kind names no family: the trips and the camps are asked for exactly as before.
    expect(query.get('source')).toBeNull();
    await page.keyboard.press('Escape');
    await expect(mine).toHaveCount(2, { timeout: 30_000 });
    await expect(mine.filter({ hasText: training.title })).toHaveCount(1);
    await expect(mine.filter({ hasText: tripTitle })).toHaveCount(1);
    await expect(mine.filter({ hasText: meeting.title })).toHaveCount(0);
    // And the page says so, because a filter one family in three can answer owes the other two a
    // sentence.
    await expect(page.getByTestId('calendar-kind-note')).toContainText('Trips and camps');
    await expect(page).toHaveURL(/[?&]kind=training(&|$)/);

    // Events of one kind and nothing else: the kind, with the other two families turned off.
    await page.getByTestId('calendar-toggle-trips').click();
    const askedForEvents = askedFor(page, without('expedition'));
    await page.getByTestId('calendar-toggle-camps').click();
    const narrowed = await askedForEvents;
    expect(narrowed.get('source')).toBe('event');
    expect(narrowed.get('kind')).toBe('training');
    await expect(mine).toHaveCount(1, { timeout: 30_000 });
    await expect(mine).toContainText(training.title);
    await expect(page.getByTestId('calendar-kind-note')).toHaveCount(0);

    // The same address opened afresh is the same calendar.
    const here = new URL(page.url());
    await gotoRoute(page, `${here.pathname}${here.search}`);
    await expect(mine).toHaveCount(1, { timeout: 30_000 });
    await expect(mine).toContainText(training.title);
    await expect(page.getByTestId('calendar-toggle-trips')).not.toBeChecked();
    await expect(page.getByTestId('calendar-toggle-events')).toBeChecked();
    await expect(page.getByTestId('calendar-kind-filter')).toContainText('Training');

    // With the events turned off there is nothing for a kind to narrow. The control is still
    // there, cannot be used, and says why — and the trips come back without a kind being asked.
    await page.getByTestId('calendar-toggle-trips').click();
    const askedWithoutEvents = askedFor(page, without('event'));
    await page.getByTestId('calendar-toggle-events').click();
    const withoutEvents = await askedWithoutEvents;
    expect(withoutEvents.get('source')).toBe('tripLog');
    expect(withoutEvents.get('kind')).toBeNull();
    await expect(page.getByTestId('calendar-kind-filter')).toHaveClass(/ant-select-disabled/);
    await page.getByTestId('calendar-kind-filter').hover();
    await expect(page.getByRole('tooltip')).toContainText('Events are turned off');
    await expect(mine).toHaveCount(1, { timeout: 30_000 });
    await expect(mine).toContainText(tripTitle);

    // A word that names no kind is refused by the server rather than ignored, so a link carrying
    // one draws the sentence for a calendar that could not be read — not a calendar quietly
    // showing every kind. The browser writes a console error for any request answered 400.
    consoleErrors.allow(
      /status of 400/,
      'this flow opens a link naming a kind the server does not have, which it refuses',
    );
    await gotoRoute(page, `/calendar?from=${first}&to=${last}&kind=banana`);
    await expect(page.getByTestId('calendar-empty')).toContainText('could not be read', {
      timeout: 30_000,
    });
  } finally {
    await removeEvents(page, written);
    if (tripId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
  }
});

/**
 * The record's rows under headings: which rows sit under which, how many each holds, and that a
 * heading folds its rows away without losing its count.
 *
 * Read off records written for this run on days of their own, far enough ahead that the window
 * holds them and nothing else — so a heading's count is exact and "the only row under it" means
 * what it says. The grouping is carried in the address like every other choice, and the flow
 * opens that address afresh to show the same headings come back.
 */
test('the record groups its rows under headings that count them, fold away, and travel in the address', async ({
  page,
}) => {
  const stamp = Date.now();
  const written: Written[] = [];
  let tripId: string | undefined;
  await login(page);

  const first = localDay(90);
  const second = localDay(91);
  const third = localDay(92);

  try {
    const training = await writeEvent(page, written, `E2E Group Training ${stamp}`, first, {
      kind: 'training',
    });
    const course = await writeEvent(page, written, `E2E Group Course ${stamp}`, second, {
      kind: 'training',
    });
    const meeting = await writeEvent(page, written, `E2E Group Meeting ${stamp}`, third);
    const tripTitle = `E2E Group Trip ${stamp}`;
    const trip = await asPerson<{ id: string }>(
      page,
      'POST',
      '/api/v1/trip-logs/',
      tripBody(tripTitle, second),
    );
    tripId = trip.id;
    await asPerson(
      page,
      'POST',
      `/api/v1/trip-logs/${trip.id}/state`,
      { state: 'planned' },
      { 'If-Match': await versionOf(page, `/api/v1/trip-logs/${trip.id}`) },
    );

    await gotoRoute(page, `/calendar?from=${first}&to=${third}`);
    const record = page.getByTestId('calendar-record');
    const rows = record.locator('tbody tr.calendar-row-entry');
    await expect(rows).toHaveCount(4, { timeout: 30_000 });
    await expect(page.getByTestId('calendar-group')).toHaveCount(0);

    // By what the Kind column says: a family for the row that has no kind, the kind for the rest.
    await chooseOption(page, page.getByTestId('calendar-group-by'), 'Kind');
    await expect(page).toHaveURL(/[?&]groupBy=kind(&|$)/);
    const headings = page.getByTestId('calendar-group');
    await expect(headings).toHaveText(['Trip (1)', 'Club meeting (1)', 'Training (2)']);

    // Every line of the list in order: each heading, then the rows it counts.
    const lines = record.locator('tbody tr.ant-table-row');
    await expect(lines).toHaveCount(7);
    await expect(lines.nth(1)).toContainText(tripTitle);
    await expect(lines.nth(3)).toContainText(meeting.title);
    await expect(lines.nth(5)).toContainText(training.title);
    await expect(lines.nth(6)).toContainText(course.title);

    // Folded, a heading keeps its count and gives up its rows; the others are untouched.
    const trainings = headings.filter({ hasText: 'Training' });
    await expect(trainings).toHaveAttribute('aria-expanded', 'true');
    await trainings.click();
    await expect(trainings).toHaveAttribute('aria-expanded', 'false');
    await expect(trainings).toHaveText('Training (2)');
    await expect(rows).toHaveCount(2);
    await expect(rows.filter({ hasText: training.title })).toHaveCount(0);
    await trainings.click();
    await expect(rows).toHaveCount(4);

    // The same address opened afresh is the same list under the same headings.
    const here = new URL(page.url());
    await gotoRoute(page, `${here.pathname}${here.search}`);
    await expect(page.getByTestId('calendar-group')).toHaveText(
      ['Trip (1)', 'Club meeting (1)', 'Training (2)'],
      { timeout: 30_000 },
    );
    await expect(page.getByTestId('calendar-group-by')).toContainText('Kind');

    // A row under a heading still opens its own record.
    await record.locator('tbody tr.calendar-row-entry').filter({ hasText: course.title }).click();
    await page.waitForURL((url) => url.pathname === `/events/${course.id}`, { timeout: 60_000 });
  } finally {
    await removeEvents(page, written);
    if (tripId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
  }
});

/**
 * The record opens on today: the list is one run through the days with a line where it crosses
 * today, and it arrives with that line a few rows down — the last things that happened above it,
 * the next things coming below.
 *
 * Measured in the browser, because nothing else lays a page out. Eight records are written either
 * side of today so there is more above the line than the list keeps in sight, and what is checked
 * is geometry: the row the list opens on is at the top of the list's own body, the line is inside
 * that body, and the page itself has not been moved. Then the part that matters to somebody
 * reading: the list is moved again when the question changes and when they ask, and is left where
 * they put it when the same question is merely answered again.
 */
test('the record opens on today, returns there when asked, and stays put when the same days are read again', async ({
  page,
}) => {
  test.slow();
  const stamp = Date.now();
  const written: Written[] = [];
  await login(page);

  try {
    for (let offset = -8; offset <= 7; offset++) {
      await writeEvent(page, written, `E2E Pivot ${offset < 0 ? 'past' : 'ahead'} ${stamp} ${offset}`, localDay(offset));
    }

    await gotoRoute(page, `/calendar?from=${localDay(-10)}&to=${localDay(10)}&map=false`);
    const record = page.getByTestId('calendar-record');
    const body = record.locator('.ant-table-body');
    const line = page.getByTestId('calendar-today-line');
    const anchor = record.locator('tbody tr.calendar-row-anchor');
    await expect(line).toBeVisible({ timeout: 30_000 });

    /** Where a row sits in the list's own body: its top and bottom, measured from the body's. */
    const within = async (locator: Locator) => {
      const [outer, inner] = await Promise.all([body.boundingBox(), locator.boundingBox()]);
      expect(outer).not.toBeNull();
      expect(inner).not.toBeNull();
      return {
        top: inner!.y - outer!.y,
        bottom: inner!.y + inner!.height - outer!.y,
        height: outer!.height,
      };
    };
    const scrollTop = () => body.evaluate((el) => el.scrollTop);

    // It arrived moved: there is more above the line than fits above it, so the body is scrolled,
    // the row it opens on is at the body's top, and the line is in sight below it.
    await expect.poll(scrollTop, { timeout: 30_000 }).toBeGreaterThan(0);
    expect(Math.abs((await within(anchor)).top)).toBeLessThanOrEqual(2);
    const lineAt = await within(line);
    expect(lineAt.top).toBeGreaterThan(0);
    expect(lineAt.bottom).toBeLessThan(lineAt.height);
    // Five rows are above the line, and what began before today is among them.
    const lines = record.locator('tbody tr.ant-table-row');
    const kinds = await lines.evaluateAll((all) =>
      all.map(
        (row) => Array.from(row.classList).find((name) => name.startsWith('calendar-row-')) ?? '',
      ),
    );
    const anchorAt = await lines.evaluateAll((all) =>
      all.findIndex((row) => row.classList.contains('calendar-row-anchor')),
    );
    expect(kinds.indexOf('calendar-row-today') - anchorAt).toBe(5);
    // Rows either side of the line are in sight together.
    const past = record.locator('tbody tr.calendar-row-entry').filter({ hasText: `${stamp} -1` });
    const ahead = record.locator('tbody tr.calendar-row-entry').filter({ hasText: `${stamp} 0` });
    expect((await within(past)).top).toBeGreaterThanOrEqual(0);
    expect((await within(ahead)).bottom).toBeLessThanOrEqual(lineAt.height);
    // Only the list was moved. Nothing the list sits inside has been — not the window, and not
    // whichever part of the application's frame it is that scrolls.
    const movedAround = await record.evaluate((el) => {
      let moved = window.scrollY;
      for (let node = el.parentElement; node; node = node.parentElement) {
        moved += node.scrollTop;
      }
      return moved;
    });
    expect(movedAround).toBe(0);

    // Moved by the reader, it stays where they put it when the same days are read again — even
    // when what comes back is different. A record is written behind the page's back and the page
    // is made to read its days again, which is what coming back to the window does; the new row
    // arriving is what says the answer has been redrawn, and the list has not been moved by it.
    await body.evaluate((el) => el.scrollTo({ top: 0 }));
    expect(await scrollTop()).toBe(0);
    const late = await writeEvent(page, written, `E2E Pivot late ${stamp}`, localDay(9));
    await page.evaluate(() => window.dispatchEvent(new Event('visibilitychange')));
    await expect(
      record.locator('tbody tr.calendar-row-entry').filter({ hasText: late.title }),
    ).toBeAttached({ timeout: 30_000 });
    expect(await scrollTop()).toBe(0);

    // Asked for, it goes back.
    await page.getByTestId('calendar-today').click();
    await expect.poll(scrollTop).toBeGreaterThan(0);
    expect(Math.abs((await within(anchor)).top)).toBeLessThanOrEqual(2);

    // A different question is a different list, and opens on today of its own accord.
    await body.evaluate((el) => el.scrollTo({ top: 0 }));
    const narrowed = askedFor(page, without('tripLog'));
    await page.getByTestId('calendar-toggle-trips').click();
    await narrowed;
    await expect.poll(scrollTop, { timeout: 30_000 }).toBeGreaterThan(0);

    // Ordered by title the list runs through no time: no line, and no way to today offered.
    await page.locator('.ant-table-thead th').filter({ hasText: 'What' }).click();
    await expect(page).toHaveURL(/[?&]sort=title(&|$)/);
    await expect(line).toHaveCount(0);
    await expect(page.getByTestId('calendar-today')).toBeDisabled();
  } finally {
    await removeEvents(page, written);
  }
});
