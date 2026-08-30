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

/**
 * A row of people-and-their-details as a form holds it while it is being edited: the person the
 * row was loaded for, the name it arrived under, and the name it currently reads.
 */
export interface CaverReferenceRow {
  caverId?: string | null;
  /** The name this row arrived under, absent for a row that was typed in from nothing. */
  loadedName?: string | null;
  name: string;
}

/**
 * Whether a row still points at the person it was loaded for.
 *
 * A row still reading the name it arrived under still means that person; one typed over means
 * whoever the new text names. Both halves of that are load-bearing and both were got wrong once:
 *
 * - Keeping the reference beside a corrected name stores nothing at all, because the reference is
 *   what a write is read by — so a misspelled name would survive every attempt to fix it, in
 *   silence, with a success message.
 * - Dropping the reference on any keystroke makes two people out of one. The name shown for
 *   somebody who holds an account is their profile's, which need not be the name recorded against
 *   them, so an edit that changed nothing would quietly detach the row and a second entry would
 *   be written for a person already known.
 *
 * One home, because the rule is the same wherever a form lets somebody edit the text beside a
 * person it already knows — writing up who was on a trip, or naming who is being asked on one.
 */
export function referencesLoadedCaver(row: CaverReferenceRow): boolean {
  return row.caverId != null && row.name.trim() === (row.loadedName ?? '').trim();
}

/**
 * The person a row names, as an identity where the row still carries one and as a bare name
 * otherwise. A caller that can only accept an identity reads `caverId` and refuses a row that
 * has none rather than inventing one.
 */
export function caverReference(row: CaverReferenceRow): {
  caverId: string | null;
  newCaverName: string | null;
} {
  const name = row.name.trim();
  return referencesLoadedCaver(row)
    ? { caverId: row.caverId!, newCaverName: null }
    : { caverId: null, newCaverName: name };
}
