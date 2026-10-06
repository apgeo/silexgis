// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { CalendarEntry } from '../../api/hooks.ts';
import { entryKey } from './calendarDays.ts';
import {
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

const options = (overrides: Partial<RecordLayoutOptions> = {}): RecordLayoutOptions => ({
  grouping: 'none',
  order: 'ascending',
  collapsed: new Set(),
  weekOf: mondayWeekOf,
  ...overrides,
});

/** A layout as the words a reader would see down the list: a title, or a heading. */
const drawn = (rows: readonly RecordRow[]): string[] =>
  rows.map((row) =>
    row.type === 'entry'
      ? row.entry.title
      : `[${row.group.value}:${row.group.entries.length}${row.collapsed ? ' folded' : ''}]`,
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

describe('the lines of a list under headings', () => {
  const autumn = () => [
    entry('2026-09-05', { title: 'sep-a' }),
    entry('2026-09-19', { title: 'sep-b' }),
    entry('2026-10-03', { title: 'oct-a' }),
    entry('2026-10-10', { title: 'oct-b' }),
    entry('2026-10-17', { title: 'oct-c' }),
    entry('2026-10-24', { title: 'oct-d' }),
    entry('2026-11-07', { title: 'nov-a' }),
  ];

  it('is the rows as the answer gave them when nothing is grouped', () => {
    const rows = autumn();

    const layout = layoutRecord(rows, options());

    expect(drawn(layout.rows)).toEqual(rows.map((row) => row.title));
    expect(layout.groups).toEqual([]);
    expect(layout.rows[0].key).toBe(entryKey(rows[0]));
  });

  it('puts each heading above the rows it counts', () => {
    const layout = layoutRecord(autumn(), options({ grouping: 'month' }));

    expect(drawn(layout.rows)).toEqual([
      '[2026-09:2]',
      'sep-a',
      'sep-b',
      '[2026-10:4]',
      'oct-a',
      'oct-b',
      'oct-c',
      'oct-d',
      '[2026-11:1]',
      'nov-a',
    ]);
    expect(layout.groups.map((group) => group.value)).toEqual(['2026-09', '2026-10', '2026-11']);
  });

  /** Folded, a group gives up its rows and keeps its heading — and the count on it. */
  it('folds a group away to its heading and keeps the others', () => {
    const layout = layoutRecord(
      autumn(),
      options({ grouping: 'month', collapsed: new Set(['2026-10']) }),
    );

    expect(drawn(layout.rows)).toEqual([
      '[2026-09:2]',
      'sep-a',
      'sep-b',
      '[2026-10:4 folded]',
      '[2026-11:1]',
      'nov-a',
    ]);
    expect(layout.rows.find((row) => row.key === groupKey('2026-10'))).toMatchObject({
      type: 'group',
      collapsed: true,
    });
  });

  it('runs the headings latest-first when the answer does', () => {
    const layout = layoutRecord(
      autumn().reverse(),
      options({ grouping: 'month', order: 'descending' }),
    );

    expect(layout.groups.map((group) => group.value)).toEqual(['2026-11', '2026-10', '2026-09']);
    expect(drawn(layout.rows).slice(0, 2)).toEqual(['[2026-11:1]', 'nov-a']);
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
