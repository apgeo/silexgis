// SPDX-License-Identifier: AGPL-3.0-or-later
import dayjs, { type Dayjs } from 'dayjs';
import type { CalendarEntry } from '../../api/hooks.ts';
import { parseTripDay } from '../../components/trips/tripDates.ts';

/**
 * Where a record falls when a window of days is drawn as cells rather than as a list.
 *
 * A record that lasts more than one day is one row on the wire and appears in every cell it
 * covers, because a grid answers "what is happening on this day" and a row drawn only on the day
 * it began answers a different question. What stops that repetition reading as several separate
 * records is everything else carried here: which of the record's days a cell is and out of how
 * many, whether it is the first or the last of them, and whether it is the cell where the record
 * should say its name — so the eye can tell a four-day trip from four one-day trips, and a
 * reader who cannot see the drawing is told "day 2 of 4".
 *
 * The repetition is the whole shape and it has a limit worth stating where it is implemented: the
 * cells of a grid are laid out independently of each other, so nothing here can draw one
 * continuous bar across a week or hold the same record at the same height in consecutive cells.
 * A cell knows where it sits in its own record's days; it does not know what is drawn beside it.
 */
export interface DayEntry {
  entry: CalendarEntry;
  /** Whether this cell is the day the record begins on — false in every later cell it covers. */
  isStart: boolean;
  /** Whether this cell is the last day the record covers. */
  isEnd: boolean;
  /**
   * Which of the record's days this cell is, the day it begins being the first. Counted from the
   * day the record really began, not from the first day of it the window happens to show: a camp
   * met on its fifth day is on its fifth day.
   */
  dayNumber: number;
  /** How many days the record runs for in all — one for a record that did not run on. */
  dayCount: number;
  /**
   * Whether this is the first cell the record has in its row of days: the day it begins, the
   * first day of a week it runs on into, or the first day the window shows of a record that was
   * already under way. It is where the record says its name. Said once to a row, the name reads
   * as one record running along it; said in every cell, it reads as that many records sharing a
   * title, which is the reading this exists to prevent.
   */
  leadsRow: boolean;
}

/**
 * Where a cell sits in the run of days its record covers: the only day, the first of several,
 * the last of several, or one of the days between.
 */
export type SpanPosition = 'single' | 'start' | 'middle' | 'end';

/**
 * A record's first and last days are its two ends and are drawn closed; every other day is
 * somewhere along it and is drawn open on both sides. A record cut short by the window is read
 * by what its own days are, not by where the window stops — the last day shown of a camp that
 * runs on past the window is a day in the middle of it, and is drawn as one.
 */
export function spanPosition(placed: Pick<DayEntry, 'isStart' | 'isEnd'>): SpanPosition {
  if (placed.isStart) {
    return placed.isEnd ? 'single' : 'start';
  }
  return placed.isEnd ? 'end' : 'middle';
}

/**
 * What identifies a record wherever it is drawn. The identifier alone is not enough: the answer
 * merges three families of record, each numbered by its own table.
 */
export const entryKey = (entry: CalendarEntry): string => `${entry.source}:${entry.id}`;

/** A wire date reduced to its day, so an answer that ever grew a time part still keys correctly. */
const asDay = (value: string): string => value.slice(0, 10);

/**
 * A calendar day as a count of days, so that two of them can be subtracted. Counted in a clock
 * that has no daylight saving: between two local midnights either side of a clock change there
 * are twenty-three or twenty-five hours, and a difference taken there and divided by a day is a
 * fraction somebody then has to round the right way.
 */
const dayOrdinal = (value: string): number => {
  const [year, month, date] = asDay(value).split('-').map(Number);
  return Math.round(Date.UTC(year, month - 1, date) / 86_400_000);
};

/**
 * Whether a day is the first of its week — the reader's language's week, which is where a row of
 * the month grid begins and where the week strip does.
 */
export function startsWeek(day: string): boolean {
  return dayjs(day).startOf('week').format('YYYY-MM-DD') === day;
}

/**
 * A local date written back as the calendar day it is. `toISOString` would answer in UTC, which
 * for every reader east of Greenwich is the next day — the same trap the local-date parser beside
 * this exists to avoid, arrived at from the other direction.
 */
export function formatDay(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/**
 * The days of a window that a record covers, in order, clipped to the window itself.
 *
 * Clipping is what bounds the work: the answer's window is asked for and refused above a few
 * hundred days, so a record can never expand into more cells than the reader asked to see. A
 * record that began before the window still expands from the window's first day, and carries no
 * start mark there, which is the truthful drawing of "this was already going on".
 */
export function coveredDays(
  start: string,
  end: string | null | undefined,
  from: string,
  to: string,
): string[] {
  const first = asDay(start);
  const last = end ? asDay(end) : first;
  // The comparisons are on "YYYY-MM-DD" strings, whose lexical order is their chronological one.
  const clippedFirst = first > from ? first : from;
  const clippedLast = last < to ? last : to;
  if (clippedFirst > clippedLast) {
    return [];
  }
  const days: string[] = [];
  const cursor = parseTripDay(clippedFirst);
  const stop = parseTripDay(clippedLast);
  while (cursor.getTime() <= stop.getTime()) {
    days.push(formatDay(cursor));
    cursor.setDate(cursor.getDate() + 1);
  }
  return days;
}

/**
 * The answer's rows arranged by the day they fall on, each day keeping the order the answer gave
 * them.
 *
 * That order is the answer's own and is preserved here, but it is not an order *within* a day: the
 * answer is arranged by the day a record falls on and breaks ties on an identifier that carries no
 * meaning, so rows sharing a day arrive in no order at all. Giving a day one is a separate job and
 * belongs to whoever draws the day — see `orderedForDay`.
 */
export function entriesByDay(
  entries: readonly CalendarEntry[],
  from: string,
  to: string,
  // Which days begin a row of days. Both places that draw days draw them a week to a row, so
  // the week is the default; it is an argument so the rule can be asked about a stated week
  // rather than about whichever one the language in force happens to give.
  startsRow: (day: string) => boolean = startsWeek,
): Map<string, DayEntry[]> {
  const byDay = new Map<string, DayEntry[]>();
  for (const entry of entries) {
    const start = asDay(entry.start);
    const end = entry.end ? asDay(entry.end) : start;
    const first = dayOrdinal(start);
    // An end before its start is not something the answer carries, and such a record covers no
    // day and is drawn nowhere; the floor only keeps the count a count.
    const dayCount = Math.max(1, dayOrdinal(end) - first + 1);
    coveredDays(entry.start, entry.end, from, to).forEach((day, index) => {
      const cell = byDay.get(day);
      const placed: DayEntry = {
        entry,
        isStart: day === start,
        isEnd: day === end,
        dayNumber: dayOrdinal(day) - first + 1,
        dayCount,
        // The first day the window shows of the record is either the day it begins or the day
        // it is first met already under way; either way it is the first cell it has.
        leadsRow: index === 0 || startsRow(day),
      };
      if (cell) {
        cell.push(placed);
      } else {
        byDay.set(day, [placed]);
      }
    });
  }
  return byDay;
}

/**
 * How many records touch each month of the window, keyed "YYYY-MM". A year is twelve cells and a
 * record covering three of them is counted in all three, for the same reason a multi-day record
 * appears in every day it covers: the cell says what is happening in that stretch of time.
 */
export function countByMonth(
  entries: readonly CalendarEntry[],
  from: string,
  to: string,
): Map<string, number> {
  const byMonth = new Map<string, number>();
  for (const entry of entries) {
    const days = coveredDays(entry.start, entry.end, from, to);
    if (days.length === 0) {
      continue;
    }
    // Walked by month rather than by day: a camp lasting a fortnight touches one or two months,
    // and counting it a day at a time would count it fourteen times.
    const seen = new Set<string>();
    for (const day of days) {
      seen.add(day.slice(0, 7));
    }
    for (const month of seen) {
      byMonth.set(month, (byMonth.get(month) ?? 0) + 1);
    }
  }
  return byMonth;
}

/**
 * The time of day a record claims in the cell it is being drawn in, or nothing.
 *
 * A record carries one wall-clock time and it belongs to the day it begins on. In every later day
 * a span covers, the record is something already going on rather than something starting, so it
 * claims no time there — which is also the truthful answer, because nothing on the wire says when
 * the second morning of a camp begins.
 */
const timeIn = (placed: Pick<DayEntry, 'entry' | 'isStart'>): string | null =>
  placed.isStart ? (placed.entry.startTime ?? null) : null;

/**
 * One day's records in the order a day is read: what has no time first, then what does, earliest
 * first.
 *
 * **Rows without a time are ordered, not dropped.** Most records here carry no time of day at all
 * — a trip states when its party went underground and not when it left, a camp states days, and
 * an all-day record has nothing to place — so a day sorted only on the times it happens to hold
 * would put the majority of its records nowhere. They go first, in the order the answer gave
 * them, which is where an all-day thing belongs when the rest of the column is a clock.
 *
 * This is an ordering within a single day and nothing more. It is not a time axis: the times here
 * carry no zone and no date, several records may claim the same minute, and none of them says how
 * long it lasts — so there is no interval to lay out and nothing to pack side by side.
 */
export function orderedForDay<T extends Pick<DayEntry, 'entry' | 'isStart'>>(
  placed: readonly T[],
): T[] {
  // Sorting is stable, so records that claim no time and records that claim the same minute keep
  // the order the answer merged them in.
  return [...placed].sort((a, b) => {
    const left = timeIn(a);
    const right = timeIn(b);
    if (left === right) {
      return 0;
    }
    if (left === null) {
      return -1;
    }
    if (right === null) {
      return 1;
    }
    return left < right ? -1 : 1;
  });
}

/**
 * The seven days of the week a given day falls in, as the keys a cell is looked up by.
 *
 * The week starts on the day the reader's language starts it on — Monday for a Romanian reader,
 * Sunday for an English one — which is decided in one place for the whole application by the date
 * library's locale, so a strip built from it and a date picker beside it agree without either
 * knowing about the other.
 */
export function weekDays(anyDayInTheWeek: Dayjs): string[] {
  const first = anyDayInTheWeek.startOf('week');
  return Array.from({ length: 7 }, (_, offset) => first.add(offset, 'day').format('YYYY-MM-DD'));
}
