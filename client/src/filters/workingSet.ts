// SPDX-License-Identifier: AGPL-3.0-or-later
import type { SelectedRef } from '../stores/workspaceStore.ts';

/**
 * Putting a list's result into the session's working set.
 *
 * The working set is the map's multi-selection — ids only, capped, held for this window and never
 * stored. A feature set would be the wrong home: a feature set is something access rules are
 * written against, and turning "the caves this filter found today" into one would quietly make a
 * search result into a permission anchor.
 */

/** The working-set member a listed row becomes. */
export function workingSetRef(id: string, kind: string | null | undefined): SelectedRef {
  if (kind === 'cave') {
    return { kind: 'cave', id };
  }
  if (kind === 'caveEntrance') {
    return { kind: 'entrance', id };
  }
  return { kind: 'feature', id };
}

export interface WorkingSetTake {
  refs: SelectedRef[];
  /** How many went in. */
  taken: number;
  /** How many the list found for this reader. */
  total: number;
  /** Whether the cap left some out, which the page has to say rather than imply all were taken. */
  truncated: boolean;
}

/**
 * The first `cap` rows of a result, in the list's own order, and what to say about it.
 *
 * `total` is the list's own count, which is already the count of rows this reader may read — so
 * "first 200 of 1,234" never includes anything they could not have scrolled to.
 */
export function takeIntoWorkingSet(
  rows: readonly { id: string; kind?: string | null }[],
  total: number,
  cap: number,
): WorkingSetTake {
  const refs = rows.slice(0, cap).map((row) => workingSetRef(row.id, row.kind));
  const whole = Math.max(total, refs.length);
  return { refs, taken: refs.length, total: whole, truncated: refs.length < whole };
}
