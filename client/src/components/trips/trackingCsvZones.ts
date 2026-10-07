// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Whose clock a sheet's times are on, as far as the browser can help say it.
 *
 * A sheet writes "14:05" and means the clock on somebody's wall. The server turns that into an
 * instant once it is told which wall, by an IANA zone name; what is here is only the list the
 * importer chooses the name from and how a row is shown back in the zone it was read in. Whether a
 * name is a zone the server can read a sheet in is the server's to answer, and it refuses one it
 * does not know — so nothing here tries to be a second judge of that.
 */

/**
 * The choice that names no zone: a time with no offset is the instant it says, as UTC.
 *
 * Written so that it can never be mistaken for a zone name — a zone has an area and a slash, or is
 * the three letters "UTC".
 */
export const SHEET_ZONE_AS_WRITTEN = 'as-written';

/**
 * The zone this browser keeps its own clock in, or nothing where that is no help.
 *
 * Nothing when the browser will not say, and nothing when it says UTC: on such a machine "my own
 * zone" and "exactly as written" are one reading, and offering it twice under two names would
 * suggest a difference that is not there.
 */
export function ownSheetZone(): string | null {
  try {
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    return !zone || zone === 'UTC' || zone === 'Etc/UTC' ? null : zone;
  } catch {
    return null;
  }
}

/**
 * Every zone the browser knows by name, for the list somebody searches.
 *
 * Empty on a browser too old to list them. The importer's own zone is then still offered, read
 * from the clock rather than from this list, and that is the one almost every sheet needs.
 */
export function sheetZoneNames(): readonly string[] {
  try {
    return typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
  } catch {
    return [];
  }
}

/**
 * An instant as the clocks of a zone showed it, in the application's language.
 *
 * <b>This is what lets a reviewer check a sheet read in a zone against the sheet itself.</b> A row
 * the sheet wrote as 14:05 and the server read as 14:05 in Bucharest is shown as 14:05 whatever
 * zone the reviewer's own machine is in, so the preview and the paper agree line by line.
 *
 * With no zone the instant is shown on the reader's own clock, as every other moment on the
 * tracking surfaces is. A name this browser cannot format in — the server's zone list and the
 * browser's are two lists — falls back to the same, rather than failing the whole table over the
 * wording of one column.
 */
export function momentInSheetZone(instant: string, language: string, zone: string | null): string {
  const at = new Date(instant);
  if (zone === null) {
    return at.toLocaleString(language);
  }
  try {
    return at.toLocaleString(language, { timeZone: zone });
  } catch {
    return at.toLocaleString(language);
  }
}

/** A calendar day ("2026-09-12") in the application's language, with no clock to shift it. */
export function sheetDayInWords(day: string, language: string): string {
  const at = new Date(`${day}T00:00:00Z`);
  return Number.isNaN(at.getTime())
    ? day
    : at.toLocaleDateString(language, { timeZone: 'UTC' });
}
