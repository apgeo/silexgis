// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { CalendarEntry } from '../../api/hooks.ts';
import { entryKey } from './calendarDays.ts';
import {
  ROWS_BEFORE_TODAY,
  TodayKey,
  chronologyOf,
  groupKey,
  groupRecord,
  groupValueOf,
  layoutRecord,
  type RecordLayoutOptions,
  type RecordRow,
} from './recordRows.ts';

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

/** A Monday-first week, stated here so no test depends on which language the runner started in. */
const mondayWeekOf = (day: string): string => {
  const [year, month, date] = day.split('-').map(Number);
  const at = new Date(Date.UTC(year, month - 1, date));
  at.setUTCDate(at.getUTCDate() - ((at.getUTCDay() + 6) % 7));
  return at.toISOString().slice(0, 10);
};

/** The clock every case below is read against: a Wednesday in the middle of a month. */
const TODAY = '2026-10-14';

const options = (overrides: Partial<RecordLayoutOptions> = {}): RecordLayoutOptions => ({
  grouping: 'none',
  order: 'ascending',
  collapsed: new Set(),
  today: TODAY,
  from: '2026-09-01',
  to: '2026-12-31',
  weekOf: mondayWeekOf,
  ...overrides,
});

/** A layout as the words a reader would see down the list: a title, a heading, or the line. */
const drawn = (rows: readonly RecordRow[]): string[] =>
  rows.map((row) =>
    row.type === 'entry'
      ? row.entry.title
      : row.type === 'group'
        ? `[${row.group.value}:${row.group.entries.length}${row.collapsed ? ' folded' : ''}]`
        : '— today —',
  );

describe('what a row is grouped under', () => {
  it('reads each grouping off the row itself', () => {
    const row = entry('2026-10-14', {
      source: 'event',
      kind: 'training',
      state: 'confirmed',
      cavingGroupId: 'club-a',
    });

    expect(groupValueOf(row, 'month', mondayWeekOf)).toBe('2026-10');
    expect(groupValueOf(row, 'week', mondayWeekOf)).toBe('2026-10-12');
    expect(groupValueOf(row, 'kind', mondayWeekOf)).toBe('training');
    expect(groupValueOf(row, 'state', mondayWeekOf)).toBe('confirmed');
    expect(groupValueOf(row, 'cavingGroup', mondayWeekOf)).toBe('club-a');
  });

  /** What the Kind column says: the row's own kind where it has one, its family otherwise. */
  it('groups a row with no kind under its family', () => {
    expect(groupValueOf(entry('2026-10-14'), 'kind')).toBe('tripLog');
    expect(groupValueOf(entry('2026-10-14', { source: 'expedition' }), 'kind')).toBe('expedition');
  });

  /**
   * A record that runs over several days is filed once, under the day it begins. Filing a camp
   * across the end of a month under both would make the headings count more rows than there are.
   */
  it('files a record that runs on under the day it begins, and only there', () => {
    const camp = entry('2026-09-28', { source: 'expedition', end: '2026-10-04' });

    expect(groupValueOf(camp, 'month', mondayWeekOf)).toBe('2026-09');
    expect(groupValueOf(camp, 'week', mondayWeekOf)).toBe('2026-09-28');

    const groups = groupRecord([camp, entry('2026-10-02')], 'month', { order: 'ascending' });
    expect(groups.map((group) => [group.value, group.entries.length])).toEqual([
      ['2026-09', 1],
      ['2026-10', 1],
    ]);
  });

  /** A row carrying nothing to be grouped by is counted under a heading of its own. */
  it('keeps a row that names no group, under the empty value', () => {
    expect(groupValueOf(entry('2026-10-14'), 'cavingGroup')).toBe('');
  });
});

describe('the headings a list is cut into', () => {
  it('puts every row under exactly one heading, in the order the answer gave them', () => {
    const rows = [
      entry('2026-09-03', { title: 'a' }),
      entry('2026-10-01', { title: 'b' }),
      entry('2026-09-20', { title: 'c' }),
      entry('2026-10-09', { title: 'd' }),
    ];

    const groups = groupRecord(rows, 'month', { order: 'ascending' });

    expect(groups.map((group) => group.entries.map((row) => row.title))).toEqual([
      ['a', 'c'],
      ['b', 'd'],
    ]);
    expect(groups.reduce((sum, group) => sum + group.entries.length, 0)).toBe(rows.length);
  });

  /** A heading that is a stretch of time follows the way the answer runs. */
  it('runs months and weeks the way the answer runs', () => {
    const rows = [entry('2026-11-02'), entry('2026-09-03'), entry('2026-10-01')];

    expect(groupRecord(rows, 'month', { order: 'ascending' }).map((g) => g.value)).toEqual([
      '2026-09',
      '2026-10',
      '2026-11',
    ]);
    expect(groupRecord(rows, 'month', { order: 'descending' }).map((g) => g.value)).toEqual([
      '2026-11',
      '2026-10',
      '2026-09',
    ]);
    expect(
      groupRecord(rows, 'week', { order: 'ascending', weekOf: mondayWeekOf }).map((g) => g.value),
    ).toEqual(['2026-08-31', '2026-09-28', '2026-11-02']);
    // Ordered by title the answer runs through no time at all, and the months still read forwards.
    expect(groupRecord(rows, 'month', { order: null }).map((g) => g.value)).toEqual([
      '2026-09',
      '2026-10',
      '2026-11',
    ]);
  });

  /**
   * A heading that is a word follows its vocabulary rather than the alphabet, which would
   * reorder the headings with the interface language: the families first, then the kinds as the
   * filter beside the list offers them.
   */
  it('runs kinds in the order the families and the kinds are offered in', () => {
    const rows = [
      entry('2026-10-01', { source: 'event', kind: 'deadline' }),
      entry('2026-10-02', { source: 'event', kind: 'clubMeeting' }),
      entry('2026-10-03', { source: 'expedition' }),
      entry('2026-10-04'),
      entry('2026-10-05', { source: 'event', kind: 'training' }),
    ];

    expect(groupRecord(rows, 'kind').map((group) => group.value)).toEqual([
      'tripLog',
      'expedition',
      'clubMeeting',
      'training',
      'deadline',
    ]);
  });

  it('runs states in the order a record moves through them', () => {
    const rows = [
      entry('2026-10-01', { state: 'cancelled' }),
      entry('2026-10-02', { state: 'done' }),
      entry('2026-10-03', { state: 'proposed' }),
      entry('2026-10-04', { state: 'planned' }),
    ];

    expect(groupRecord(rows, 'state').map((group) => group.value)).toEqual([
      'proposed',
      'planned',
      'done',
      'cancelled',
    ]);
  });

  /**
   * Caving groups go by name. A group the reader cannot list still has rows and is drawn after
   * the named ones, and the rows that name no group come last of all, under their own heading.
   */
  it('runs caving groups by name, then the unnamed, then the rows that name none', () => {
    const rows = [
      entry('2026-10-01'),
      entry('2026-10-02', { cavingGroupId: 'id-z' }),
      entry('2026-10-03', { cavingGroupId: 'id-hidden' }),
      entry('2026-10-04', { cavingGroupId: 'id-a' }),
      entry('2026-10-05', { cavingGroupId: 'id-z' }),
    ];
    const groupNames = new Map([
      ['id-z', 'Avenul'],
      ['id-a', 'Zarand'],
    ]);

    const groups = groupRecord(rows, 'cavingGroup', { order: 'ascending', groupNames });

    expect(groups.map((group) => [group.value, group.entries.length])).toEqual([
      ['id-z', 2],
      ['id-a', 1],
      ['id-hidden', 1],
      ['', 1],
    ]);
  });
});

describe('which way an order runs through time', () => {
  /** The server answers anything it does not recognise in its own order, which is by day. */
  it('reads the server’s words, and anything else as the server’s own order', () => {
    expect(chronologyOf(undefined)).toBe('ascending');
    expect(chronologyOf('start')).toBe('ascending');
    expect(chronologyOf('cavesWithheld')).toBe('ascending');
    expect(chronologyOf('-start')).toBe('descending');
    expect(chronologyOf('title')).toBeNull();
    expect(chronologyOf('-title')).toBeNull();
  });
});

describe('the line marking today, and the row a list opens on', () => {
  /** Seven records before today and seven from today on, a day apart. */
  const fortnight = () => [
    ...[7, 8, 9, 10, 11, 12, 13].map((day) => entry(`2026-10-${String(day).padStart(2, '0')}`)),
    ...[14, 15, 16, 17, 18, 19, 20].map((day) => entry(`2026-10-${day}`)),
  ];

  it('draws the line where the run crosses today, and opens five rows above it', () => {
    const rows = fortnight();

    const layout = layoutRecord(rows, options());

    expect(layout.chronological).toBe(true);
    const lines = drawn(layout.rows);
    expect(lines.indexOf('— today —')).toBe(7);
    // What began before today is above the line, and what begins today is below it.
    expect(lines[6]).toBe(rows[6].title);
    expect(lines[8]).toBe(rows[7].title);
    // Five rows above the line, so the last things that happened are in sight with the next.
    expect(layout.anchorKey).toBe(entryKey(rows[7 - ROWS_BEFORE_TODAY]));
  });

  /** The clock is an argument: the same rows read a week later cross somewhere else. */
  it('moves with the clock', () => {
    const rows = fortnight();

    const later = layoutRecord(rows, options({ today: '2026-10-18' }));

    expect(drawn(later.rows).indexOf('— today —')).toBe(11);
    expect(later.anchorKey).toBe(entryKey(rows[11 - ROWS_BEFORE_TODAY]));
  });

  /** With fewer rows above the line than are kept in sight, the list opens at its first row. */
  it('opens at the top when little has happened yet', () => {
    const rows = [entry('2026-10-12'), entry('2026-10-13'), entry('2026-10-20')];

    const layout = layoutRecord(rows, options());

    expect(drawn(layout.rows)).toEqual([rows[0].title, rows[1].title, '— today —', rows[2].title]);
    expect(layout.anchorKey).toBe(entryKey(rows[0]));
  });

  /** Nothing before today: the line is the first thing drawn, and the list opens on it. */
  it('draws the line first when nothing in the window began before today', () => {
    const rows = [entry('2026-10-14'), entry('2026-10-20'), entry('2026-11-02')];

    const layout = layoutRecord(rows, options());

    expect(drawn(layout.rows)[0]).toBe('— today —');
    expect(layout.anchorKey).toBe(TodayKey);
  });

  /**
   * Nothing from today on: the line is the last thing drawn, under everything that has happened,
   * and the list opens on the last few of those rather than at the top of a past the reader
   * would have to scroll through to find out there is nothing ahead.
   */
  it('draws the line last when nothing is still to come, and opens on the latest rows', () => {
    const rows = [1, 2, 3, 4, 5, 6, 7, 8].map((day) => entry(`2026-10-0${day}`));

    const layout = layoutRecord(rows, options());

    const lines = drawn(layout.rows);
    expect(lines.at(-1)).toBe('— today —');
    expect(layout.anchorKey).toBe(entryKey(rows[8 - ROWS_BEFORE_TODAY]));
  });

  /**
   * The line goes by the day a record begins, because that is what the list is ordered by. A
   * camp that began last week and is still running stays above the line, where its row was.
   */
  it('keeps a record that is still running above the line it began before', () => {
    const camp = entry('2026-10-10', { source: 'expedition', end: '2026-10-20' });
    const tonight = entry('2026-10-14');

    const layout = layoutRecord([camp, tonight], options());

    expect(drawn(layout.rows)).toEqual([camp.title, '— today —', tonight.title]);
  });

  /** Latest-first, the same line the other way up: what is coming above it, what happened below. */
  it('draws the line the other way up for an order that runs latest-first', () => {
    const rows = fortnight().reverse();

    const layout = layoutRecord(rows, options({ order: 'descending' }));

    const lines = drawn(layout.rows);
    expect(lines.indexOf('— today —')).toBe(7);
    expect(lines[6]).toBe(rows[6].title);
    expect(rows[6].start).toBe('2026-10-14');
    expect(lines[8]).toBe(rows[7].title);
    expect(layout.anchorKey).toBe(entryKey(rows[7 - ROWS_BEFORE_TODAY]));
  });

  /**
   * Over a window that does not hold today every row is on one side of it, and a line under all
   * of them would say "today" about a list today is not in. No line, and nowhere to open but the
   * top.
   */
  it('draws no line over a window that does not hold today', () => {
    const rows = [entry('2025-03-01'), entry('2025-03-09')];

    const layout = layoutRecord(rows, options({ from: '2025-03-01', to: '2025-03-31' }));

    expect(drawn(layout.rows)).toEqual([rows[0].title, rows[1].title]);
    expect(layout.anchorKey).toBeNull();
    // The arrangement is still one a line could be drawn in; it is the window that has no today.
    expect(layout.chronological).toBe(true);
  });

  /** Ordered by title the list runs through no time, so "today" names no place in it. */
  it('draws no line in a list ordered by title', () => {
    const layout = layoutRecord(fortnight(), options({ order: null }));

    expect(drawn(layout.rows)).not.toContain('— today —');
    expect(layout.anchorKey).toBeNull();
    expect(layout.chronological).toBe(false);
  });

  it('draws nothing at all for no rows', () => {
    const layout = layoutRecord([], options());

    expect(layout.rows).toEqual([]);
    expect(layout.anchorKey).toBeNull();
  });
});

describe('the line marking today under headings', () => {
  /** September, October either side of today, and November. */
  const autumn = () => [
    entry('2026-09-05', { title: 'sep-a' }),
    entry('2026-09-19', { title: 'sep-b' }),
    entry('2026-10-03', { title: 'oct-a' }),
    entry('2026-10-10', { title: 'oct-b' }),
    entry('2026-10-17', { title: 'oct-c' }),
    entry('2026-10-24', { title: 'oct-d' }),
    entry('2026-11-07', { title: 'nov-a' }),
  ];

  /**
   * Under headings that are stretches of time the list is still one run through the days, so
   * the line is drawn inside the month it falls in — and a heading takes a line of the list's
   * height like any other, so it is counted among the rows kept in sight above.
   */
  it('draws the line inside the month today falls in, counting headings as lines', () => {
    const rows = autumn();

    const layout = layoutRecord(rows, options({ grouping: 'month' }));

    expect(drawn(layout.rows)).toEqual([
      '[2026-09:2]',
      'sep-a',
      'sep-b',
      '[2026-10:4]',
      'oct-a',
      'oct-b',
      '— today —',
      'oct-c',
      'oct-d',
      '[2026-11:1]',
      'nov-a',
    ]);
    // Five lines above the line: two of October's rows, its heading, and the end of September.
    expect(layout.anchorKey).toBe(entryKey(rows[0]));
    expect(layout.groups.map((group) => group.value)).toEqual(['2026-09', '2026-10', '2026-11']);
  });

  /** A heading stays with its rows: the line goes above a group that lies wholly past it. */
  it('draws the line above the heading of a month that is wholly still to come', () => {
    const rows = [
      entry('2026-10-03', { title: 'oct-a' }),
      entry('2026-10-10', { title: 'oct-b' }),
      entry('2026-11-07', { title: 'nov-a' }),
    ];

    const layout = layoutRecord(rows, options({ grouping: 'month' }));

    expect(drawn(layout.rows)).toEqual([
      '[2026-10:2]',
      'oct-a',
      'oct-b',
      '— today —',
      '[2026-11:1]',
      'nov-a',
    ]);
  });

  /**
   * A folded group shows no rows to draw a line between. One that holds anything from today on
   * is treated as lying on that side, so the line goes above its heading rather than below a
   * heading whose hidden rows are still to come.
   */
  it('draws the line above a folded group that reaches today', () => {
    const layout = layoutRecord(
      autumn(),
      options({ grouping: 'month', collapsed: new Set(['2026-10']) }),
    );

    expect(drawn(layout.rows)).toEqual([
      '[2026-09:2]',
      'sep-a',
      'sep-b',
      '— today —',
      '[2026-10:4 folded]',
      '[2026-11:1]',
      'nov-a',
    ]);
    // Folded, a group still says how many rows it holds.
    expect(layout.rows.find((row) => row.key === groupKey('2026-10'))).toMatchObject({
      type: 'group',
      collapsed: true,
    });
  });

  it('leaves a folded group that is wholly behind above the line', () => {
    const layout = layoutRecord(
      autumn(),
      options({ grouping: 'month', collapsed: new Set(['2026-09']) }),
    );

    expect(drawn(layout.rows).slice(0, 5)).toEqual([
      '[2026-09:2 folded]',
      '[2026-10:4]',
      'oct-a',
      'oct-b',
      '— today —',
    ]);
  });

  it('draws the line under weeks as it does under months', () => {
    const rows = [
      entry('2026-10-06', { title: 'last-week' }),
      entry('2026-10-12', { title: 'monday' }),
      entry('2026-10-15', { title: 'thursday' }),
    ];

    const layout = layoutRecord(rows, options({ grouping: 'week' }));

    expect(drawn(layout.rows)).toEqual([
      '[2026-10-05:1]',
      'last-week',
      '[2026-10-12:2]',
      'monday',
      '— today —',
      'thursday',
    ]);
  });

  it('draws the line the other way up under months that run latest-first', () => {
    const rows = autumn().reverse();

    const layout = layoutRecord(rows, options({ grouping: 'month', order: 'descending' }));

    expect(drawn(layout.rows)).toEqual([
      '[2026-11:1]',
      'nov-a',
      '[2026-10:4]',
      'oct-d',
      'oct-c',
      '— today —',
      'oct-b',
      'oct-a',
      '[2026-09:2]',
      'sep-b',
      'sep-a',
    ]);
  });

  /**
   * Cut by kind, by state or by caving group the list is several runs through the days, one
   * under each heading, and "today" names no single place in it: no line, and nowhere to open
   * but the top.
   */
  it('draws no line under headings that are not stretches of time', () => {
    for (const grouping of ['kind', 'state', 'cavingGroup'] as const) {
      const layout = layoutRecord(autumn(), options({ grouping }));

      expect(drawn(layout.rows)).not.toContain('— today —');
      expect(layout.anchorKey).toBeNull();
      expect(layout.chronological).toBe(false);
    }
  });

  it('folds a group away to its heading and keeps the others', () => {
    const layout = layoutRecord(
      autumn(),
      options({ grouping: 'kind', collapsed: new Set(['tripLog']) }),
    );

    expect(drawn(layout.rows)).toEqual(['[tripLog:7 folded]']);
  });

  /**
   * The list is laid out again each time a heading is folded, from rows that have not changed. A
   * table redraws a row when it is handed a different line for it, so a record's line is the
   * same line from one layout to the next — folding one heading away must not hand the table a
   * window's worth of new ones.
   */
  it('hands back the same line for the same record when the list is laid out again', () => {
    const rows = autumn();
    const lineFor = (layout: { rows: readonly RecordRow[] }, title: string) =>
      layout.rows.find((row) => row.type === 'entry' && row.entry.title === title);

    const open = layoutRecord(rows, options({ grouping: 'month' }));
    const folded = layoutRecord(rows, options({ grouping: 'month', collapsed: new Set(['2026-11']) }));
    const regrouped = layoutRecord(rows, options({ grouping: 'kind' }));

    expect(lineFor(open, 'sep-a')).toBeDefined();
    expect(lineFor(folded, 'sep-a')).toBe(lineFor(open, 'sep-a'));
    expect(lineFor(regrouped, 'oct-c')).toBe(lineFor(open, 'oct-c'));
    // A different record, even one that says all the same things, has a line of its own.
    const twin = { ...rows[0] };
    expect(layoutRecord([twin], options()).rows[0]).not.toBe(lineFor(open, 'sep-a'));
  });
});
