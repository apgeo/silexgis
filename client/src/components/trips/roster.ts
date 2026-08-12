// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TripLogInfo } from '../../api/hooks.ts';

/**
 * How many people a roster names.
 *
 * A roster row is a person *and* the job they did, so somebody who led the trip and surveyed it
 * is two rows and one caver. Anywhere a reader is shown a number of people — a list column, a
 * summary — that number has to be of people, not of rows, or every trip where somebody wore two
 * hats reports more attendees than were underground.
 */
export function countPeople(roster: readonly TripLogInfo['participants'][number][]): number {
  return new Set(roster.map((p) => p.caverId)).size;
}
