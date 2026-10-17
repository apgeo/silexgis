// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import { unwrap, unwrapVoid } from './shared.ts';
import type { components } from '../schema';
import type { SavedFilterAudience } from '../../filters/savedFilterAudience.ts';
import type { FilterDocument } from '../../filters/types.ts';

/**
 * Saved filters: named questions somebody keeps, privately, shared with a caving group or shared
 * with everyone signed in.
 *
 * A saved filter describes a question and grants nothing — whoever runs one is answered under
 * their own access — so nothing here caches rows. What is cached is the list of questions, which
 * changes only when somebody saves, renames, shares or deletes one.
 */

export type SavedFilter = components['schemas']['SavedFilterDto'];
export type SavedFilterState = SavedFilter['state'];

/**
 * What a saved filter is written as, with the document in the shape the filter logic builds.
 *
 * The generated contract describes the document's condition tree as an unknown — a polymorphic
 * list does not survive the description — so the request is assembled here from the hand-written
 * filter types and handed over at the edge.
 */
export interface SavedFilterWrite {
  name: string;
  description?: string | null;
  /** Null on an update keeps the stored question; required when creating. */
  document: FilterDocument | null;
  cavingGroupId?: string | null;
  /** Never `public`: the server refuses it for a saved filter. */
  visibility: SavedFilterAudience['visibility'];
}

const savedFilterKeys = {
  all: ['saved-filters'] as const,
  list: ['saved-filters', 'list'] as const,
};

/** The saved filters this account may read, each with what it reads as to them. */
export function useSavedFilters(enabled = true) {
  return useQuery({
    queryKey: savedFilterKeys.list,
    queryFn: () => unwrap(api.GET('/api/v1/saved-filters')),
    enabled,
    staleTime: 60_000,
  });
}

function useInvalidateSavedFilters() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: savedFilterKeys.all });
}

function body(write: SavedFilterWrite): components['schemas']['SavedFilterWriteRequest'] {
  return {
    name: write.name,
    description: write.description ?? null,
    document: write.document as unknown as components['schemas']['SavedFilterWriteRequest']['document'],
    cavingGroupId: write.cavingGroupId ?? null,
    visibility: write.visibility,
  };
}

export function useCreateSavedFilter() {
  const invalidate = useInvalidateSavedFilters();
  return useMutation({
    mutationFn: (write: SavedFilterWrite) =>
      unwrap(api.POST('/api/v1/saved-filters', { body: body(write) })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateSavedFilter() {
  const invalidate = useInvalidateSavedFilters();
  return useMutation({
    mutationFn: ({ id, write }: { id: string; write: SavedFilterWrite }) =>
      unwrap(api.PUT('/api/v1/saved-filters/{id}', { params: { path: { id } }, body: body(write) })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteSavedFilter() {
  const invalidate = useInvalidateSavedFilters();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/saved-filters/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** One saved filter, read fresh — for a control that applies it the moment it is picked. */
export async function fetchSavedFilter(id: string): Promise<SavedFilter> {
  return unwrap(api.GET('/api/v1/saved-filters/{id}', { params: { path: { id } } }));
}

/**
 * Whether a saved filter can be applied by a surface that runs `world`: it reads as runnable to
 * this account and it asks something about that world. Checked before applying, so a filter
 * about trips picked on the map is refused with a sentence rather than by the map dropping to
 * unfiltered as if a link had been broken.
 */
export function appliesTo(filter: SavedFilter, world: string): boolean {
  const document = documentOf(filter);
  return filter.state !== 'unreadable' && document !== null && document.scope.some((s) => s.world === world);
}

/** A saved filter's stored question in the shape the filter logic reads, or null when it has none. */
export function documentOf(filter: SavedFilter): FilterDocument | null {
  return (filter.document ?? null) as unknown as FilterDocument | null;
}
