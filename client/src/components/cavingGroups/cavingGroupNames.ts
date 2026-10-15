// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Letters as a reader compares them: diacritics dropped and case folded, so "Ştiinţa" and
 * "stiinta" are the same word to somebody typing on a keyboard without the marks.
 */
export function foldName(text: string): string {
  return text.normalize('NFD').replace(/\p{Mn}/gu, '').toLocaleLowerCase('en');
}

/**
 * The key two caving-group names collide on, written the way the server writes the slug it
 * refuses duplicates by: diacritics dropped, ASCII letters and digits kept in lower case, and
 * every run of anything else turned into one dash, with none at either end. So "Clubul  Speo-Ursus"
 * and "clubul speo ursus" are the same group, and a picker that offered to create the second would
 * only be offering a refusal.
 *
 * The server stays the judge — it refuses a duplicate whatever this says — and the comparison is
 * made against the slug each group already carries rather than against its name, so the two can
 * disagree only if this function and the server's drift apart.
 */
export function cavingGroupKey(name: string): string {
  return name
    .trim()
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .replace(/[^A-Za-z0-9]+/g, '-')
    .toLowerCase()
    .replace(/^-+|-+$/g, '');
}
