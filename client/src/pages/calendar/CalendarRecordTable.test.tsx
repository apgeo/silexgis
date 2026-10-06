// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CalendarEntry } from '../../api/hooks.ts';
import CalendarRecordTable from './CalendarRecordTable.tsx';

/**
 * The list as a reader meets it: the rows under their headings, the line marking today, and the
 * one thing no drawing shows — when the list is moved to today and when it is left alone.
 *
 * Nothing here lays a page out, so where the list is moved *to* is read off the row it marks as
 * the one to open on, and *whether* it was moved is read off the one call that moves it. Which
 * row that should be, for which rows on which day, is asked of the layout directly in its own
 * tests; what is asked here is that the list acts on the answer at the right moments only.
 */

/** The clock every case is read against: the middle of a Wednesday, the fourteenth of October. */
const NOW = new Date(2026, 9, 14, 12, 0, 0);

let serial = 0;
function entry(start: string, overrides: Partial<CalendarEntry> = {}): CalendarEntry {
  serial += 1;
  const base: CalendarEntry = {
    source: 'tripLog',
    id: `id-${serial}`,
    title: `Row ${serial}`,
    start,
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

/** Seven records before today and seven from today on, a day apart. */
const fortnight = (): CalendarEntry[] =>
  [7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20].map((day) =>
    entry(`2026-10-${String(day).padStart(2, '0')}`),
  );

const scrolled = vi.fn();
const opened = vi.fn();
const sortAsked = vi.fn();
const groupingAsked = vi.fn();
const todayAsked = vi.fn();

type Props = Parameters<typeof CalendarRecordTable>[0];

function props(overrides: Partial<Props> = {}): Props {
  return {
    view: 'record',
    entries: fortnight(),
    loading: false,
    emptyText: 'Nothing is recorded in these days.',
    from: '2026-09-01',
    to: '2026-12-31',
    sort: undefined,
    onSortChange: sortAsked,
    grouping: 'none',
    onGroupingChange: groupingAsked,
    groupNames: new Map(),
    question: 'q1',
    onOpen: opened,
    onShowToday: todayAsked,
    ...overrides,
  };
}

/** The lines of the list as drawn, by what each one is. */
function lines(): { kind: string; text: string; anchor: boolean }[] {
  return Array.from(document.querySelectorAll<HTMLElement>('tbody tr.ant-table-row')).map((row) => ({
    kind: Array.from(row.classList)
      .find((name) => /^calendar-row-(entry|group|today)$/.test(name))!
      .replace('calendar-row-', ''),
    text: row.textContent ?? '',
    anchor: row.classList.contains('calendar-row-anchor'),
  }));
}

const kinds = (): string[] => lines().map((line) => line.kind);
/** Where the row the list opens on is, counted from the top. */
const anchorAt = (): number => lines().findIndex((line) => line.anchor);

beforeEach(() => {
  // Only the date is pinned: the component library keeps real timers of its own.
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
  for (const spy of [scrolled, opened, sortAsked, groupingAsked, todayAsked]) {
    spy.mockReset();
  }
  // The body of the list is moved through this one call, on the element that scrolls.
  Element.prototype.scrollTo = scrolled as unknown as typeof Element.prototype.scrollTo;
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe('the list as one run through the days', () => {
  it('draws every row in hand, with no pages between them', () => {
    const many = Array.from({ length: 60 }, (_, index) =>
      entry(`2026-11-${String((index % 28) + 1).padStart(2, '0')}`),
    );
    render(<CalendarRecordTable {...props({ entries: many })} />);

    expect(kinds().filter((kind) => kind === 'entry')).toHaveLength(60);
    expect(document.querySelector('.ant-pagination')).toBeNull();
  });

  /** Above the line what began before today, below it what begins today or later. */
  it('draws a line where the run crosses today', () => {
    const rows = fortnight();
    render(<CalendarRecordTable {...props({ entries: rows })} />);

    const drawn = lines();
    const at = drawn.findIndex((line) => line.kind === 'today');
    expect(at).toBe(7);
    expect(drawn[6].text).toContain(rows[6].title);
    expect(drawn[8].text).toContain(rows[7].title);
    expect(screen.getByTestId('calendar-today-line').textContent).toContain('Today');
    // Spanning the list's whole width rather than sitting in its first column.
    const cell = screen.getByTestId('calendar-today-line').closest('td')!;
    expect(cell.getAttribute('colspan')).toBe('4');
  });

  it('draws no line over days that do not include today', () => {
    render(
      <CalendarRecordTable
        {...props({
          entries: [entry('2025-03-02'), entry('2025-03-09')],
          from: '2025-03-01',
          to: '2025-03-31',
        })}
      />,
    );

    expect(kinds()).toEqual(['entry', 'entry']);
    expect(screen.queryByTestId('calendar-today-line')).toBeNull();
  });

  it('opens a record from its row, and nothing from the line', () => {
    const rows = fortnight();
    render(<CalendarRecordTable {...props({ entries: rows })} />);

    fireEvent.click(screen.getByText(rows[3].title));
    expect(opened).toHaveBeenCalledTimes(1);
    expect(opened).toHaveBeenLastCalledWith(rows[3]);

    fireEvent.click(screen.getByTestId('calendar-today-line'));
    expect(opened).toHaveBeenCalledTimes(1);
  });

  it('says why there is nothing, when there is nothing', () => {
    render(<CalendarRecordTable {...props({ entries: [], emptyText: 'Nothing in these days matches.' })} />);

    expect(screen.getByTestId('calendar-empty').textContent).toContain('Nothing in these days matches.');
    expect(screen.queryByTestId('calendar-today-line')).toBeNull();
  });

  /**
   * While the first answer is still coming there are no rows, and that is not the same as there
   * being nothing: the sentence that says a stretch of days is empty is a claim about those
   * days, and a question still in flight has not earned it.
   */
  it('claims nothing about the days while the first answer is still coming', () => {
    const view = render(
      <CalendarRecordTable
        {...props({ entries: [], loading: true, question: null, emptyText: 'Nothing is recorded.' })}
      />,
    );
    expect(screen.queryByTestId('calendar-empty')).toBeNull();

    view.rerender(
      <CalendarRecordTable {...props({ entries: [], loading: false, emptyText: 'Nothing is recorded.' })} />,
    );
    expect(screen.getByTestId('calendar-empty').textContent).toContain('Nothing is recorded.');
  });
});

describe('where the list opens, and when it is moved', () => {
  /**
   * Five rows above the line, so the last things that happened and the next things coming are in
   * sight together — and moved once, when the answer arrives.
   */
  it('opens on today when the answer to the question is in hand', () => {
    render(<CalendarRecordTable {...props()} />);

    expect(scrolled).toHaveBeenCalledTimes(1);
    expect(scrolled).toHaveBeenLastCalledWith({ top: expect.any(Number) });
    const drawn = lines();
    expect(anchorAt()).toBe(drawn.findIndex((line) => line.kind === 'today') - 5);
  });

  /**
   * While the rows on screen are still the answer to the question before, there is nothing to
   * open on yet: moving now would move to a place in the old rows, and then again in the new.
   */
  it('waits for the answer before it moves', () => {
    const view = render(<CalendarRecordTable {...props({ question: null })} />);
    expect(scrolled).not.toHaveBeenCalled();

    view.rerender(<CalendarRecordTable {...props({ question: 'q1' })} />);
    expect(scrolled).toHaveBeenCalledTimes(1);
  });

  /**
   * The same question answered again — a record changed somewhere, the window regained focus —
   * is the same list, and moving it would take the reader's place away from them mid-read. The
   * rows change under them; where they are standing does not.
   */
  it('leaves the reader where they are when the same question is answered again', () => {
    const view = render(<CalendarRecordTable {...props()} />);
    expect(scrolled).toHaveBeenCalledTimes(1);

    view.rerender(
      <CalendarRecordTable {...props({ entries: [...fortnight(), entry('2026-10-21')] })} />,
    );
    view.rerender(<CalendarRecordTable {...props({ entries: fortnight().slice(2) })} />);
    // Even through a moment when the rows are being fetched again.
    view.rerender(<CalendarRecordTable {...props({ question: null })} />);
    view.rerender(<CalendarRecordTable {...props({ question: 'q1' })} />);

    expect(scrolled).toHaveBeenCalledTimes(1);
  });

  it('opens on today again for a different question', () => {
    const view = render(<CalendarRecordTable {...props()} />);

    view.rerender(<CalendarRecordTable {...props({ question: 'q2' })} />);

    expect(scrolled).toHaveBeenCalledTimes(2);
  });

  /**
   * A list regrouped, or read as an agenda, is a different list: the place somebody had in the
   * old one is nowhere in the new.
   */
  it('opens on today again when the same rows are arranged differently', () => {
    const view = render(<CalendarRecordTable {...props()} />);

    view.rerender(<CalendarRecordTable {...props({ grouping: 'month' })} />);
    expect(scrolled).toHaveBeenCalledTimes(2);

    view.rerender(<CalendarRecordTable {...props({ grouping: 'month', view: 'agenda' })} />);
    expect(scrolled).toHaveBeenCalledTimes(3);
  });

  /** Folding a heading away is the reader arranging what is in front of them, not a new list. */
  it('does not move when a heading is folded', () => {
    render(<CalendarRecordTable {...props({ grouping: 'month' })} />);
    expect(scrolled).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getAllByTestId('calendar-group')[0]);

    expect(scrolled).toHaveBeenCalledTimes(1);
  });

  it('goes back to today when asked', () => {
    render(<CalendarRecordTable {...props()} />);
    expect(scrolled).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByTestId('calendar-today'));

    expect(scrolled).toHaveBeenCalledTimes(2);
    expect(todayAsked).not.toHaveBeenCalled();
  });

  /**
   * Over a window that does not hold today there is no line to go to. The page is asked for a
   * window that does, and the list opens on today when that window's answer arrives.
   */
  it('asks for a window that holds today when the one in hand does not', () => {
    render(
      <CalendarRecordTable
        {...props({ entries: [entry('2025-03-02')], from: '2025-03-01', to: '2025-03-31' })}
      />,
    );
    scrolled.mockClear();

    fireEvent.click(screen.getByTestId('calendar-today'));

    expect(todayAsked).toHaveBeenCalledTimes(1);
    expect(scrolled).not.toHaveBeenCalled();
  });

  /**
   * A list in the order of its titles, or cut by kind, runs through no time: there is no one
   * place in it that is today, so the button cannot be used, and says why.
   */
  it('offers no way to today in an arrangement that has no today in it', () => {
    const view = render(<CalendarRecordTable {...props({ sort: 'title' })} />);

    const button = screen.getByTestId('calendar-today');
    expect(button).toHaveProperty('disabled', true);
    expect(screen.queryByTestId('calendar-today-line')).toBeNull();
    expect(anchorAt()).toBe(-1);
    // Said beside the button, and tied to it for a reader who is told rather than shown.
    const hint = screen.getByTestId('calendar-today-hint');
    expect(hint.textContent).toContain('no one place that is today');
    expect(button.getAttribute('aria-describedby')).toBe(hint.id);

    view.rerender(<CalendarRecordTable {...props({ grouping: 'kind' })} />);
    expect(screen.getByTestId('calendar-today')).toHaveProperty('disabled', true);
    expect(screen.getByTestId('calendar-today-hint')).toBeTruthy();

    view.rerender(<CalendarRecordTable {...props({ sort: '-start', entries: fortnight().reverse() })} />);
    expect(screen.getByTestId('calendar-today')).toHaveProperty('disabled', false);
    expect(screen.queryByTestId('calendar-today-hint')).toBeNull();
    expect(screen.getByTestId('calendar-today-line')).toBeTruthy();
  });
});

describe('the list under headings', () => {
  const autumn = (): CalendarEntry[] => [
    entry('2026-09-05', { title: 'sep-a' }),
    entry('2026-09-19', { title: 'sep-b' }),
    entry('2026-10-03', { title: 'oct-a' }),
    entry('2026-10-10', { title: 'oct-b' }),
    entry('2026-10-17', { title: 'oct-c' }),
    entry('2026-11-07', { title: 'nov-a' }),
  ];

  /** The heading the rows of a group sit under, as the words it shows. */
  const headings = (): string[] =>
    screen.getAllByTestId('calendar-group').map((heading) => heading.textContent ?? '');

  it('offers what the list can be grouped by, nothing first, and reports the choice', () => {
    render(<CalendarRecordTable {...props({ entries: autumn() })} />);

    const control = screen.getByTestId('calendar-group-by');
    expect(control.textContent).toContain('Nothing');
    fireEvent.mouseDown(control.querySelector('.ant-select-selector') ?? control);
    const offered = Array.from(document.querySelectorAll('.ant-select-item-option')).map(
      (option) => option.getAttribute('title'),
    );
    expect(offered).toEqual(['Nothing', 'Month', 'Week', 'Kind', 'State', 'Caving group']);

    fireEvent.click(document.querySelector('.ant-select-item-option[title="Month"]')!);
    expect(groupingAsked).toHaveBeenCalledWith('month');
  });

  /** A heading carries its count, always: a name alone says something was there, not how much. */
  it('puts the rows under a heading for each month, each with its count', () => {
    render(<CalendarRecordTable {...props({ entries: autumn(), grouping: 'month' })} />);

    expect(headings()).toEqual(['September 2026 (2)', 'October 2026 (3)', 'November 2026 (1)']);
    expect(kinds()).toEqual([
      'group', 'entry', 'entry',
      'group', 'entry', 'entry', 'today', 'entry',
      'group', 'entry',
    ]);
    // A heading spans the list's whole width.
    expect(screen.getAllByTestId('calendar-group')[0].closest('td')!.getAttribute('colspan')).toBe('4');
  });

  it('folds a heading away to its name and count, and opens it again', () => {
    render(<CalendarRecordTable {...props({ entries: autumn(), grouping: 'month' })} />);

    const september = screen.getAllByTestId('calendar-group')[0];
    expect(september.getAttribute('aria-expanded')).toBe('true');
    fireEvent.click(september);

    expect(screen.queryByText('sep-a')).toBeNull();
    expect(screen.queryByText('sep-b')).toBeNull();
    expect(screen.getByText('oct-a')).toBeTruthy();
    // Folded, it still says how many rows it holds.
    expect(headings()[0]).toBe('September 2026 (2)');
    expect(screen.getAllByTestId('calendar-group')[0].getAttribute('aria-expanded')).toBe('false');
    // And folding is not opening a record.
    expect(opened).not.toHaveBeenCalled();

    fireEvent.click(screen.getAllByTestId('calendar-group')[0]);
    expect(screen.getByText('sep-a')).toBeTruthy();
  });

  /** What was folded belongs to the grouping it was folded under. */
  it('starts with everything open when the grouping changes', () => {
    const rows = autumn();
    const view = render(<CalendarRecordTable {...props({ entries: rows, grouping: 'month' })} />);
    fireEvent.click(screen.getAllByTestId('calendar-group')[0]);
    expect(screen.queryByText('sep-a')).toBeNull();

    view.rerender(<CalendarRecordTable {...props({ entries: rows, grouping: 'state' })} />);
    expect(screen.getByText('sep-a')).toBeTruthy();

    view.rerender(<CalendarRecordTable {...props({ entries: rows, grouping: 'month' })} />);
    expect(screen.getByText('sep-a')).toBeTruthy();
  });

  it('names a week by the days it covers', () => {
    render(
      <CalendarRecordTable
        {...props({ entries: [entry('2026-10-13'), entry('2026-10-15')], grouping: 'week' })}
      />,
    );

    // The interface reads English here, whose week begins on a Sunday.
    expect(headings()).toEqual(['11 Oct – 17 Oct 2026 (2)']);
  });

  /** What the Kind column says: the family of a row that has no kind, the kind of one that has. */
  it('names a kind as the Kind column does', () => {
    render(
      <CalendarRecordTable
        {...props({
          entries: [
            entry('2026-10-01', { source: 'event', kind: 'deadline' }),
            entry('2026-10-02', { source: 'expedition' }),
            entry('2026-10-03'),
            entry('2026-10-04', { source: 'event', kind: 'training' }),
            entry('2026-10-05'),
          ],
          grouping: 'kind',
        })}
      />,
    );

    expect(headings()).toEqual(['Trip (2)', 'Camp (1)', 'Training (1)', 'Deadline (1)']);
    // Cut by kind the list is several runs through the days, and none of them is drawn a today.
    expect(screen.queryByTestId('calendar-today-line')).toBeNull();
  });

  /** In the order a record moves through them, whatever order the rows arrived in. */
  it('names a state by its own word, in the order a record moves through them', () => {
    render(
      <CalendarRecordTable
        {...props({
          entries: [
            entry('2026-10-01', { state: 'cancelled' }),
            entry('2026-10-02', { state: 'planned' }),
            entry('2026-10-03', { state: 'done' }),
            entry('2026-10-04', { state: 'planned' }),
          ],
          grouping: 'state',
        })}
      />,
    );

    expect(headings()).toEqual(['Planned (2)', 'Done (1)', 'Cancelled (1)']);
  });

  /**
   * Three different things, and three different words for them: a group the reader can list, a
   * group whose rows they may read while the group itself is not theirs to list, and a row that
   * names no group at all — which is counted under a heading of its own rather than dropped.
   */
  it('names a caving group, an unlisted one, and the rows that name none', () => {
    render(
      <CalendarRecordTable
        {...props({
          entries: [
            entry('2026-10-01'),
            entry('2026-10-02', { cavingGroupId: 'club-z' }),
            entry('2026-10-03', { cavingGroupId: 'club-hidden' }),
            entry('2026-10-04', { cavingGroupId: 'club-z' }),
          ],
          grouping: 'cavingGroup',
          groupNames: new Map([['club-z', 'Speo Zarand']]),
        })}
      />,
    );

    expect(headings()).toEqual([
      'Speo Zarand (2)',
      'A group you cannot list (1)',
      'Not recorded (1)',
    ]);
  });
});

describe('the list read as an agenda', () => {
  /**
   * The agenda is the record read forwards: the same rows, headings, line and place to open, with
   * each row drawn as one entry and no header over a single column of them.
   */
  it('keeps the headings and the line, one entry to a row', () => {
    const rows: CalendarEntry[] = [
      entry('2026-10-03', { title: 'oct-a', startTime: '08:30:00' }),
      entry('2026-10-17', { title: 'oct-c' }),
    ];
    render(
      <CalendarRecordTable {...props({ entries: rows, grouping: 'month', view: 'agenda' })} />,
    );

    expect(kinds()).toEqual(['group', 'entry', 'today', 'entry']);
    expect(screen.getAllByTestId('calendar-agenda-row')).toHaveLength(2);
    expect(screen.getAllByTestId('calendar-agenda-row')[0].textContent).toContain('08:30');
    expect(document.querySelector('thead')).toBeNull();
    // One column of whole rows, so a heading has nothing to span and is simply the row.
    const headingRow = screen.getByTestId('calendar-group').closest('tr')!;
    expect(headingRow.querySelectorAll('td')).toHaveLength(1);
  });
});

describe('the order of the list', () => {
  /** The order is the server's to make, so a click on a header is a request and not a reshuffle. */
  it('asks for an order rather than making one', () => {
    const view = render(<CalendarRecordTable {...props()} />);

    fireEvent.click(screen.getAllByText('When')[0]);
    expect(sortAsked).toHaveBeenLastCalledWith('-start');

    view.rerender(<CalendarRecordTable {...props({ sort: '-start' })} />);
    fireEvent.click(screen.getAllByText('When')[0]);
    // Back to the server's own order, which is the absence of a word.
    expect(sortAsked).toHaveBeenLastCalledWith(undefined);

    fireEvent.click(screen.getAllByText('What')[0]);
    expect(sortAsked).toHaveBeenLastCalledWith('title');
  });
});
