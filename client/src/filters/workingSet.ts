// SPDX-License-Identifier: AGPL-3.0-or-later
import type { SelectedRef } from '../stores/workspaceStore.ts';
import { holdsPosition, withoutAnchor } from './spatial.ts';
import type { FilterDocument } from './types.ts';
import { decodeFilter, encodeFilter } from './url.ts';

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

/** What a result taken into the working set keeps of the question that found it. */
export interface ResultQuestion {
  /** The document the set may be saved as a filter under, or null when no saved filter can hold it. */
  savable: FilterDocument | null;
  /** The filter the map opens on with the set. */
  mapToken: string | undefined;
}

/**
 * The question behind a list's result, as the working set keeps it.
 *
 * The list may have been asked nearest first, and then its request carries the place the order is
 * measured from, so that "the first N" are the first N the list shows. That place orders one
 * request and belongs to no question that outlives it: neither the filter the map opens on nor the
 * one the set can be saved as keeps it. The set is savable only when a saved filter can hold its
 * question at all — not cut to the map's window, and naming no point or outline of its own, both
 * of which a saved filter refuses to store — since saving a nearby question would keep a different
 * one under a name that claims this one.
 */
export function resultQuestion(token: string | undefined, windowed: boolean): ResultQuestion {
  const decoded = decodeFilter(token);
  if (decoded === null) {
    return { savable: null, mapToken: token };
  }
  const question = withoutAnchor(decoded);
  return {
    savable: windowed || holdsPosition(question) ? null : question,
    mapToken: question === decoded ? token : encodeFilter(question),
  };
}
