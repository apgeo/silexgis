// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import {
  asPerson,
  localDay,
  registerAccount,
  signedInElsewhere,
  tryAsPerson,
  versionOf,
} from './arrange.ts';
import { chooseOption, gotoRoute, login } from './helpers.ts';

/**
 * The dated things a club runs that are not trips and not camps: the list they are found on, the
 * page each one is read on, the form they are written and changed with, and the list of who is
 * coming to one.
 *
 * Writing an event, asking somebody to one and writing a run of them are driven by the calendar
 * spec, where the calendar is the reason they exist. What is driven here is the rest of each
 * page: what an event says about itself once written, how it moves from an idea to a date that
 * happened, how a run is edited and called off as a run, how the places on one fill and who waits,
 * and what somebody the event is not for is shown of it.
 *
 * Every event is written by the run that reads it and carries the run's stamp in its title.
 */

interface EventRow {
  id: string;
  title: string;
  startDate: string;
  seriesId?: string | null;
  place?: string | null;
}

/** Writes one club event through the API, as the signed-in person. */
async function writeEvent(
  page: Page,
  title: string,
  startDate: string,
  extra: Record<string, unknown> = {},
): Promise<EventRow> {
  return asPerson<EventRow>(page, 'POST', '/api/v1/events', {
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
}

/** Moves an event to another state, naming the version it was read at, as the page does. */
async function moveEvent(page: Page, id: string, state: string) {
  const version = await versionOf(page, `/api/v1/events/${id}`);
  await asPerson(page, 'POST', `/api/v1/events/${id}/state`, { state }, { 'If-Match': version });
}

/** Types a value into one of the form's date or time fields and commits it with Enter. */
async function typeInto(page: Page, field: Locator, value: string) {
  await field.click();
  await field.fill(value);
  await page.keyboard.press('Enter');
  await expect(field).toHaveValue(value);
}

/** The value beside one of the event page's labels. */
function described(page: Page, label: string): Locator {
  return page
    .locator('.ant-descriptions-item')
    .filter({ has: page.locator('.ant-descriptions-item-label', { hasText: new RegExp(`^${label}$`) }) })
    .locator('.ant-descriptions-item-content');
}

/** Clicks one of the state control's moves, by the words on it, and waits for the move to land. */
async function move(page: Page, label: string, confirm = false) {
  const moved = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' && /\/api\/v1\/events\/[^/]+\/state$/.test(response.url()),
  );
  await page.getByRole('button', { name: new RegExp(`${label}$`) }).click();
  if (confirm) {
    await page.getByRole('tooltip').getByRole('button', { name: 'OK' }).click();
  }
  expect((await moved).ok()).toBeTruthy();
}

test('an event is written with its day, its hours and its place, changed, taken from an idea to called off, and deleted', async ({
  page,
}) => {
  const title = `E2E Event Page ${Date.now()}`;
  const day = localDay(12);
  await login(page);

  await gotoRoute(page, '/events');
  await page.getByTestId('event-create').click();
  const form = page.getByTestId('event-form');
  await expect(form).toBeVisible();
  await form.getByTestId('event-title').fill(title);
  await chooseOption(page, page.getByTestId('event-kind'), 'Training');
  await typeInto(page, form.getByPlaceholder('Start date'), day);
  // Wall-clock times carrying no zone: an evening that starts at seven reads as seven to everybody.
  await typeInto(page, page.getByTestId('event-start-time'), '19:00');
  await typeInto(page, page.getByTestId('event-end-time'), '21:30');
  await page.getByTestId('event-place').fill('The club hut');
  await form.getByLabel('Description').fill('Rope work on the training wall.');
  await page.getByRole('dialog').getByRole('button', { name: 'OK' }).click();

  // The create opens the event it wrote, and the page says back everything the form was told.
  await expect(page.getByTestId('event-title')).toHaveText(title, { timeout: 15_000 });
  await expect(page.getByTestId('trip-state')).toHaveText('Draft');
  await expect(page.getByText('Training', { exact: true }).first()).toBeVisible();
  await expect(described(page, 'Time')).toHaveText('19:00 – 21:30');
  await expect(described(page, 'Place')).toHaveText('The club hut');
  await expect(described(page, 'Description')).toHaveText('Rope work on the training wall.');
  const eventUrl = page.url();

  // Changed through the same form, opened on what the event already says.
  await page.getByTestId('event-edit').click();
  const edit = page.getByRole('dialog').filter({ hasText: 'Edit event' });
  await expect(edit.getByTestId('event-title')).toHaveValue(title);
  await expect(page.getByTestId('event-place')).toHaveValue('The club hut');
  await page.getByTestId('event-place').fill('The village hall');
  await edit.getByRole('button', { name: 'OK' }).click();
  await expect(edit).toBeHidden({ timeout: 15_000 });
  await expect(described(page, 'Place')).toHaveText('The village hall');

  // From a workshop draft to a date the club is keeping, then to one it has called off. Calling it
  // off reads as an ending, so it is asked first; the others are not.
  await move(page, 'Start organising');
  await expect(page.getByTestId('trip-state')).toHaveText('Planned', { timeout: 15_000 });
  await move(page, 'It is going ahead');
  await expect(page.getByTestId('trip-state')).toHaveText('Confirmed', { timeout: 15_000 });
  await move(page, 'Call it off', true);
  await expect(page.getByTestId('trip-state')).toHaveText('Cancelled', { timeout: 15_000 });
  // A called-off date offers only the way back to the workshop.
  await expect(page.getByRole('button', { name: /It is going ahead$/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Back to draft$/ })).toBeVisible();

  // Every one of those changes is stored, not held by the page.
  await page.goto(eventUrl);
  await expect(page.getByTestId('trip-state')).toHaveText('Cancelled', { timeout: 30_000 });
  await expect(described(page, 'Place')).toHaveText('The village hall');

  // A training evening is somewhere people come, so it is asked who is coming; its history is the
  // other tab, and the tab is in the address.
  await expect(page.getByRole('tab', { name: 'Who is coming', exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'History', exact: true }).click();
  await expect(page).toHaveURL(/\?tab=history$/);

  // An event standing on its own is deleted after a plain yes, and the list no longer has it.
  await page.getByTestId('event-delete').click();
  await expect(page.getByText('Delete this event?')).toBeVisible();
  await page.getByRole('tooltip').getByRole('button', { name: 'OK' }).click();
  await page.waitForURL((url) => url.pathname === '/events', { timeout: 30_000 });
  await page.getByTestId('event-search').locator('input').fill(title);
  await expect(page.getByTestId('event-table').locator('.ant-empty')).toBeVisible({
    timeout: 15_000,
  });
  await expect(page.getByTestId('event-table').getByText(title, { exact: true })).toHaveCount(0);
});

test('the event list narrows by title, by kind and by state, and a row opens its event', async ({
  page,
}) => {
  const stamp = Date.now();
  await login(page);

  const written: EventRow[] = [];
  try {
    written.push(
      await writeEvent(page, `E2E Listed Training ${stamp}`, localDay(20), { kind: 'training' }),
    );
    written.push(await writeEvent(page, `E2E Listed Meeting ${stamp}`, localDay(21)));
    await moveEvent(page, written[1].id, 'planned');
    const [training, meeting] = written;

    await gotoRoute(page, '/events');
    const table = page.getByTestId('event-table');
    const rows = table.locator('tr.ant-table-row');

    // The search is the title, and both of this run's events carry its stamp.
    await page.getByTestId('event-search').locator('input').fill(`${stamp}`);
    await expect(rows).toHaveCount(2, { timeout: 15_000 });

    // By kind: the training evening alone.
    await chooseOption(page, page.getByTestId('event-kind-filter'), 'Training');
    await expect(rows).toHaveCount(1, { timeout: 15_000 });
    await expect(rows.nth(0)).toContainText(training.title);
    await expect(rows.nth(0)).toContainText('Training');
    await page.getByTestId('event-kind-filter').locator('.ant-select-clear').click();
    await expect(rows).toHaveCount(2, { timeout: 15_000 });

    // By where it has got to: only the meeting is being organised.
    await chooseOption(page, page.getByTestId('event-state-filter'), 'Planned');
    await expect(rows).toHaveCount(1, { timeout: 15_000 });
    await expect(rows.nth(0)).toContainText(meeting.title);
    await expect(rows.nth(0)).toContainText('Planned');

    await rows.nth(0).click();
    await page.waitForURL((url) => url.pathname === `/events/${meeting.id}`, { timeout: 30_000 });
    await expect(page.getByTestId('event-title')).toHaveText(meeting.title, { timeout: 15_000 });
    await expect(page.getByTestId('trip-state')).toHaveText('Planned');
  } finally {
    for (const row of written) {
      await tryAsPerson(page, 'DELETE', `/api/v1/events/${row.id}`);
    }
  }
});

/**
 * A run of evenings is ordinary events sharing a grouping key, so everything done to it as a run
 * is done from one of its occurrences: the way to the rest, an edit that reaches this evening and
 * the later ones, and calling off the rest — which keeps every evening that has already happened,
 * because each is the record of an evening that did.
 *
 * The run starts a fortnight back, weekly, four evenings: two behind, one today, one ahead. Calling
 * off from the second keeps the second, which has happened, as well as the first, which the act
 * never reached; today's and next week's go.
 */
test('a run of evenings links to the rest, an edit reaches this evening and the later ones, and calling off the rest keeps what happened', async ({
  page,
}) => {
  const stamp = Date.now();
  const title = `E2E Weekly ${stamp}`;
  await login(page);

  const anchor = await writeEvent(page, title, localDay(-14), {
    recurrence: { frequency: 'weekly', rule: 'every week of the season', count: 4, until: null },
  });
  expect(anchor.seriesId).toBeTruthy();
  const seriesId = anchor.seriesId!;

  try {
    const listed = await asPerson<{ items: EventRow[] }>(
      page,
      'GET',
      `/api/v1/events?seriesId=${seriesId}&pageSize=50`,
    );
    const run = [...listed.items].sort((a, b) => a.startDate.localeCompare(b.startDate));
    expect(run.map((row) => row.startDate)).toEqual([
      localDay(-14),
      localDay(-7),
      localDay(0),
      localDay(7),
    ]);
    const [firstEvening, secondEvening, , lastEvening] = run;

    // Any occurrence says it is one of a run, in its author's words, and leads to the rest.
    await gotoRoute(page, `/events/${secondEvening.id}`);
    const banner = page.getByTestId('event-series-banner');
    await expect(banner).toContainText('Described as: every week of the season', { timeout: 15_000 });
    await page.getByTestId('event-series-occurrences').click();
    await page.waitForURL(
      (url) => url.pathname === '/events' && url.searchParams.get('seriesId') === seriesId,
      { timeout: 30_000 },
    );
    // The narrowing is said out loud and can be put away, because a list showing four rows with
    // no visible reason reads as a list that has lost the rest.
    await expect(page.getByTestId('event-series-filter')).toBeVisible();
    const rows = page.getByTestId('event-table').locator('tr.ant-table-row');
    await expect(rows).toHaveCount(4, { timeout: 15_000 });
    await page.getByTestId('event-series-filter').locator('.ant-tag-close-icon').click();
    await expect(page.getByTestId('event-series-filter')).toHaveCount(0);

    // An edit asks what it reaches, and the narrow answer is the one it opens on.
    await gotoRoute(page, `/events/${secondEvening.id}`);
    await page.getByTestId('event-edit').click();
    const edit = page.getByRole('dialog').filter({ hasText: 'Edit event' });
    // The test ids sit on the radio inputs themselves; the words are what a person clicks.
    await expect(edit.getByTestId('event-scope-occurrence')).toBeChecked();
    await edit.getByText('This one and every later one', { exact: true }).click();
    await expect(edit.getByTestId('event-scope-following')).toBeChecked();
    await page.getByTestId('event-place').fill('The lower hall');
    await edit.getByRole('button', { name: 'OK' }).click();
    // This evening and the two after it — an edit reaches back over evenings that happened.
    await expect(page.getByText('3 occurrences were changed.')).toBeVisible({ timeout: 15_000 });
    await expect(described(page, 'Place')).toHaveText('The lower hall');

    await gotoRoute(page, `/events/${lastEvening.id}`);
    await expect(described(page, 'Place')).toHaveText('The lower hall', { timeout: 15_000 });
    await gotoRoute(page, `/events/${firstEvening.id}`);
    await expect(page.getByTestId('event-title')).toHaveText(title, { timeout: 15_000 });
    await expect(described(page, 'Place')).toHaveCount(0);

    // Calling off the rest, from the second evening. It has happened, so it is kept; the two still
    // to come go. Both halves are said, and the kept half counts every evening of the run that is
    // behind us — somebody told only how many went would believe the whole run is gone.
    await gotoRoute(page, `/events/${secondEvening.id}`);
    await page.getByTestId('event-delete').click();
    const scope = page.getByTestId('event-delete-scope');
    await expect(scope).toBeVisible();
    // Calling off two years of evenings must be something somebody picked, so it opens on one.
    await expect(page.getByTestId('event-delete-scope-occurrence')).toBeChecked();
    await scope.getByText('This one and every later one', { exact: true }).click();
    await expect(page.getByTestId('event-delete-scope-following')).toBeChecked();
    const calledOff = page.waitForResponse(
      (response) =>
        response.request().method() === 'DELETE' && response.url().endsWith('/series/following'),
    );
    await page.getByTestId('event-delete-confirm').click();
    const outcome = await calledOff;
    expect(outcome.ok()).toBeTruthy();
    expect(await outcome.json()).toMatchObject({ deleted: 2, kept: 2 });
    await expect(
      page.getByText('2 occurrences went; 2 had already happened and were kept.'),
    ).toBeVisible({ timeout: 15_000 });
    await page.waitForURL((url) => url.pathname === '/events', { timeout: 30_000 });

    await gotoRoute(page, `/events/${secondEvening.id}`);
    await expect(page.getByTestId('event-title')).toHaveText(title, { timeout: 15_000 });
    const remaining = await asPerson<{ items: EventRow[] }>(
      page,
      'GET',
      `/api/v1/events?seriesId=${seriesId}&pageSize=50`,
    );
    expect(remaining.items.map((row) => row.startDate).sort()).toEqual([
      localDay(-14),
      localDay(-7),
    ]);
  } finally {
    const left = await asPerson<{ items: EventRow[] }>(
      page,
      'GET',
      `/api/v1/events?seriesId=${seriesId}&pageSize=50`,
    ).catch(() => ({ items: [] as EventRow[] }));
    for (const row of left.items) {
      await tryAsPerson(page, 'DELETE', `/api/v1/events/${row.id}`);
    }
  }
});

/**
 * The places on an event fill in the order people said yes, and the limit never turns anybody away:
 * whoever is past it waits, in order. Picking somebody out of the order is the organiser's
 * override — it gives them a place wherever they stand and moves the last person who would
 * otherwise have had one to waiting, while the order underneath stays exactly the order people
 * answered in.
 */
test('who is coming fills the places in the order people answer, keeps the rest waiting, and the organiser picks and removes', async ({
  page,
}) => {
  const stamp = Date.now();
  await login(page);

  const event = await writeEvent(page, `E2E Places ${stamp}`, localDay(15), { maxParticipants: 1 });
  try {
    const caverNamed = async (name: string) => {
      const found = await asPerson<{ id: string; name: string }[]>(
        page,
        'GET',
        `/api/v1/cavers?search=${encodeURIComponent(name)}`,
      );
      const caver = found.find((row) => row.name === name);
      expect(caver, `the demonstration roster holds ${name}`).toBeTruthy();
      return caver!.id;
    };
    const ana = await caverNamed('Ana Demo');
    const bogdan = await caverNamed('Bogdan Demo');
    // Asked through the API: asking from the tab is driven by the calendar spec.
    for (const caverId of [ana, bogdan]) {
      await asPerson(page, 'POST', `/api/v1/events/${event.id}/invitations`, { caverId });
    }

    await gotoRoute(page, `/events/${event.id}`);
    // The list of who is coming is the first tab an event people come to opens on.
    const limit = page.getByTestId('event-invitations-limit');
    await expect(limit).toHaveText('0 of 1 places taken, 0 waiting.', { timeout: 15_000 });

    const answer = async (caverId: string, response: string, note?: string) => {
      const control = page.getByTestId(`event-invitation-answer-${caverId}`);
      await chooseOption(page, control, response);
      if (note) {
        await page.getByTestId(`event-invitation-note-${caverId}`).fill(note);
      }
      const saved = page.waitForResponse(
        (r) => r.request().method() === 'PUT' && r.url().includes(`/invitations/${caverId}/response`),
      );
      await page.getByTestId(`event-invitation-save-${caverId}`).click();
      expect((await saved).ok()).toBeTruthy();
    };
    const row = (caverId: string) => page.getByTestId(`event-invitation-${caverId}`);

    await answer(ana, 'Coming');
    await expect(limit).toHaveText('1 of 1 places taken, 0 waiting.', { timeout: 15_000 });
    await expect(page.getByTestId(`event-invitation-place-${ana}`)).toHaveText('#1');

    // The second yes is past the limit, and waits rather than being refused.
    await answer(bogdan, 'Coming', 'Can drive two');
    await expect(limit).toHaveText('1 of 1 places taken, 1 waiting.', { timeout: 15_000 });
    await expect(page.getByTestId(`event-invitation-place-${bogdan}`)).toHaveText('#2');
    await expect(row(bogdan)).toContainText('Waiting');
    await expect(row(bogdan)).toContainText('Can drive two');

    // The organiser picks the second: they hold the place now, the first waits, and nobody's place
    // in the order moves.
    await page.getByTestId(`event-invitation-select-${bogdan}`).click();
    await expect(row(bogdan)).toContainText('Picked', { timeout: 15_000 });
    await expect(row(ana)).toContainText('Waiting');
    await expect(limit).toHaveText('1 of 1 places taken, 1 waiting.');
    await expect(page.getByTestId(`event-invitation-place-${ana}`)).toHaveText('#1');
    await expect(page.getByTestId(`event-invitation-place-${bogdan}`)).toHaveText('#2');

    // Taking somebody off the list is asked first, and takes their answer with them.
    await page.getByTestId(`event-invitation-remove-${ana}`).click();
    await expect(page.getByText('Take this person off the list entirely, answer and all?')).toBeVisible();
    await page.getByRole('tooltip').getByRole('button', { name: 'OK' }).click();
    await expect(row(ana)).toHaveCount(0, { timeout: 15_000 });
    await expect(limit).toHaveText('1 of 1 places taken, 0 waiting.');
  } finally {
    await tryAsPerson(page, 'DELETE', `/api/v1/events/${event.id}`);
  }
});

/**
 * What somebody else is shown. An event they may not read and one that does not exist are the same
 * page, saying it cannot tell the two apart; an event they may read is read in full, with none of
 * the controls that would change it, because the right to edit an event is its own.
 */
test('an event somebody may not read is no such event to them, and one they may read offers them nothing to change', async ({
  page,
  browser,
  request,
  consoleErrors,
}) => {
  test.slow();
  const stamp = Date.now();
  // The page asks for the event it was sent to, is answered 404, and says so — and the browser
  // writes a console error for every request that answered 404, however well the page handled it.
  consoleErrors.allow(
    /status of 404/,
    'this flow opens an event its reader may not read, which the server answers as not found',
  );
  const reader = await registerAccount(request, 'eventreader');
  const admin = await signedInElsewhere(browser);

  const written: EventRow[] = [];
  try {
    written.push(
      await writeEvent(admin.page, `E2E Kept Close ${stamp}`, localDay(18), { visibility: 'private' }),
    );
    written.push(await writeEvent(admin.page, `E2E Open Evening ${stamp}`, localDay(19)));
    const [closed, open] = written;

    await login(page, reader.email, reader.password);

    await gotoRoute(page, `/events/${closed.id}`);
    await expect(page.getByText('No such event')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('Either it is not there, or it is not yours to read.')).toBeVisible();
    await expect(page.getByText(closed.title)).toHaveCount(0);

    await gotoRoute(page, `/events/${open.id}`);
    await expect(page.getByTestId('event-title')).toHaveText(open.title, { timeout: 30_000 });
    await expect(page.getByTestId('trip-state')).toHaveText('Draft');
    for (const control of ['event-edit', 'event-delete', 'event-permissions']) {
      await expect(page.getByTestId(control)).toHaveCount(0);
    }
    await expect(page.getByRole('button', { name: /Start organising$/ })).toHaveCount(0);
    // The list of who is coming is read, and nobody is asked from here.
    await expect(page.getByTestId('event-invitations-limit')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('event-invite-name')).toHaveCount(0);
  } finally {
    for (const row of written) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/events/${row.id}`);
    }
    await admin.context.close();
  }
});
