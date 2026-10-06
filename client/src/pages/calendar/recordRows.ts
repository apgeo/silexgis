// SPDX-License-Identifier: AGPL-3.0-or-later
import dayjs from 'dayjs';
import type { CalendarEntry } from '../../api/hooks.ts';
import { EVENT_KINDS } from '../../components/events/eventKinds.ts';
import { ACTIVITY_STATES } from '../../components/trips/tripStates.ts';
import { CALENDAR_SOURCES } from './calendarAddress.ts';
import { entryKey } from './calendarDays.ts';
import { NoGrouping, type RecordGrouping } from './recordGrouping.ts';

/**
 * How the rows of a window are laid out when they are read as a list: which rows sit under which
 * heading, and in what order the headings come.
 *
 * It is worked out here, from the rows already in hand and with nothing drawn, for two reasons.
 * The window is bounded and read whole, so there is no second question to ask the server and
 * nothing a server could add: every row a heading counts is on the page. And every decision
 * below is one a reader meets as a fact about their calendar — "four in October" — so each is a
 * function that can be asked directly what it would say for a given set of rows, rather than
 * something only a rendered table can show.
 */

/** One heading and the rows under it. */
export interface RecordGroup {
  /**
   * What the rows have in common, as a row carries it: a month ("YYYY-MM"), the first day of a
   * week, a kind of event or the family of a row that has no kind, a lifecycle state, a caving
   * group's identifier — or the empty string, for rows that carry nothing to be grouped by.
   */
  value: string;
  /** The rows, in the order the answer gave them. */
  entries: CalendarEntry[];
}

/** A line of the list: a record, or a heading over some records. */
export type RecordRow =
  | { type: 'entry'; key: string; entry: CalendarEntry }
  | { type: 'group'; key: string; group: RecordGroup; collapsed: boolean };

export interface RecordLayout {
  /** Every line to draw, top to bottom. */
  rows: RecordRow[];
  /** The headings, in the order drawn. Empty when the list is not grouped. */
  groups: RecordGroup[];
}

export interface RecordLayoutOptions {
  grouping: RecordGrouping;
  /** Which way the answer runs through time; null when it is ordered by something else. */
  order: 'ascending' | 'descending' | null;
  /** The headings that have been folded away, by their value. */
  collapsed: ReadonlySet<string>;
  /** The names of the caving groups the reader can list, by identifier. */
  groupNames?: ReadonlyMap<string, string>;
  /**
   * The first day of the week a day falls in. The reader's language decides which day that is,
   * and it is settled in one place for every date this application draws, so it is handed in
   * rather than decided here.
   */
  weekOf?: (day: string) => string;
}

export const groupKey = (value: string): string => `group:${value}`;

/**
 * A record's line, the same one each time the same record is laid out.
 *
 * The list is laid out again whenever a heading is folded, and what it is laid out from has not
 * changed — the answer's rows are the same objects. A table decides what to redraw by whether it
 * is handed the same line as before, so lines made anew on every layout would have every row of
 * the window redrawn to fold one heading away. Kept by the record itself and let go of with it.
 */
const lines = new WeakMap<CalendarEntry, RecordRow>();
const lineOf = (entry: CalendarEntry): RecordRow => {
  let line = lines.get(entry);
  if (!line) {
    line = { type: 'entry', key: entryKey(entry), entry };
    lines.set(entry, line);
  }
  return line;
};

/** A wire date reduced to its day, so an answer that ever grew a time part still compares. */
const asDay = (value: string): string => value.slice(0, 10);

const localWeekOf = (day: string): string => dayjs(day).startOf('week').format('YYYY-MM-DD');

/**
 * Which way an order word runs through time. The words are the server's: it knows two orders by
 * title, one by day descending, and answers everything else — its own word for ascending, no word
 * at all, or a word it has never heard of — in its own order, which is by day ascending.
 */
export function chronologyOf(sort: string | undefined): 'ascending' | 'descending' | null {
  if (sort === 'title' || sort === '-title') {
    return null;
  }
  return sort === '-start' ? 'descending' : 'ascending';
}

/**
 * What a row is grouped under.
 *
 * A record that runs over several days is grouped by the day it **begins**, and appears once. A
 * camp across the end of a month sits under the month it started in: filing it under both would
 * make the counts on the headings add up to more than the rows, and a heading's count is the one
 * figure on the page a reader does not check.
 */
export function groupValueOf(
  entry: CalendarEntry,
  grouping: Exclude<RecordGrouping, 'none'>,
  weekOf: (day: string) => string = localWeekOf,
): string {
  switch (grouping) {
    case 'month':
      return asDay(entry.start).slice(0, 7);
    case 'week':
      return weekOf(asDay(entry.start));
    case 'kind':
      // What the Kind column says: the row's own kind where it has one, its family otherwise.
      return entry.kind ?? entry.source;
    case 'state':
      return entry.state;
    case 'cavingGroup':
      return entry.cavingGroupId ?? '';
  }
}

/** Where a value sits in a vocabulary, with anything the vocabulary does not hold after all of it. */
const rank = (vocabulary: readonly string[], value: string): number => {
  const index = vocabulary.indexOf(value);
  return index === -1 ? vocabulary.length : index;
};

/**
 * The order the headings are drawn in.
 *
 * A heading that is a stretch of time follows the way the answer runs, so the list is still one
 * run through the days with headings laid over it. A heading that is a word follows its
 * vocabulary — the families and then the kinds as the Kind filter lists them, the states as a
 * record moves through them — rather than the alphabet, which would reorder them with the
 * interface language. Caving groups go by name. In every case the rows that carry nothing to be
 * grouped by come last, under a heading of their own, so they are counted rather than dropped.
 */
function compareGroups(
  grouping: Exclude<RecordGrouping, 'none'>,
  descending: boolean,
  groupNames: ReadonlyMap<string, string>,
): (a: RecordGroup, b: RecordGroup) => number {
  const kinds: readonly string[] = [...CALENDAR_SOURCES, ...EVENT_KINDS];
  const states: readonly string[] = ACTIVITY_STATES;
  return (a, b) => {
    if ((a.value === '') !== (b.value === '')) {
      return a.value === '' ? 1 : -1;
    }
    switch (grouping) {
      case 'month':
      case 'week':
        // "YYYY-MM" and "YYYY-MM-DD" order as text the way they order as dates.
        return (a.value < b.value ? -1 : a.value > b.value ? 1 : 0) * (descending ? -1 : 1);
      case 'kind':
        return rank(kinds, a.value) - rank(kinds, b.value);
      case 'state':
        return rank(states, a.value) - rank(states, b.value);
      case 'cavingGroup': {
        const left = groupNames.get(a.value);
        const right = groupNames.get(b.value);
        // A group whose name the reader cannot list still has rows; it follows the named ones.
        if ((left === undefined) !== (right === undefined)) {
          return left === undefined ? 1 : -1;
        }
        return (left ?? a.value).localeCompare(right ?? b.value);
      }
    }
  };
}

/**
 * The rows cut into headings. Every row lands under exactly one, so the counts on the headings
 * add up to the rows, and within a heading the rows keep the order the answer gave them.
 */
export function groupRecord(
  entries: readonly CalendarEntry[],
  grouping: Exclude<RecordGrouping, 'none'>,
  options: Pick<RecordLayoutOptions, 'order' | 'groupNames' | 'weekOf'> = { order: 'ascending' },
): RecordGroup[] {
  const byValue = new Map<string, RecordGroup>();
  for (const entry of entries) {
    const value = groupValueOf(entry, grouping, options.weekOf);
    const group = byValue.get(value);
    if (group) {
      group.entries.push(entry);
    } else {
      byValue.set(value, { value, entries: [entry] });
    }
  }
  // Sorting is stable, so headings the comparison cannot tell apart — two words no vocabulary
  // holds — keep the order their first rows arrived in.
  return [...byValue.values()].sort(
    compareGroups(grouping, options.order === 'descending', options.groupNames ?? new Map()),
  );
}

/**
 * Lays the rows out as the lines of a list: each heading, then the rows under it unless it has
 * been folded away. Ungrouped, the lines are the rows as the answer gave them.
 */
export function layoutRecord(
  entries: readonly CalendarEntry[],
  options: RecordLayoutOptions,
): RecordLayout {
  const { grouping, collapsed } = options;
  if (grouping === NoGrouping) {
    return { rows: entries.map(lineOf), groups: [] };
  }

  const groups = groupRecord(entries, grouping, options);
  const rows: RecordRow[] = [];
  for (const group of groups) {
    const folded = collapsed.has(group.value);
    rows.push({ type: 'group', key: groupKey(group.value), group, collapsed: folded });
    if (!folded) {
      for (const entry of group.entries) {
        rows.push(lineOf(entry));
      }
    }
  }
  return { rows, groups };
}
