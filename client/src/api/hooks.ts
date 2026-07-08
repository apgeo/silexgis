// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client.ts';
import type { components } from './schema';

export type CaveListItem = components['schemas']['CaveListItemDto'];
export type CaveDetail = components['schemas']['CaveDto'];
export type CaveWrite = components['schemas']['CaveWriteRequest'];
export type Entrance = components['schemas']['EntranceDto'];
export type EntranceWrite = components['schemas']['EntranceWriteRequest'];
export type MapLayerInfo = components['schemas']['MapLayerDto'];
export type Taxonomy = components['schemas']['TaxonomyDto'];
export type FeatureType = components['schemas']['FeatureTypeDto'];
export type EntranceFeatureCollection = components['schemas']['FeatureCollection'];

// Query keys live here so invalidation stays precise.
export const queryKeys = {
  me: ['me'] as const,
  mapLayers: ['map-layers'] as const,
  taxonomy: (kind: string) => ['taxonomy', kind] as const,
  caves: (params: CaveListParams) => ['caves', 'list', params] as const,
  cave: (id: string) => ['caves', 'detail', id] as const,
  entrances: (caveId: string) => ['entrances', caveId] as const,
  caveSearch: (q: string) => ['cave-search', q] as const,
  nominatim: (q: string) => ['nominatim', q] as const,
};

async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await call;
  if (error !== undefined || data === undefined) {
    throw new Error(`API error ${response.status}`);
  }
  return data;
}

export function useMe() {
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: () => unwrap(api.GET('/api/v1/me')),
  });
}

export function useMapLayers() {
  return useQuery({
    queryKey: queryKeys.mapLayers,
    queryFn: () => unwrap(api.GET('/api/v1/map-layers')),
    staleTime: 5 * 60_000,
  });
}

function taxonomyQuery(path: '/api/v1/cave-types' | '/api/v1/entrance-types' | '/api/v1/rock-types', kind: string) {
  return {
    queryKey: queryKeys.taxonomy(kind),
    queryFn: () => unwrap(api.GET(path)),
    staleTime: 5 * 60_000,
  };
}

export function useCaveTypes() {
  return useQuery(taxonomyQuery('/api/v1/cave-types', 'cave-types'));
}

export function useEntranceTypes() {
  return useQuery(taxonomyQuery('/api/v1/entrance-types', 'entrance-types'));
}

export function useRockTypes() {
  return useQuery(taxonomyQuery('/api/v1/rock-types', 'rock-types'));
}

export function useFeatureTypes() {
  return useQuery({
    queryKey: queryKeys.taxonomy('feature-types'),
    queryFn: () => unwrap(api.GET('/api/v1/feature-types')),
    staleTime: 5 * 60_000,
  });
}

export interface CaveListParams {
  page?: number;
  pageSize?: number;
  sort?: string;
  caveTypeId?: number;
  region?: string;
  search?: string;
}

export function useCaves(params: CaveListParams) {
  return useQuery({
    queryKey: queryKeys.caves(params),
    queryFn: () => unwrap(api.GET('/api/v1/caves', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useCave(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cave(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

export function useEntrances(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.entrances(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/entrances', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
  });
}

export function useCaveSearch(q: string) {
  return useQuery({
    queryKey: queryKeys.caveSearch(q),
    queryFn: () => unwrap(api.GET('/api/v1/search', { params: { query: { q } } })),
    enabled: q.trim().length >= 2,
    staleTime: 30_000,
  });
}

export interface NominatimPlace {
  place_id: number;
  display_name: string;
  lon: string;
  lat: string;
}

export function useNominatim(q: string) {
  return useQuery({
    queryKey: queryKeys.nominatim(q),
    queryFn: async (): Promise<NominatimPlace[]> => {
      const url = `https://nominatim.openstreetmap.org/search?format=jsonv2&limit=5&q=${encodeURIComponent(q)}`;
      const response = await fetch(url, { headers: { Accept: 'application/json' } });
      return response.ok ? ((await response.json()) as NominatimPlace[]) : [];
    },
    enabled: q.trim().length >= 3,
    staleTime: 60_000,
    retry: false,
  });
}

/** Imperative fetch used by the OpenLayers entrance-layer loader (not a hook). */
export async function fetchEntranceFeatures(bbox: string, zoom: number): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/cave-entrances', { params: { query: { bbox, zoom } } }));
}

/** Imperative fetch used by the OpenLayers surface-feature loader (not a hook). */
export async function fetchSurfaceFeatureCollection(bbox: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/surface-features', { params: { query: { bbox } } }));
}

function useInvalidateCaves() {
  const queryClient = useQueryClient();
  return (caveId?: string) => {
    void queryClient.invalidateQueries({ queryKey: ['caves'] });
    if (caveId) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.entrances(caveId) });
    }
  };
}

export function useCreateCave() {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: (body: CaveWrite) => unwrap(api.POST('/api/v1/caves', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCave(id: string) {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: (body: CaveWrite) =>
      unwrap(api.PUT('/api/v1/caves/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(id),
  });
}

export function useDeleteCave() {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/caves/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export function useCreateEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: (body: EntranceWrite) =>
      unwrap(api.POST('/api/v1/caves/{caveId}/entrances', { params: { path: { caveId } }, body })),
    onSuccess: () => invalidate(caveId),
  });
}

export function useUpdateEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: EntranceWrite }) =>
      unwrap(api.PUT('/api/v1/cave-entrances/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(caveId),
  });
}

export function useDeleteEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/cave-entrances/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(caveId),
  });
}
