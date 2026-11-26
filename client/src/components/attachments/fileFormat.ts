// SPDX-License-Identifier: AGPL-3.0-or-later
import i18next from 'i18next';

const SizeUnits = ['B', 'KB', 'MB', 'GB'] as const;

/**
 * A file's size in the shortest unit that keeps the figure readable — the one way every list of
 * files says it, so one file never reads "1 KB" in one place and "39 B" in another. Bytes are
 * whole; a larger unit keeps one decimal while the figure is under a hundred ("17.5 KB") and is
 * whole above it, and a trailing ".0" is dropped. The figure is written in the page's language
 * ("17,5 KB" in Romanian), as every other measurement on the page is.
 */
export function formatSize(bytes: number, language: string = i18next.language || 'en'): string {
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < SizeUnits.length - 1) {
    value /= 1024;
    unit += 1;
  }
  const figure = unit === 0 || value >= 100 ? Math.round(value) : Math.round(value * 10) / 10;
  return `${new Intl.NumberFormat(language, { maximumFractionDigits: 1 }).format(figure)} ${SizeUnits[unit]}`;
}
