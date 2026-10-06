// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CalendarEntry } from '../../api/hooks.ts';
import CalendarRecordTable from './CalendarRecordTable.tsx';

/**
 * The list as a reader meets it: the rows, and the headings they sit under. Which rows sit under
 * which heading is asked of the layout directly in its own tests; what is asked here is that the
 * list draws that answer, folds it and names it.
 */

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

/** A fortnight of records, a day apart. */
const fortnight = (): CalendarEntry[] =>
  [7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20].map((day) =>
    entry(`2026-10-${String(day).padStart(2, '0')}`),
  );

const opened = vi.fn();
const sortAsked = vi.fn();
const groupingAsked = vi.fn();

type Props = Parameters<typeof CalendarRecordTable>[0];

function props(overrides: Partial<Props> = {}): Props {
  return {
    view: 'record',
    entries: fortnight(),
    loading: false,
    emptyText: 'Nothing is recorded in these days.',
    sort: undefined,
    onSortChange: sortAsked,
    grouping: 'none',
    onGroupingChange: groupingAsked,
    groupNames: new Map(),
    onOpen: opened,
    ...overrides,
  };
}

/** The lines of the list as drawn, by what each one is. */
function lines(): { kind: string; text: string }[] {
  return Array.from(document.querySelectorAll<HTMLElement>('tbody tr.ant-table-row')).map((row) => ({
    kind: Array.from(row.classList)
      .find((name) => /^calendar-row-(entry|group)$/.test(name))!
      .replace('calendar-row-', ''),
    text: row.textContent ?? '',
  }));
}

const kinds = (): string[] => lines().map((line) => line.kind);

beforeEach(() => {
  for (const spy of [opened, sortAsked, groupingAsked]) {
    spy.mockReset();
  }
});

afterEach(cleanup);

describe('the list of a window', () => {
  it('draws every row in hand, with no pages between them', () => {
    const many = Array.from({ length: 60 }, (_, index) =>
      entry(`2026-11-${String((index % 28) + 1).padStart(2, '0')}`),
    );
    render(<CalendarRecordTable {...props({ entries: many })} />);

    expect(kinds().filter((kind) => kind === 'entry')).toHaveLength(60);
    expect(document.querySelector('.ant-pagination')).toBeNull();
  });

  it('opens a record from its row', () => {
    const rows = fortnight();
    render(<CalendarRecordTable {...props({ entries: rows })} />);

    fireEvent.click(screen.getByText(rows[3].title));
    expect(opened).toHaveBeenCalledTimes(1);
    expect(opened).toHaveBeenLastCalledWith(rows[3]);
  });

  it('says why there is nothing, when there is nothing', () => {
    render(<CalendarRecordTable {...props({ entries: [], emptyText: 'Nothing in these days matches.' })} />);

    expect(screen.getByTestId('calendar-empty').textContent).toContain('Nothing in these days matches.');
  });

  /**
   * While the first answer is still coming there are no rows, and that is not the same as there
   * being nothing: the sentence that says a stretch of days is empty is a claim about those
   * days, and a question still in flight has not earned it.
   */
  it('claims nothing about the days while the first answer is still coming', () => {
    const view = render(
      <CalendarRecordTable
        {...props({ entries: [], loading: true, emptyText: 'Nothing is recorded.' })}
      />,
    );
    expect(screen.queryByTestId('calendar-empty')).toBeNull();

    view.rerender(
      <CalendarRecordTable {...props({ entries: [], loading: false, emptyText: 'Nothing is recorded.' })} />,
    );
    expect(screen.getByTestId('calendar-empty').textContent).toContain('Nothing is recorded.');
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
      'group', 'entry', 'entry', 'entry',
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
   * The agenda is the record read forwards: the same rows under the same headings, with each row
   * drawn as one entry and no header over a single column of them.
   */
  it('keeps the headings, one entry to a row', () => {
    const rows: CalendarEntry[] = [
      entry('2026-10-03', { title: 'oct-a', startTime: '08:30:00' }),
      entry('2026-10-17', { title: 'oct-c' }),
    ];
    render(
      <CalendarRecordTable {...props({ entries: rows, grouping: 'month', view: 'agenda' })} />,
    );

    expect(kinds()).toEqual(['group', 'entry', 'entry']);
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
