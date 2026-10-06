// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The things the record's rows can be grouped by, "none" first.
 *
 * They are the columns of the record that are worth cutting a list by, each read off the row
 * itself: the month and the week a record begins in, what the Kind column says about it, its
 * lifecycle state, and the caving group it belongs to. A row carries nothing else a heading could
 * be made of — its title is its own, and whether it has a position is not something anybody
 * groups a calendar by.
 *
 * Not grouping at all is the ordinary state and is listed first, so that a control whose first
 * option reshapes the page cannot reshape it by accident.
 *
 * A vocabulary of its own because two things read it: the address, which carries the choice, and
 * the layout, which makes it.
 */
export const RECORD_GROUPINGS = ['none', 'month', 'week', 'kind', 'state', 'cavingGroup'] as const;
export type RecordGrouping = (typeof RECORD_GROUPINGS)[number];

/** Not cutting the list at all. */
export const NoGrouping = 'none' satisfies RecordGrouping;

/**
 * The grouping a word names. A word this application does not have is no grouping rather than
 * an error: how the same rows are laid out hides none of them, so there is nothing a refusal
 * would protect a reader from.
 */
export function recordGroupingOf(word: string | null | undefined): RecordGrouping {
  return (RECORD_GROUPINGS as readonly string[]).includes(word ?? '')
    ? (word as RecordGrouping)
    : NoGrouping;
}
