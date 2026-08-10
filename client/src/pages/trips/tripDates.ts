// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * What a trip's dates mean, in one place. The list, the detail page and the form all have to
 * agree on two things: when a trip counts as spanning more than one day, and how long its
 * party was underground — which cannot be derived from the two clock times alone once a trip
 * may span days.
 */

const MINUTES_PER_DAY = 24 * 60;
const MS_PER_DAY = 24 * 60 * 60 * 1000;

/**
 * Server trip dates are calendar days ("YYYY-MM-DD") carrying no zone at all. Handing one to
 * `new Date()` reads it as UTC midnight, which then renders as the *previous* day for every
 * reader west of Greenwich — so the day is rebuilt from its parts as a local date instead.
 */
export function parseTripDay(value: string): Date {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  return new Date(year, month - 1, day);
}

/** Whether the trip ran past the day it started on. An end equal to the start is a single day. */
export function isMultiDay(tripDate: string, tripDateEnd: string | null | undefined): boolean {
  return tripDateEnd != null && tripDateEnd.slice(0, 10) !== tripDate.slice(0, 10);
}

/** A single day reads as a date; a trip that ran on past it reads as a range. */
export function formatTripDates(
  tripDate: string,
  tripDateEnd: string | null | undefined,
  locale: string | undefined,
): string {
  const start = parseTripDay(tripDate).toLocaleDateString(locale);
  if (!isMultiDay(tripDate, tripDateEnd)) {
    return start;
  }
  return `${start} – ${parseTripDay(tripDateEnd!).toLocaleDateString(locale)}`;
}

const minutesOfDay = (time: string): number => {
  const [hours, minutes] = time.split(':').map(Number);
  return hours * 60 + minutes;
};

/**
 * Minutes underground, or null when the pair cannot say. The two clock times are wall-clock and
 * carry no day, so the days the trip spans have to be added back: an exit time earlier than the
 * entry time means "the next morning" only on a trip recorded as lasting one day — on a trip that
 * records the day it ended, the span is already known and guessing a midnight would double-count.
 */
export function undergroundMinutes(
  tripDate: string,
  tripDateEnd: string | null | undefined,
  entryTime: string | null | undefined,
  exitTime: string | null | undefined,
): number | null {
  if (!entryTime || !exitTime) {
    return null;
  }
  // Days across a daylight-saving change are 23 or 25 hours long; rounding keeps them whole days.
  const days = isMultiDay(tripDate, tripDateEnd)
    ? Math.round((parseTripDay(tripDateEnd!).getTime() - parseTripDay(tripDate).getTime()) / MS_PER_DAY)
    : 0;
  let minutes = days * MINUTES_PER_DAY + minutesOfDay(exitTime) - minutesOfDay(entryTime);
  if (days === 0 && minutes < 0) {
    minutes += MINUTES_PER_DAY;
  }
  return minutes < 0 ? null : minutes;
}

/** "09:00 – 17:30 (8h 30m)" — the derived length dropped when the dates and times cannot give one. */
export function formatUndergroundTime(
  tripDate: string,
  tripDateEnd: string | null | undefined,
  entryTime: string | null | undefined,
  exitTime: string | null | undefined,
): string | null {
  if (!entryTime && !exitTime) {
    return null;
  }
  const range = `${entryTime?.slice(0, 5) ?? '—'} – ${exitTime?.slice(0, 5) ?? '—'}`;
  const minutes = undergroundMinutes(tripDate, tripDateEnd, entryTime, exitTime);
  if (minutes == null) {
    return range;
  }
  return `${range} (${Math.floor(minutes / 60)}h ${minutes % 60}m)`;
}

/**
 * The end date as the server should hold it. A picked range whose ends are the same day is a
 * single-day trip: the stored end date is the "and it ran on to" fact, so an equal end is stored
 * as nothing rather than making every one-day trip read as a range of itself.
 */
export function tripDateEndForWrite(start: string, end: string | null | undefined): string | null {
  return end && end !== start ? end : null;
}
