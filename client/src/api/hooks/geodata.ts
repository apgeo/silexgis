// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import type { EntranceFeatureCollection, GeofileListParams, RasterMapListParams } from './types.ts';
import { queryKeys } from './queryKeys.ts';
import type { GeofileSourceOptions } from './imports.ts';

export type GeofileInfo = components['schemas']['GeofileDto'];
export type GeofileUpdate = components['schemas']['GeofileUpdateRequest'];

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

export type RasterMapInfo = components['schemas']['GeoreferencedMapDto'];
export type RasterMapUpdate = components['schemas']['GeoreferencedMapUpdateRequest'];

export function useRasterMaps(params: RasterMapListParams, pollWhileProcessing = false) {
  return useQuery({
    queryKey: queryKeys.rasterMaps(params),
    queryFn: () => unwrap(api.GET('/api/v1/georeferenced-maps', { params: { query: params } })),
    placeholderData: keepPreviousData,
    // COG normalization runs in a background job; the signed CogUrl also has a
    // 10-minute lifetime, so refresh periodically either way.
    refetchInterval: pollWhileProcessing
      ? (query) =>
          query.state.data?.items.some(
            (m) => m.status === 'uploaded' || m.status === 'processing',
          )
            ? 2000
            : 8 * 60_000
      : 8 * 60_000,
  });
}

function useInvalidateRasterMaps() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['raster-maps'] });
}

export function useUploadRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: async (file: File): Promise<RasterMapInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/georeferenced-maps', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useUpdateRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: RasterMapUpdate }) =>
      unwrap(api.PUT('/api/v1/georeferenced-maps/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/georeferenced-maps/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export function useGeofile(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.geofile(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/geofiles/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

/**
 * The header of a delimited upload, so a wrong coordinate-column guess can be corrected.
 * Only a delimited upload has one — callers must not ask about anything else, because the
 * refusal is a browser console error on every review of a GPX.
 */
export function useGeofileColumns(geofileId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.geofileColumns(geofileId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/geofiles/{id}/columns', { params: { path: { id: geofileId! } } })),
    enabled: Boolean(geofileId) && enabled,
    retry: false,
  });
}

export function useReimportGeofile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, sourceOptions }: { id: string; sourceOptions?: GeofileSourceOptions }) =>
      unwrap(
        api.POST('/api/v1/geofiles/{id}/reimport', {
          params: { path: { id } },
          body: { sourceOptions: sourceOptions ?? null },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['geofiles'] });
      void queryClient.invalidateQueries({ queryKey: ['import-preview'] });
      void queryClient.invalidateQueries({ queryKey: ['geofile-columns'] });
    },
  });
}
