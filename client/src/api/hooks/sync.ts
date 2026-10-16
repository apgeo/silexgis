// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

export type SyncSet = components['schemas']['SyncSetDto'];
export type SyncSetWrite = components['schemas']['SyncSetWriteRequest'];

/**
 * The caller's own sync sets. This listing is never widened: it answers with the sets the signed-in
 * account owns and no others, whatever an installation allows an administrator to read one set by.
 */
export function useSyncSets() {
  return useQuery({
    queryKey: queryKeys.syncSets,
    queryFn: () => unwrap(api.GET('/api/v1/sync/sets')),
  });
}

export function useCreateSyncSet() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SyncSetWrite) => unwrap(api.POST('/api/v1/sync/sets', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.syncSets }),
  });
}

export function useUpdateSyncSet() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: SyncSetWrite }) =>
      unwrap(api.PUT('/api/v1/sync/sets/{id}', { params: { path: { id } }, body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.syncSets }),
  });
}

/**
 * Revokes a selection. Nothing that was synced is touched — a sync set names caves, it never owned
 * any of them — so what this ends is a device's licence to keep asking for them.
 */
export function useDeleteSyncSet() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/sync/sets/{id}', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.syncSets }),
  });
}
