// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import dayjs from 'dayjs';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CalendarEntry, CalendarParams } from '../../api/hooks.ts';

const { calendarSpy, navigateSpy } = vi.hoisted(() => ({
  calendarSpy: vi.fn(),
  navigateSpy: vi.fn(),
}));

vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useNavigate: () => navigateSpy,
}));

vi.mock('../../api/hooks.ts', () => ({
  useCalendar: (params: CalendarParams, options?: { enabled?: boolean }) =>
    calendarSpy(params, options),
  useCavingGroups: () => ({ data: [] }),
  // The pane under the record builds an OpenLayers map of its own; what it asks for is proved
  // where it lives, and here it is only required not to interfere with the record above it.
  useTripLogMap: () => ({ data: undefined }),
}));

const { default: CalendarPage } = await import('./CalendarPage.tsx');

/**
 * Every member a calendar row carries, written as a map over the row's own type rather than as a
 * list of words. A member added to the row on the server, regenerated into the client's types and
 * not thought about here, fails to compile against `Record<keyof CalendarEntry, true>` — which is
 * the point: the field most likely to arrive is a cave, and widening this map is the deliberate
 * act of saying somebody decided it belongs.
 */
const rowShape: Record<keyof CalendarEntry, true> = {
  source: true,
  id: true,
  title: true,
  start: true,
  end: true,
  startTime: true,
  endTime: true,
  kind: true,
  state: true,
  placement: true,
  cavingGroupId: true,
  hasPosition: true,
};

function row(overrides: Partial<CalendarEntry> = {}): CalendarEntry {
  // No cast: the literal must satisfy the generated type exactly, so a new member on the row
  // stops the build here instead of being absorbed by an assertion nobody re-reads.
  const base: CalendarEntry = {
    source: 'tripLog',
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Coiba Mare recce',
    start: '2026-09-05',
    end: null,
    startTime: null,
    endTime: null,
    kind: null,
    state: 'planned',
    placement: 'ahead',
    cavingGroupId: null,
    hasPosition: false,
  };
  return { ...base, ...overrides };
}

/** What the page last asked the server for. */
function lastParams(): CalendarParams {
  return calendarSpy.mock.calls.at(-1)![0] as CalendarParams;
}

/** Whether the page asked at all. */
function lastEnabled(): boolean {
  const options = calendarSpy.mock.calls.at(-1)![1] as { enabled?: boolean } | undefined;
  return options?.enabled ?? true;
}

function answer(entries: CalendarEntry[], omitted = 0) {
  calendarSpy.mockReturnValue({
    data: { entries, omitted },
    isFetching: false,
    isError: false,
  });
}

function fails() {
  calendarSpy.mockReturnValue({ data: undefined, isFetching: false, isError: true });
}

function show() {
  return render(
    <MemoryRouter>
      <CalendarPage />
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  calendarSpy.mockReset();
  navigateSpy.mockReset();
  answer([row()]);
});

describe('the calendar record', () => {
  it('opens on a window it chose, with both ends of it, and reads both families of record', () => {
    show();

    const asked = lastParams();
    expect(asked.from).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(asked.to).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(asked.from < asked.to).toBe(true);
    // Both kinds are on, so no family is named: the answer is everything in the window.
    expect(asked.source).toBeUndefined();
    expect(screen.getByText('Coiba Mare recce')).toBeTruthy();
  });

  /**
   * The row's own title, drawn as it was written. Rewriting it for a reader who has already been
   * handed the row would withhold from them what the row's own page shows, and the decision about
   * who may see a row is taken before the row reaches this table at all.
   */
  it('draws the title as written', () => {
    answer([row({ title: 'Peștera cu Oase — derig' })]);
    show();

    expect(screen.getByText('Peștera cu Oase — derig')).toBeTruthy();
  });

  /**
   * The one thing a record of a month must not do is be quietly short. When the answer says rows
   * were left out, the page says so where somebody reading the rows will see it.
   */
  it('says how many rows the answer could not carry', () => {
    answer([row()], 12);
    show();

    expect(screen.getByTestId('calendar-omitted').textContent).toContain('12');
  });

  it('says nothing about a shortfall when there was none', () => {
    answer([row()], 0);
    show();

    expect(screen.queryByTestId('calendar-omitted')).toBeNull();
  });

  /** A date that has been put back is listed and marked, because that date is not one anybody is going on. */
  it('marks a row whose date has been abandoned', () => {
    answer([row({ state: 'delayed', placement: 'putBack' })]);
    show();

    expect(screen.getByTestId('calendar-postponed')).toBeTruthy();
  });

  /**
   * Narrowing to one family is a question the answer understands; asking for neither is not, and
   * a request that could only come back empty is not worth making.
   */
  it('names the families it wants, and asks nothing when it wants none', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-other'));
    expect(lastParams().source).toBe('tripLog');

    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    expect(lastEnabled()).toBe(false);
    expect(screen.getByTestId('calendar-empty').textContent).toContain('No kind of record');
  });

  /**
   * "Everything except the trips" is more than one family now that there are three, and a
   * narrowing that could only name one would drop the family it left out of a record that says
   * nothing is missing — the failure nobody sees, because an absent calendar row looks exactly
   * like a day with nothing on it.
   */
  it('keeps every non-trip family when the trips are turned off', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    expect(lastEnabled()).toBe(true);
    expect(lastParams().source?.split(',').sort()).toEqual(['event', 'expedition']);
  });

  /**
   * A row leads to its own record and to no other. The families are three and the addresses are
   * three; a row that opened another family's page would be a dead end on the one surface whose
   * whole purpose is leading somewhere.
   */
  it('opens each family of row at its own address', () => {
    const cases: [CalendarEntry['source'], string][] = [
      ['tripLog', '/trip-logs/'],
      ['expedition', '/expeditions/'],
      ['event', '/events/'],
    ];

    for (const [source, prefix] of cases) {
      answer([row({ source, id: '99999999-9999-9999-9999-999999999999' })]);
      show();
      fireEvent.click(screen.getByText('Coiba Mare recce'));
      expect(navigateSpy).toHaveBeenLastCalledWith(`${prefix}99999999-9999-9999-9999-999999999999`);
      cleanup();
    }
  });

  /**
   * The one source that is more than one thing says which thing it is. A permit deadline and a
   * social evening drawn as the same word would be indistinguishable to somebody scanning a month.
   */
  it('draws an event by its own kind rather than by its family', () => {
    answer([row({ source: 'event', kind: 'deadline' })]);
    show();

    expect(screen.getByText('Deadline')).toBeTruthy();
  });

  /**
   * Rows called off are in by default and are narrowed away only when somebody asks — the person
   * who was going on one is exactly the reader who most needs to find it.
   */
  it('leaves called-off rows in until they are turned off', () => {
    show();

    expect(lastParams().includeCancelled).toBeUndefined();
    fireEvent.click(screen.getByTestId('calendar-toggle-cancelled'));
    expect(lastParams().includeCancelled).toBe(false);
  });

  it('narrows the window to what is still to come when the past is turned off', () => {
    show();

    expect(lastParams().includePast).toBeUndefined();
    fireEvent.click(screen.getByTestId('calendar-toggle-past'));
    expect(lastParams().includePast).toBe(false);
  });

  /**
   * Whose rows these are is worked out from the request itself. The page sends a flag and never
   * an identifier, so there is no control here that could assemble where a named person has been.
   */
  it('asks for its own reader by a flag, and never names anybody', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-mine'));
    expect(lastParams().mine).toBe(true);
    expect(Object.keys(lastParams())).toEqual(
      expect.not.arrayContaining(['caverId', 'userId', 'participantId', 'ownerId']),
    );
  });

  /**
   * The row is a projection, not a trip: nothing derived from a cave reaches it, so nothing here
   * can draw one. A field added to the row later fails this rather than passing review.
   */
  it('carries no cave-derived field to draw', () => {
    // Asserted over the row's own type, not over a literal written next to it: a fixture can
    // only ever say what somebody typed into it, while the map is checked against the generated
    // contract and cannot fall behind it.
    const declared = Object.keys(rowShape);
    // `hasPosition` is deliberately allowed: whether there is a place is not where it is.
    expect(declared.filter((key) => /cave|geom|coordinate|latitude|longitude/i.test(key))).toEqual(
      [],
    );
    expect(declared).toEqual([
      'source',
      'id',
      'title',
      'start',
      'end',
      'startTime',
      'endTime',
      'kind',
      'state',
      'placement',
      'cavingGroupId',
      'hasPosition',
    ]);
    // And what the page is actually handed carries the same members and no others.
    expect([...Object.keys(row())].sort()).toEqual([...declared].sort());
  });

  /**
   * Both orders this page offers are asked of the server, because the rows on screen are one page
   * of a merged answer and reordering that page would arrange the wrong rows. A column identified
   * to the table only by a key reports no field when it is clicked, so the header would draw an
   * arrow for an order that was never sent — which is exactly what this pins.
   */
  it('asks the server for either order it offers', () => {
    show();

    // The table draws its header cells more than once (a measuring row sits behind the visible
    // one), so the first match is the one a reader clicks.
    const when = screen.getAllByText('When')[0];
    fireEvent.click(when);
    expect(lastParams().sort).toBe('-start');
    fireEvent.click(when);
    expect(lastParams().sort).toBeUndefined();

    const what = screen.getAllByText('What')[0];
    fireEvent.click(what);
    expect(lastParams().sort).toBe('title');
    fireEvent.click(what);
    expect(lastParams().sort).toBe('-title');
  });

  /** Three situations, three sentences: only one of them is a fact about the calendar. */
  it('separates a failed read from a narrowed one and from an empty stretch of days', () => {
    fails();
    show();
    expect(screen.getByTestId('calendar-empty').textContent).toContain('could not be read');

    cleanup();
    answer([]);
    show();
    expect(screen.getByTestId('calendar-empty').textContent).toContain('Nothing is recorded');

    fireEvent.click(screen.getByTestId('calendar-toggle-mine'));
    expect(screen.getByTestId('calendar-empty').textContent).toContain('matches what you asked');
  });
});


/**
 * The grids are drawn over the month or the year they are showing, so a fixture has to fall in
 * the panel the page opens on rather than on a date somebody wrote down once.
 */
const inThisMonth = (offsetDays: number): string =>
  dayjs().startOf('month').add(offsetDays, 'day').format('YYYY-MM-DD');

function showMonth() {
  const rendered = show();
  fireEvent.click(screen.getByText('Month'));
  return rendered;
}

/** Every chip drawn in one day's cell, in the order the grid drew them. */
function chipsOn(day: string): HTMLElement[] {
  return within(screen.getByTestId(`calendar-day-${day}`)).queryAllByTestId('calendar-chip');
}

describe('the calendar as a grid of days', () => {
  /**
   * The reason the grid overrides the height of a day's contents rather than using the calendar
   * as it ships. The shipped day cell reserves exactly three rows and scrolls the rest inside
   * itself, so a Saturday carrying four records shows three of them and hides the fourth behind a
   * scrollbar a few pixels wide — a clash the reader is never told about. The count is asserted,
   * not the presence of a cell, because three-of-four is precisely the failure.
   */
  it('shows every record on a day, and not the first three of them', () => {
    const saturday = inThisMonth(10);
    answer([
      row({ id: 'a', title: 'Coiba Mare recce', start: saturday }),
      row({ id: 'b', title: 'Huda lui Papară', start: saturday }),
      row({ id: 'c', title: 'Vântului derig', start: saturday }),
      row({ id: 'd', title: 'Scărișoara survey', start: saturday }),
    ]);
    showMonth();

    const chips = chipsOn(saturday);
    expect(chips).toHaveLength(4);
    expect(chips.map((chip) => chip.textContent)).toEqual([
      'Coiba Mare recce',
      'Huda lui Papară',
      'Vântului derig',
      'Scărișoara survey',
    ]);
  });

  /**
   * The other half of that, and the half no count can see. Nothing renders a stylesheet here, so
   * all four chips are in the document whether or not the clip is turned off — the clip is a
   * height and an overflow applied by the calendar's own styling to the box the contents sit in.
   * What this asserts is that the override reached that box: if it stops being handed over, the
   * shipped three-row clip comes back and every count above stays green while a reader loses the
   * fourth record on a day.
   */
  it('hands the height override to the box the calendar would have clipped', () => {
    const day = inThisMonth(10);
    answer([row({ start: day })]);
    showMonth();

    const box = screen.getByTestId(`calendar-day-${day}`).parentElement!;
    // The calendar's own element for a cell's contents — the one it sizes at three rows.
    expect(box.className).toContain('date-content');
    expect(box.style.height).toBe('auto');
    expect(box.style.overflowY).toBe('visible');
  });

  /**
   * A record lasting four days is drawn in all four of them, because a grid answers "what is
   * happening on this day". The first and the last cell are marked and the two between are not,
   * which is what stops the repetition reading as four separate trips.
   */
  it('draws a record in every day it spans, marked where it starts and where it ends', () => {
    const first = inThisMonth(3);
    const last = inThisMonth(6);
    answer([row({ title: 'Ponorul camp', start: first, end: last })]);
    showMonth();

    const days = [3, 4, 5, 6].map((offset) => inThisMonth(offset));
    const marks = days.map((day) => {
      const chip = chipsOn(day)[0];
      expect(chip).toBeTruthy();
      expect(chip.textContent).toBe('Ponorul camp');
      return [chip.dataset.spanStart, chip.dataset.spanEnd];
    });

    expect(marks).toEqual([
      ['true', 'false'],
      ['false', 'false'],
      ['false', 'false'],
      ['false', 'true'],
    ]);
  });

  /**
   * A day with nothing on it is an empty day and not a hole. The cell and its content box are
   * still drawn, so the month reads as a grid; what is missing is the records, not the day.
   */
  it('draws a day with nothing on it as an empty day', () => {
    const busy = inThisMonth(10);
    answer([row({ start: busy })]);
    showMonth();

    const quiet = screen.getByTestId(`calendar-day-${inThisMonth(11)}`);
    expect(quiet).toBeTruthy();
    expect(within(quiet).queryAllByTestId('calendar-chip')).toHaveLength(0);
    expect(chipsOn(busy)).toHaveLength(1);
  });

  /**
   * A day in the grid is read the same way down as a day in the week strip: what claims no time
   * of day first, in the order the answer gave, then what claims one, earliest first. The answer
   * arranges rows by the day they fall on and separates rows sharing a day only by an identifier
   * that means nothing, so there is no order inside a day for the grid to have preserved — and
   * two views of the same Saturday that disagreed about which trip came first would be worse than
   * either of them alone.
   */
  it('reads a day down by the time each record claims, whichever way the answer listed them', () => {
    const day = inThisMonth(8);
    answer([
      row({ id: 'a', title: 'Zulu', start: day, startTime: '18:00:00' }),
      row({ id: 'b', title: 'Alpha', start: day, startTime: '07:00:00' }),
      row({ id: 'c', title: 'Ponorul camp', start: day }),
    ]);
    showMonth();

    expect(chipsOn(day).map((chip) => chip.textContent)).toEqual([
      'Ponorul camp',
      '07:00 Alpha',
      '18:00 Zulu',
    ]);
  });

  /** A grid is drawn over the days it shows, so the window it asks for is the panel's own. */
  it('asks for the month it is showing rather than the window the record used', () => {
    showMonth();

    const asked = lastParams();
    expect(asked.from <= dayjs().startOf('month').format('YYYY-MM-DD')).toBe(true);
    expect(asked.to >= dayjs().endOf('month').format('YYYY-MM-DD')).toBe(true);
  });

  /**
   * A year is twelve cells an inch wide, so each says how much is in it rather than naming any of
   * it — a record that spans two months is counted in both, for the same reason it is drawn in
   * every day it covers.
   */
  it('counts a year by month rather than naming what is in it', () => {
    const day = dayjs().startOf('year').add(1, 'month');
    answer([
      row({ id: 'a', start: day.format('YYYY-MM-DD') }),
      row({ id: 'b', start: day.add(2, 'day').format('YYYY-MM-DD') }),
    ]);
    show();
    fireEvent.click(screen.getByText('Year'));

    const cell = screen.getByTestId(`calendar-month-${day.format('YYYY-MM')}`);
    expect(cell.textContent).toContain('2');
  });

  /**
   * An empty grid cannot say why it is empty. The sentence that can is drawn above it, so a read
   * that failed is never mistaken for a stretch of days with nothing on them.
   */
  it('says why a grid is empty rather than leaving blank cells to say it', () => {
    answer([]);
    showMonth();

    expect(screen.getByTestId('calendar-empty').textContent).toContain('Nothing is recorded');
  });
});

/** A day of the week the page opens on, so a fixture falls in the strip rather than beside it. */
const inThisWeek = (offsetDays: number): string =>
  dayjs().startOf('week').add(offsetDays, 'day').format('YYYY-MM-DD');

function showWeek() {
  const rendered = show();
  fireEvent.click(screen.getByText('Week'));
  return rendered;
}

/** Every chip drawn in one day's column of the strip, in the order the strip drew them. */
function chipsInColumn(day: string): HTMLElement[] {
  return within(screen.getByTestId(`calendar-week-day-${day}`)).queryAllByTestId('calendar-chip');
}

describe('the calendar as a week of days', () => {
  /**
   * The strip covers the week the day it is given falls in, and that week is the reader's
   * language's week — Monday-first in Romanian, Sunday-first in English — decided once by the
   * date library for every date this application draws. Seven columns, and the window asked for
   * is those same seven days: a strip drawn over days the answer did not cover would have columns
   * saying "nothing is happening" about days nobody asked about.
   */
  it('draws the seven days of the week it is showing, and asks for exactly those days', () => {
    showWeek();

    const days = Array.from({ length: 7 }, (_, offset) => inThisWeek(offset));
    days.forEach((day) => {
      expect(screen.getByTestId(`calendar-week-day-${day}`)).toBeTruthy();
    });
    expect(screen.queryByTestId(`calendar-week-day-${inThisWeek(7)}`)).toBeNull();
    expect(screen.queryByTestId(`calendar-week-day-${inThisWeek(-1)}`)).toBeNull();

    expect(lastParams().from).toBe(days[0]);
    expect(lastParams().to).toBe(days[6]);
  });

  /**
   * A day is read down: the things that claim no time of day first, then the ones that do, at
   * their time. Most records here claim none — a trip states the times its party was under ground
   * and a camp states days — so a column that ordered only on times would have nowhere to put
   * most of what is on it. The untimed row is asserted present, because losing it is the failure
   * this guards.
   */
  it('stacks a day by the time each record claims, and keeps the ones that claim none', () => {
    const day = inThisWeek(3);
    answer([
      row({ id: 'a', title: 'Zulu', start: day, startTime: '18:00:00' }),
      row({ id: 'b', title: 'Ponorul camp', start: day }),
      row({ id: 'c', title: 'Alpha', start: day, startTime: '07:00:00' }),
    ]);
    showWeek();

    expect(chipsInColumn(day).map((chip) => chip.textContent)).toEqual([
      'Ponorul camp',
      '07:00 Alpha',
      '18:00 Zulu',
    ]);
  });

  /**
   * A record lasting several days stands in every column it covers, marked where it begins and
   * where it ends, and states its time only in the column it begins in — it started once, not
   * once a morning.
   */
  it('stands a record in every day of the week it covers', () => {
    const first = inThisWeek(1);
    const last = inThisWeek(4);
    answer([row({ title: 'Ponorul camp', start: first, end: last, startTime: '09:00:00' })]);
    showWeek();

    const marks = [1, 2, 3, 4].map((offset) => {
      const chip = chipsInColumn(inThisWeek(offset))[0];
      expect(chip).toBeTruthy();
      return [chip.textContent, chip.dataset.spanStart, chip.dataset.spanEnd];
    });

    expect(marks).toEqual([
      ['09:00 Ponorul camp', 'true', 'false'],
      ['Ponorul camp', 'false', 'false'],
      ['Ponorul camp', 'false', 'false'],
      ['Ponorul camp', 'false', 'true'],
    ]);
  });

  /** Moving a week moves the days drawn and the days asked for together. */
  it('moves a week at a time', () => {
    showWeek();
    fireEvent.click(screen.getByTestId('calendar-week-next'));

    expect(screen.getByTestId(`calendar-week-day-${inThisWeek(7)}`)).toBeTruthy();
    expect(lastParams().from).toBe(inThisWeek(7));
    expect(lastParams().to).toBe(inThisWeek(13));
  });
});

describe('the calendar as an agenda', () => {
  /**
   * The agenda is the record read forwards: the same answer, the same paging and the same click
   * through to the record, with each row drawn as one entry instead of as a set of cells to
   * compare across. The column headings go with the cells — there is nothing left to head.
   */
  it('draws each row as one entry rather than as a row of cells', () => {
    answer([row({ title: 'Coiba Mare recce', start: '2026-09-05', startTime: '08:30:00' })]);
    show();
    fireEvent.click(screen.getByText('Agenda'));

    const entry = screen.getByTestId('calendar-agenda-row');
    expect(entry.textContent).toContain('Coiba Mare recce');
    expect(entry.textContent).toContain('08:30');
    expect(entry.textContent).toContain('Trip');
    expect(screen.queryByText('When')).toBeNull();
  });

  /** The window stays the reader's own: the agenda is a way of reading the record, not of days. */
  it('keeps the window the reader picked, and the way through to the record', () => {
    answer([row({ id: 'a', source: 'expedition', title: 'Ponorul camp' })]);
    show();
    const chosenBefore = lastParams();
    fireEvent.click(screen.getByText('Agenda'));

    expect(lastParams().from).toBe(chosenBefore.from);
    expect(lastParams().to).toBe(chosenBefore.to);
    // The range control names itself on each end of the window it offers.
    expect(screen.getAllByTestId('calendar-window').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByText('Ponorul camp'));
    expect(navigateSpy).toHaveBeenCalledWith('/expeditions/a');
  });
});
