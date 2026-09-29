// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackedTrip } from '../../../api/hooks.ts';
import { formatTripDates } from '../../trips/tripDates.ts';

/**
 * The days a movie's trips span, as the reader writes dates: from the earliest day any of them
 * started to the latest day any of them ended, read the way a trip's own dates are read — one day
 * as a date, several as a range. Null when none of the trips is dated.
 *
 * Trip days are calendar days carrying no zone, so they are compared as the `yyyy-mm-dd` text they
 * arrive as, which orders them correctly without reading any of them as an instant.
 */
export function movieTripDays(
  trips: readonly Pick<TrackedTrip, 'tripDate' | 'tripDateEnd'>[],
  language: string,
): string | null {
  let first: string | null = null;
  let last: string | null = null;
  for (const trip of trips) {
    if (trip.tripDate === null) {
      continue;
    }
    const start = trip.tripDate.slice(0, 10);
    const end = (trip.tripDateEnd ?? trip.tripDate).slice(0, 10);
    if (first === null || start < first) {
      first = start;
    }
    if (last === null || end > last) {
      last = end;
    }
  }
  return first === null ? null : formatTripDates(first, last, language);
}
