// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { formatTripDates, parseTripDay } from '../trips/tripDates.ts';

/**
 * What a stay's days mean, in one place — and it is not what a camp's or a trip's mean.
 *
 * A camp and a trip are written down as finished things, so for them no last day is one day. A
 * roster is kept while the camp is running, and there no last day is somebody who has not left:
 * a stay of a single day carries that day as its last as well as its first. Reading a stay with
 * the trip's rule would show everybody still at a camp as having gone home the day they came.
 */

/** Whether the person is still at the camp: the stay has a first day and no last one. */
export function isStillThere(toDate: string | null | undefined): boolean {
  return toDate == null;
}

/**
 * A stay as a line of text: one date for a single day, a range for a longer one, and for
 * somebody still there the day they arrived with words saying they have not left.
 */
export function formatStayDates(
  fromDate: string,
  toDate: string | null | undefined,
  locale: string | undefined,
  t: TFunction,
): string {
  if (isStillThere(toDate)) {
    return t('expeditions.stay.since', {
      date: parseTripDay(fromDate).toLocaleDateString(locale),
    });
  }
  // A closed stay reads exactly as a trip's days do: an end equal to the start is that one day.
  return formatTripDates(fromDate, toDate, locale);
}

/**
 * The last day as the server should hold it: nothing for somebody still there, and otherwise the
 * day picked — kept even when it is the first day, because that is how one day is said here.
 */
export function stayEndForWrite(stillThere: boolean, end: string | null | undefined): string | null {
  return stillThere ? null : (end ?? null);
}
