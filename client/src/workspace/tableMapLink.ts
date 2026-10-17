// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import type { WorkspaceSelection } from '../stores/workspaceStore.ts';
import { publish, subscribe } from './workspaceBus.ts';

// A list and a map side by side — the registry popped out onto a second monitor, or the list in
// one tab and the map in another — point at each other's rows: hovering a row marks its thing on
// the map, and picking a thing on the map marks its row in the list.
//
// Both directions go over the workspace bus, because the two are in different windows whenever
// both are on screen at all; the bus also delivers inside the sending window, so nothing here
// cares which arrangement it is in. Both carry ids only. The map marks what it has itself drawn
// for an id, where it drew it, and the list marks a row it is itself showing — so a mark never
// says anything about a row the receiving window was not already given.

const NOTHING: ReadonlySet<string> = new Set();

/** A list's reader is pointing at these rows, or at none. */
export function announceRowHover(ids: readonly string[] | null): void {
  publish({ kind: 'highlight', ids: ids === null || ids.length === 0 ? null : [...ids] });
}

/** Follows the rows a list's reader is pointing at, in any window. Returns an unsubscribe function. */
export function onRowHover(listener: (ids: ReadonlySet<string>) => void): () => void {
  return subscribe((event) => {
    if (event.kind === 'highlight') {
      listener(event.ids === null ? NOTHING : new Set(event.ids));
    }
  });
}

/**
 * The row ids a pick on the map names.
 *
 * An entrance names itself and its cave, because the feature list holds entrances as rows of
 * their own while the cave registry holds the cave; whichever list is open marks the one it has.
 * A cluster and a trip are not rows of either list, so they name nothing.
 */
export function rowIdsOfSelection(selection: WorkspaceSelection | null): string[] {
  switch (selection?.kind) {
    case 'feature':
      return [selection.featureId];
    case 'entrance':
      return [selection.entranceId, selection.caveId];
    case 'cave':
      return [selection.caveId];
    default:
      return [];
  }
}

/** Follows what is picked on the map, in any window, as row ids. Returns an unsubscribe function. */
export function onMapPick(listener: (ids: ReadonlySet<string>) => void): () => void {
  return subscribe((event) => {
    if (event.kind === 'selection') {
      const ids = rowIdsOfSelection(event.selection);
      listener(ids.length === 0 ? NOTHING : new Set(ids));
    }
  });
}

/** The class a row picked on the map is drawn with. */
export const MAP_PICKED_ROW_CLASS = 'table-row-map-picked';

/**
 * A table's end of the link: what its rows announce when pointed at, and which of them the map
 * last picked — scrolled into view when it is on the page the table is showing.
 *
 * The row is found by antd's own `data-row-key`, which is the row's id for every table that uses
 * this; a pick of something the table is not showing marks nothing and moves nothing.
 */
export function useTableMapLink<T extends { id: string }>(): {
  picked: ReadonlySet<string>;
  rowClassName: (record: T) => string;
  rowHover: (record: T) => { onMouseEnter: () => void; onMouseLeave: () => void };
} {
  const [picked, setPicked] = useState<ReadonlySet<string>>(NOTHING);

  useEffect(() => onMapPick(setPicked), []);

  useEffect(() => {
    if (picked.size === 0) {
      return;
    }
    // Compared attribute by attribute rather than put into a selector, so no id needs escaping.
    for (const row of Array.from(document.querySelectorAll<HTMLElement>('tr[data-row-key]'))) {
      if (picked.has(row.dataset.rowKey ?? '')) {
        row.scrollIntoView?.({ block: 'nearest' });
        return;
      }
    }
  }, [picked]);

  // Leaving the page must not leave a mark behind on a map that is still open.
  useEffect(() => () => announceRowHover(null), []);

  return {
    picked,
    rowClassName: (record) => (picked.has(record.id) ? MAP_PICKED_ROW_CLASS : ''),
    rowHover: (record) => ({
      onMouseEnter: () => announceRowHover([record.id]),
      onMouseLeave: () => announceRowHover(null),
    }),
  };
}
