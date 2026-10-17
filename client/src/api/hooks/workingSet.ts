// SPDX-License-Identifier: AGPL-3.0-or-later
import { api } from '../client.ts';
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
