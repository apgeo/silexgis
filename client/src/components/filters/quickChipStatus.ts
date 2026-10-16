// SPDX-License-Identifier: AGPL-3.0-or-later
import { documentOf, type SavedFilter } from '../../api/hooks/savedFilters.ts';

/** What a pinned chip can do: apply its filter, or only say why it cannot. */
export type QuickChipStatus = 'loading' | 'ready' | 'stale' | 'unreadable' | 'gone';

/**
 * A pinned filter's state, read from the saved-filter list this account may read.
 *
 * "Gone" is a pin naming a filter the list no longer holds — deleted, or no longer shared with
 * this person's caving group — and is said apart from "unreadable", a filter that is still there
 * but whose conditions can no longer be run. Neither is applied: both would be refused by the
 * server, and a chip that looked usable and drew nothing would be read as "no caves match".
 */
export function quickChipStatus(id: string, filters: readonly SavedFilter[] | undefined): QuickChipStatus {
  if (filters === undefined) {
    return 'loading';
  }
  const filter = filters.find((candidate) => candidate.id.toLowerCase() === id.toLowerCase());
  if (filter === undefined) {
    return 'gone';
  }
  if (filter.state === 'unreadable' || documentOf(filter) === null) {
    return 'unreadable';
  }
  return filter.state === 'stale' ? 'stale' : 'ready';
}
