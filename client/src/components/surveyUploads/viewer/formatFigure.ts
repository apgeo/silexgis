// SPDX-License-Identifier: AGPL-3.0-or-later

/** A number as the reader's language writes it, to at most the given decimals; a dash for unknown. */
export function formatFigure(value: number | null | undefined, language: string, decimals = 1): string {
  return value === null || value === undefined
    ? '—'
    : new Intl.NumberFormat(language, { maximumFractionDigits: decimals }).format(value);
}
