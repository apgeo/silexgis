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
  surfaceFeatures: (params: SurfaceFeatureListParams) => ['surface-features', 'list', params] as const,
  surfaceFeature: (id: string) => ['surface-features', 'detail', id] as const,
  geofiles: (params: GeofileListParams) => ['geofiles', 'list', params] as const,
  attachments: (entityType: string, entityId: string) => ['attachments', entityType, entityId] as const,
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

export type SurfaceFeatureDetail = components['schemas']['SurfaceFeatureDto'];
export type SurfaceFeatureWrite = components['schemas']['SurfaceFeatureWriteRequest'];

// Imperative surface-feature calls used by the map edit controller (outside React).
export async function fetchSurfaceFeature(id: string): Promise<SurfaceFeatureDetail> {
  return unwrap(api.GET('/api/v1/surface-features/{id}', { params: { path: { id } } }));
}

export async function createSurfaceFeature(body: SurfaceFeatureWrite): Promise<SurfaceFeatureDetail> {
  return unwrap(api.POST('/api/v1/surface-features', { body }));
}

export async function updateSurfaceFeature(id: string, body: SurfaceFeatureWrite): Promise<SurfaceFeatureDetail> {
  return unwrap(api.PUT('/api/v1/surface-features/{id}', { params: { path: { id } }, body }));
}

export interface SurfaceFeatureListParams {
  page?: number;
  pageSize?: number;
  featureTypeId?: number;
  caveId?: string;
  search?: string;
}

export function useSurfaceFeatures(params: SurfaceFeatureListParams) {
  return useQuery({
    queryKey: queryKeys.surfaceFeatures(params),
    queryFn: () => unwrap(api.GET('/api/v1/surface-features', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useSurfaceFeature(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surfaceFeature(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/surface-features/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

function useInvalidateSurfaceFeatures() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['surface-features'] });
}

export function useUpdateSurfaceFeature() {
  const invalidate = useInvalidateSurfaceFeatures();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: SurfaceFeatureWrite }) => updateSurfaceFeature(id, body),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteSurfaceFeature() {
  const invalidate = useInvalidateSurfaceFeatures();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/surface-features/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type GeofileInfo = components['schemas']['GeofileDto'];
export type GeofileUpdate = components['schemas']['GeofileUpdateRequest'];

export interface GeofileListParams {
  page?: number;
  pageSize?: number;
  search?: string;
}

export function useGeofiles(params: GeofileListParams, pollWhileImporting = false) {
  return useQuery({
    queryKey: queryKeys.geofiles(params),
    queryFn: () => unwrap(api.GET('/api/v1/geofiles', { params: { query: params } })),
    placeholderData: keepPreviousData,
    // Imports run in a background job — keep the table live until they settle.
    refetchInterval: pollWhileImporting
      ? (query) =>
          query.state.data?.items.some(
            (g) => g.importStatus === 'uploaded' || g.importStatus === 'importing',
          )
            ? 2000
            : false
      : false,
  });
}

function useInvalidateGeofiles() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['geofiles'] });
}

export function useUploadGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: async (file: File): Promise<GeofileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      // Multipart: hand the FormData through untouched (the browser sets the boundary).
      return unwrap(api.POST('/api/v1/geofiles', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useUpdateGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: GeofileUpdate }) =>
      unwrap(api.PUT('/api/v1/geofiles/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/geofiles/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

/** Imperative fetch used by the OpenLayers geofile-layer loader (not a hook). */
export async function fetchGeofileFeatureCollection(id: string, bbox: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/geofiles/{id}/features', { params: { path: { id }, query: { bbox } } }));
}

export type FileInfo = components['schemas']['FileDto'];
export type AttachmentInfo = components['schemas']['AttachmentDto'];
export type AttachedEntityType = AttachmentInfo['entityType'];
export type AttachmentRole = AttachmentInfo['role'];

export function useAttachments(entityType: AttachedEntityType, entityId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.attachments(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/attachments', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: !!entityId,
    // Delivery URLs embed 10-minute tokens; refresh the list before they lapse.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

function useInvalidateAttachments() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['attachments'] });
}

export function useUploadFile() {
  return useMutation({
    mutationFn: async (file: File): Promise<FileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/files', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
  });
}

export function useCreateAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: (body: {
      fileId: string;
      entityType: AttachedEntityType;
      entityId: string;
      role: AttachmentRole;
      caption: string | null;
      sortOrder: number;
    }) => unwrap(api.POST('/api/v1/attachments', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/attachments/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
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
