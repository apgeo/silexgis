// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import type { FilterQueryBody } from './types.ts';
import { queryKeys } from './queryKeys.ts';

export type FilterQueryResult = components['schemas']['FilterQueryResponse'];
export type FilterVocabularyResult = components['schemas']['FilterVocabularyResponse'];
export type FilterHitDto = components['schemas']['FilterHitDto'];

/**
 * What each kind of object can be asked, for this caller.
 *
 * Cached hard. It changes when an administrator edits the taxonomy, not while somebody is typing,
 * and every control that draws a filter reads it — so refetching per mount would put a request
 * behind every dropdown on the page.
 */
export function useFilterVocabulary() {
  return useQuery({
    queryKey: queryKeys.filterVocabulary,
    queryFn: () => unwrap(api.GET('/api/v1/filters/vocabulary')),
    staleTime: 10 * 60_000,
  });
}

/**
 * Runs a filter.
 *
 * `keepPreviousData` is what makes a type-ahead readable: without it the list empties on every
 * keystroke and the rows jump, which reads as the control losing what it had found.
 */
export function useFilterQuery(body: FilterQueryBody, enabled = true) {
  return useQuery({
    queryKey: queryKeys.filterQuery(body),
    queryFn: () => unwrap(api.POST('/api/v1/filters/query', { body })),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/**
 * Describes rows somebody already chose.
 *
 * An id the caller may not see comes back missing rather than refused, so the caller of this hook
 * must treat an absent row as "cannot show this" — never as an error, and never by falling back to
 * printing the identifier, which would put a raw uuid on screen where a name belongs.
 */
export function useFilterResolve(world: string, ids: readonly string[]) {
  return useQuery({
    queryKey: queryKeys.filterResolve(world, ids),
    queryFn: () => unwrap(api.POST('/api/v1/filters/resolve', {
      body: { world, ids: [...ids] },
    })),
    enabled: ids.length > 0,
    // Names change rarely and this is asked every time a form with a stored value opens.
    staleTime: 5 * 60_000,
  });
}
