// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MovieCaverLabels } from './movieSettings.ts';

/**
 * How the people of an exported movie are named on their markers.
 *
 * <b>First names by default, because the file outlives the screen it was made on.</b> A movie is
 * passed on to people who never had the trip open, and a first name is enough to tell the party
 * apart while saying less about who they are. A caver record holds a single full name, so the
 * first name is simply its first word.
 *
 * <b>Lengthened only where two people would read the same.</b> Two Ions in one movie would be two
 * markers saying the same thing, which is worse than either name alone: the reader would take one
 * person's route for the other's. So a first name shared by two different people gains the initial
 * of the last word (`Ion P.`), and where that still collides, the full name is used. "Two different
 * people" is decided by caver id, over every selected trip at once — the same person on two trips
 * is one name, and colliding with oneself is not a collision.
 *
 * <b>Worked out over the rosters, not over who is on the model at a frame.</b> A name that grew an
 * initial the moment a namesake arrived underground, and lost it when they left, would change under
 * a viewer's eyes halfway through the movie for no reason the movie shows.
 */

/** One person of one of the movie's trips, as that trip's roster names them. */
export interface MovieCaverName {
  caverId: string;
  name: string;
}

/**
 * A name's initials: the first letter of each word, a hyphenated part counting as a word, in
 * capitals. Letters of any script count; a word with none (a lone "-" or "3") gives nothing, and a
 * name that yields nothing at all is kept as it was, since an empty label would be no label.
 */
export function initialsOf(name: string): string {
  const initials = words(name)
    .flatMap((word) => word.split(/[-‐‑]+/u))
    .map((word) => firstLetter(word) ?? '')
    .join('')
    .toUpperCase();
  return initials.length > 0 ? initials : name.trim();
}

/**
 * What each person is called on their marker, keyed by caver id.
 *
 * `off` answers every name empty: the marker is drawn without a label, and nothing downstream
 * should find a name to print.
 *
 * Initials are not lengthened on a collision. Somebody who asked for initials asked to say less,
 * and the only way to tell two `AP`s apart is to say more of their names than they chose to.
 */
export function movieCaverNames(
  people: readonly MovieCaverName[],
  mode: MovieCaverLabels,
): Map<string, string> {
  // One name per person: a caver record holds one full name, so the first trip's roster is as
  // good as any, and a person listed on two trips is counted once.
  const full = new Map<string, string>();
  for (const person of people) {
    if (!full.has(person.caverId)) {
      full.set(person.caverId, tidy(person.name));
    }
  }
  if (mode === 'off' || mode === 'full' || mode === 'initials') {
    return new Map(
      [...full].map(([id, name]) => [
        id,
        mode === 'off' ? '' : mode === 'initials' ? initialsOf(name) : name,
      ]),
    );
  }

  // Each person starts at their first name and is lengthened, one step at a time, while anybody
  // else reads the same. Lengthening one pair can make a new collision with a third person (an
  // `Ion P.` meeting somebody whose first name really is that), so this runs until nothing
  // changes; it ends because each person can be lengthened only twice.
  const level = new Map<string, number>([...full.keys()].map((id) => [id, 0]));
  const labelOf = (id: string): string => spelled(full.get(id)!, level.get(id)!);
  for (let changed = true; changed; ) {
    changed = false;
    const readers = new Map<string, string[]>();
    for (const id of full.keys()) {
      const key = sameReading(labelOf(id));
      readers.set(key, [...(readers.get(key) ?? []), id]);
    }
    for (const ids of readers.values()) {
      if (ids.length < 2) {
        continue;
      }
      for (const id of ids) {
        if (level.get(id)! < 2) {
          level.set(id, level.get(id)! + 1);
          changed = true;
        }
      }
    }
  }
  return new Map([...full.keys()].map((id) => [id, labelOf(id)]));
}

/** A name at a step of lengthening: its first word, then with the last word's initial, then whole. */
function spelled(name: string, level: number): string {
  const parts = words(name);
  if (parts.length <= 1 || level >= 2) {
    return name;
  }
  if (level === 0) {
    return parts[0];
  }
  const initial = firstLetter(parts[parts.length - 1]);
  return initial === null ? name : `${parts[0]} ${initial.toUpperCase()}.`;
}

/** Whitespace of any kind, runs of it and at the ends, is not part of how a name reads. */
function tidy(name: string): string {
  return words(name).join(' ');
}

function words(name: string): string[] {
  return name.normalize('NFC').split(/\s+/u).filter((word) => word.length > 0);
}

/**
 * The first letter of a word, with any accent that is written as a separate mark kept on it —
 * `Ș` typed as an S and a comma below is still one letter, and cutting the mark off would print
 * somebody's initial as a different letter.
 */
function firstLetter(word: string): string | null {
  for (const { segment } of new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(word)) {
    if (/^\p{L}/u.test(segment)) {
      return segment;
    }
  }
  return null;
}

/**
 * Two labels read the same when they differ only in capitals or in how an accent was typed. Lower-
 * cased without a locale, so the answer does not depend on the language of the browser it runs in.
 */
function sameReading(label: string): string {
  return label.normalize('NFC').toLowerCase();
}
