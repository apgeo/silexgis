// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQuery, type QueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import { queryKeys } from './queryKeys.ts';
import { retryUnlessRefused, unwrap } from './shared.ts';
import type { GraphNeighbourhood, GraphSearchHit } from './types.ts';

/** What one request to the neighbourhood route asks: a whole first hop, or one category's next page. */
export interface GraphNeighbourhoodParams {
  type: string;
  id: string;
  category?: string;
  offset?: number;
}

/**
 * One hop around one item, read through the query cache and not kept in a component: the explorer
 * accumulates answers in its own model and asks for them one at a time as the user expands nodes,
 * which is not the shape a `useQuery` per node fits. `fresh` skips the cache (a refresh); otherwise
 * an answer fetched a moment ago serves a second expansion of the same node.
 *
 * Deliberately no retry on a refusal: an unreadable or missing seed answers 404 and that is the answer.
 */
export function fetchNeighbourhood(
  queryClient: QueryClient,
  params: GraphNeighbourhoodParams,
  fresh = false,
): Promise<GraphNeighbourhood> {
  return queryClient.fetchQuery({
    queryKey: queryKeys.graphNeighbourhood(params.type, params.id, params.category, params.offset),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/graph/neighbourhood', {
          params: {
            query: {
              type: params.type,
              id: params.id,
              ...(params.category === undefined ? {} : { category: params.category, offset: params.offset ?? 0 }),
            },
          },
        }),
      ),
    staleTime: fresh ? 0 : 30_000,
    retry: false,
  });
}

/** Items matching typed text, a few per kind, as the caller may see them. Blank text asks nothing. */
export function useGraphSearch(q: string, types: readonly string[] = [], limit = 5) {
  const text = q.trim();
  const typeList = [...types].sort().join(',');
  return useQuery({
    queryKey: queryKeys.graphSearch(text, typeList, limit),
    queryFn: (): Promise<GraphSearchHit[]> =>
      unwrap(
        api.GET('/api/v1/graph/search', {
          params: { query: { q: text, limit, ...(typeList === '' ? {} : { types: typeList }) } },
        }),
      ),
    enabled: text.length > 0,
    retry: retryUnlessRefused,
  });
}
