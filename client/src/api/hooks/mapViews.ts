// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';

export type MapViewInfo = components['schemas']['MapViewDto'];
export type MapViewWrite = components['schemas']['MapViewWriteRequest'];

export function useMapViews() {
  return useQuery({
    queryKey: ['map-views'] as const,
    queryFn: () => unwrap(api.GET('/api/v1/map-views')),
  });
}

function useInvalidateMapViews() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['map-views'] });
}

export function useCreateMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: (body: MapViewWrite) => unwrap(api.POST('/api/v1/map-views', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/map-views/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export function useShareMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/map-views/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** Imperative fetch for the anonymous shared-view page (no auth attached needed). */
export async function fetchSharedView(token: string) {
  return unwrap(api.GET('/api/v1/shared/views/{token}', { params: { path: { token } } }));
}
