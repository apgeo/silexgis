// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The states a way on is recorded in, as the shipped kind of place declares them.
 *
 * Listed here only so each has a word in the reader's language: the vocabulary lives on the kind
 * of place, the server serves the value exactly as it is stored, and a state an installation has
 * added to its own copy of the schema is shown as the word it was stored as rather than hidden for
 * being unrecognised.
 *
 * Its own module, and not a literal beside the one component that reads it, so the wording can be
 * checked against this list in both languages — a state renamed or added on the server otherwise
 * shows the reader a raw code, in both languages, with nothing failing.
 */
export const SEEDED_CONTINUATION_STATE_CODES = ['open', 'checked', 'dead-end', 'continues'] as const;

export type SeededContinuationStateCode = (typeof SEEDED_CONTINUATION_STATE_CODES)[number];

export function isSeededContinuationStateCode(code: string): code is SeededContinuationStateCode {
  return (SEEDED_CONTINUATION_STATE_CODES as readonly string[]).includes(code);
}
