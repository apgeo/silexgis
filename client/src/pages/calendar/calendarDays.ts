// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Dayjs } from 'dayjs';
import type { CalendarEntry } from '../../api/hooks.ts';
import { parseTripDay } from '../../components/trips/tripDates.ts';

/**
 * Where a record falls when a window of days is drawn as cells rather than as a list.
 *
 * A record that lasts more than one day is one row on the wire and appears in every cell it
 * covers, because a grid answers "what is happening on this day" and a row drawn only on the day
 * it began answers a different question. The two flags are what stop that repetition reading as
 * several separate records: the cell where it begins and the cell where it ends are marked, and
 * the cells between them are not, so the eye can tell a four-day trip from four one-day trips.
 *
 * The repetition is the whole shape and it has a limit worth stating where it is implemented: the
 * cells of a grid are laid out independently of each other, so nothing here can draw one
 * continuous bar across a week, align the same record to the same height in consecutive cells, or
 * mark the point where a span wraps from a Sunday onto the next row.
 */
export interface DayEntry {
  entry: CalendarEntry;
  /** Whether this cell is the day the record begins on — false in every later cell it covers. */
  isStart: boolean;
  /** Whether this cell is the last day the record covers. */
  isEnd: boolean;
}

/** A wire date reduced to its day, so an answer that ever grew a time part still keys correctly. */
const asDay = (value: string): string => value.slice(0, 10);

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
): Map<string, DayEntry[]> {
  const byDay = new Map<string, DayEntry[]>();
  for (const entry of entries) {
    const start = asDay(entry.start);
    const end = entry.end ? asDay(entry.end) : start;
    for (const day of coveredDays(entry.start, entry.end, from, to)) {
      const cell = byDay.get(day);
      const placed: DayEntry = { entry, isStart: day === start, isEnd: day === end };
      if (cell) {
        cell.push(placed);
      } else {
        byDay.set(day, [placed]);
      }
    }
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
const timeIn = (placed: DayEntry): string | null =>
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
export function orderedForDay(placed: readonly DayEntry[]): DayEntry[] {
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
