// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Whether an error is the server refusing the filter it was sent, as opposed to a read that failed
 * for any other reason (a dropped connection, a server error, a session that ran out). Matched by
 * prefix because the server's vocabulary of reasons grows; every one of them means "this filter
 * was not run", and none of the other failures may be told to the reader as a refused filter.
 */
export function isFilterRefusal(error: unknown): error is { code: string } {
  const code = (error as { code?: unknown } | null)?.code;
  return typeof code === 'string' && (code.startsWith('filter.') || code.startsWith('saved_filter.'));
}

/**
 * Refusals that name a place to measure from: one that is not there or not readable, one that is
 * not an outline, an outline inside another, or one too large to measure against.
 */
export const ANCHOR_REFUSALS: Readonly<Record<string, string>> = {
  'filter.anchor_not_found': 'filters.editor.spatial.refused.notFound',
  'filter.study_area_not_an_outline': 'filters.editor.spatial.refused.notAnArea',
  'filter.anchor_nested': 'filters.editor.spatial.refused.nested',
  'filter.anchor_too_large': 'filters.editor.spatial.refused.tooLarge',
};

/**
 * Refusals of the filter as a whole, which name no row and carry no problem list: a body the
 * server could not read at all, and a saved filter this reader's access cannot read.
 */
export const WHOLE_REFUSALS: Readonly<Record<string, string>> = {
  'filter.malformed': 'filters.editor.wholeRefusal.malformed',
  'saved_filter.unreadable': 'filters.editor.wholeRefusal.unreadable',
};

/**
 * The translation key of the sentence a refused filter is told in, from its code alone — the one
 * place a refusal code is turned into words, so every surface that shows one says the same thing.
 * Undefined for a code no sentence is written for yet; the caller then uses its general one.
 */
export function filterRefusalKey(code: string | undefined): string | undefined {
  return code === undefined ? undefined : (ANCHOR_REFUSALS[code] ?? WHOLE_REFUSALS[code]);
}
