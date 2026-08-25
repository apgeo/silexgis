// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CalendarEntry, CalendarParams } from '../../api/hooks.ts';

const { calendarSpy } = vi.hoisted(() => ({ calendarSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCalendar: (params: CalendarParams, options?: { enabled?: boolean }) =>
    calendarSpy(params, options),
  useCavingGroups: () => ({ data: [] }),
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
  it('names one family when only one is wanted, and asks nothing when neither is', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-other'));
    expect(lastParams().source).toBe('tripLog');

    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    expect(lastEnabled()).toBe(false);
    expect(screen.getByTestId('calendar-empty').textContent).toContain('No kind of record');
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
