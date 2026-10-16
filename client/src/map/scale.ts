// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The scales a person may type: a map at the closest zoom this application offers is about
 * 1:100, and a whole hemisphere fits a screen near 1:100 000 000. Anything outside is a typing
 * slip rather than a scale anybody wants.
 */
export const MIN_SCALE = 50;
export const MAX_SCALE = 500_000_000;

/** Characters people put between groups of digits: spaces of every width, points, commas, apostrophes. */
const GROUP_SEPARATORS = /[\s   .,'’]/g;

/**
 * The denominator of a scale as somebody types it — `1:25000`, `1 : 25 000`, `25000`, `25.000`,
 * `1/25,000` — or null when the text is not one.
 *
 * Points and commas are read as digit grouping, never as a decimal mark: a scale's denominator is
 * a whole number, and the two languages this application speaks group thousands with opposite
 * marks, so either one has to mean grouping.
 */
export function parseScale(text: string): number | null {
  const trimmed = text.trim();
  const match = /^(?:1\s*[:/]\s*)?([0-9][0-9\s   .,'’]*)$/.exec(trimmed);
  if (!match) {
    return null;
  }
  const digits = match[1].replace(GROUP_SEPARATORS, '');
  if (!/^[0-9]+$/.test(digits)) {
    return null;
  }
  const value = Number(digits);
  return Number.isSafeInteger(value) && value >= MIN_SCALE && value <= MAX_SCALE ? value : null;
}

/** A scale denominator as it is shown: `1:25 000`, grouped the reader's way. */
export function formatScale(denominator: number, locale?: string): string {
  return `1:${Math.round(denominator).toLocaleString(locale)}`;
}
