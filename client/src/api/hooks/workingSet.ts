// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { DEFAULT_SELECTION_SET_MAX, useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { api } from '../client.ts';
import type { FeatureListItem } from './features.ts';
import { useMapConfig } from './map.ts';
import { unwrap } from './shared.ts';

/** Which list a working set is taken from. */
export type WorkingSetList = 'caves' | 'features';

export interface WorkingSetQuery {
  /** The list's filter token, exactly as its own request sends it. */
  filter?: string;
  /** The cave registry's sort, so "the first N" are the first N the list shows. */
  sort?: string;
  /** The cave registry's map window, decided by the server as the list's is. */
  bbox?: string;
}

/**
 * The first page of a list's result, as large as the working set can hold, read through the same
 * route and the same parameters as the list itself — so what goes into the set is what the list
 * shows, under the same visibility and the same filter refusals.
 */
export async function fetchWorkingSetRows(
  list: WorkingSetList,
  query: WorkingSetQuery,
  cap: number,
): Promise<{ rows: { id: string; kind?: string | null }[]; total: number }> {
  if (list === 'caves') {
    const page = await unwrap(
      api.GET('/api/v1/caves', {
        params: { query: { page: 1, pageSize: cap, filter: query.filter, sort: query.sort, bbox: query.bbox } },
      }),
    );
    return { rows: page.items.map((item) => ({ id: item.id, kind: 'cave' })), total: page.totalItems };
  }
  const page = await unwrap(
    api.GET('/api/v1/features', { params: { query: { page: 1, pageSize: cap, filter: query.filter } } }),
  );
  return { rows: page.items.map((item) => ({ id: item.id, kind: item.kind })), total: page.totalItems };
}

/**
 * The installation's ceiling on the working set, from the map configuration, kept in the store so
 * every way of adding to the set — a list's "Select all found", a modifier-click — stops at the
 * same number the server bounds the summary's request by. The default stands in until the
 * configuration has been read.
 */
export function useSelectionSetMax(): number {
  const { data } = useMapConfig();
  const configured = data?.selectionMax;
  const stored = useWorkspaceStore((s) => s.selectionSetMax);
  useEffect(() => {
    if (configured !== undefined) {
      useWorkspaceStore.getState().setSelectionSetMax(configured);
    }
  }, [configured]);
  return configured ?? stored ?? DEFAULT_SELECTION_SET_MAX;
}

/**
 * How many ids one request for the selection's summary names. The ids travel in the address, and
 * an address is bounded long before the query is: the server and the usual proxies in front of it
 * refuse a request line over about 8 KB, which is under two hundred ids. A hundred is half of
 * that, so a larger selection is asked about in several requests rather than refused whole.
 */
export const SELECTION_IDS_PER_REQUEST = 100;

/** The ids in groups of at most `size`, in order. */
export function chunkIds(ids: readonly string[], size = SELECTION_IDS_PER_REQUEST): string[][] {
  const chunks: string[][] = [];
  for (let at = 0; at < ids.length; at += size) {
    chunks.push(ids.slice(at, at + size));
  }
  return chunks;
}

/**
 * What the working set's objects are, read through the feature list under the reader's own
 * visibility: an object they may not read simply is not among the items.
 */
export function useSelectionSummary(ids: readonly string[]) {
  return useQuery({
    // Under the feature list's own prefix, so whatever invalidates the list after an edit
    // refreshes the summary too.
    queryKey: ['features', 'list', 'selection', [...ids]] as const,
    queryFn: async (): Promise<{ items: FeatureListItem[] }> => {
      const pages = await Promise.all(
        chunkIds(ids).map((chunk) =>
          unwrap(api.GET('/api/v1/features', { params: { query: { ids: chunk, pageSize: chunk.length } } })),
        ),
      );
      return { items: pages.flatMap((page) => page.items) };
    },
    enabled: ids.length > 0,
    placeholderData: keepPreviousData,
  });
}
