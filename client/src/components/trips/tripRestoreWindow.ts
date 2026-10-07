// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * How long a deleted trip can still be put back, said in words. The number is the installation's
 * and arrives from the server; what lives here is only how to say it, so the confirmation before
 * a delete, the list of deleted trips and an import's undo all say it the same way.
 */

const MS_PER_DAY = 24 * 60 * 60 * 1000;

/**
 * A number of days the way the reader's language says it — "30 days", "1 day", "30 de zile".
 * Left to the platform because it already knows every language's plural forms, and a sentence
 * that carries the result needs none of its own.
 */
export function formatDays(days: number, language: string | undefined): string {
  try {
    return new Intl.NumberFormat(language, {
      style: 'unit',
      unit: 'day',
      unitDisplay: 'long',
    }).format(days);
  } catch {
    // A language tag the platform does not know is not worth losing the sentence over.
    return new Intl.NumberFormat('en', { style: 'unit', unit: 'day', unitDisplay: 'long' }).format(days);
  }
}

/**
 * Whole days left before a deleted trip is removed for good, or null where nothing removes it.
 * Rounded down, and never below zero: "3 days" must not be said of a trip with two days and a
 * few hours left, because whoever reads it will come back on the third day.
 */
export function daysLeft(restorableUntil: string | null | undefined, now: Date): number | null {
  if (!restorableUntil) {
    return null;
  }
  const remaining = new Date(restorableUntil).getTime() - now.getTime();
  return Math.max(0, Math.floor(remaining / MS_PER_DAY));
}
