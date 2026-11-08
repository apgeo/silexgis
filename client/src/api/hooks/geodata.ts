// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import { uploadWithProgress, type UploadProgress } from '../uploadWithProgress.ts';
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

/**
 * Reads the lists under a key again after a write, including one whose first read is still on its
 * way. Invalidating alone does not do that: a query that has no answer yet keeps the request
 * already running instead of starting another, and that request may have been answered by the
 * server before the write landed — so a file dropped while its tab was still loading never
 * appeared in the list. Cancelling first makes the next read start after the write.
 */
async function refreshAfterWrite(queryClient: QueryClient, queryKey: readonly unknown[]): Promise<void> {
  await queryClient.cancelQueries({ queryKey });
  await queryClient.invalidateQueries({ queryKey });
}

function useInvalidateGeofiles() {
  const queryClient = useQueryClient();
  return () => void refreshAfterWrite(queryClient, ['geofiles']);
}

/** A file to send, and who to tell how much of it has gone up. */
export interface FileUpload {
  file: File;
  onProgress?: (progress: UploadProgress) => void;
}

export function useUploadGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: async ({ file, onProgress }: FileUpload): Promise<GeofileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return uploadWithProgress<GeofileInfo>('/api/v1/geofiles', form, { onProgress });
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
  return () => void refreshAfterWrite(queryClient, ['raster-maps']);
}

export function useUploadRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: async ({ file, onProgress }: FileUpload): Promise<RasterMapInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return uploadWithProgress<RasterMapInfo>('/api/v1/georeferenced-maps', form, { onProgress });
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

export type RasterMapPlacement = components['schemas']['MapPlacementDto'];
export type RasterMapPlacementPreview = components['schemas']['MapPlacementPreviewDto'];
export type RasterMapRenderRequest = components['schemas']['MapSheetRenderRequest'];

/** Draws a map sheet's document again: another page, other layers or another resolution. */
export function useRenderRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: RasterMapRenderRequest }) =>
      unwrap(api.POST('/api/v1/georeferenced-maps/{id}/render', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

/** Sets where a drawn sheet lies; the server then publishes it in the background. */
export function useSaveRasterPlacement() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: RasterMapPlacement }) =>
      unwrap(api.PUT('/api/v1/georeferenced-maps/{id}/placement', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

/** Removes a hand placement; a document that carries its own place goes back to it. */
export function useClearRasterPlacement() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE('/api/v1/georeferenced-maps/{id}/placement', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/**
 * Tries a placement on the sheet's small picture without keeping it. Nothing is cached: every
 * answer belongs to one arrangement of the points, and the caller passes a signal so a newer
 * arrangement can abort the request it supersedes.
 */
export function usePreviewRasterPlacement() {
  return useMutation({
    mutationFn: ({ id, body, signal }: { id: string; body: RasterMapPlacement; signal?: AbortSignal }) =>
      unwrap(
        api.POST('/api/v1/georeferenced-maps/{id}/placement/preview', {
          params: { path: { id } },
          body,
          signal,
        }),
      ),
  });
}

export type PlacementSystem = components['schemas']['PlacementSystemDto'];
export type SheetStationPoint = components['schemas']['SheetStationPointDto'];

/**
 * One map, watched while its publishing runs: asked again every two seconds until it settles.
 * Kept under the list's key, so every write that refreshes the list refreshes this too.
 */
export function useRasterMap(id: string | undefined, watch = false) {
  return useQuery({
    queryKey: ['raster-maps', 'detail', id ?? ''] as const,
    queryFn: () => unwrap(api.GET('/api/v1/georeferenced-maps/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id) && watch,
    refetchInterval: (query) =>
      query.state.data?.status === 'uploaded' || query.state.data?.status === 'processing' ? 2000 : false,
  });
}

/** The coordinate systems control points may be typed in on this installation. */
export function usePlacementSystems() {
  return useQuery({
    queryKey: ['raster-maps', 'placement-systems'] as const,
    queryFn: () => unwrap(api.GET('/api/v1/georeferenced-maps/placement-systems')),
    staleTime: 30 * 60_000,
  });
}

/** Survey stations pinned on a sheet's picture, with where the survey puts them. */
export function useSheetStationPoints(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: ['raster-maps', 'station-points', id ?? ''] as const,
    queryFn: () =>
      unwrap(api.GET('/api/v1/georeferenced-maps/{id}/station-points', { params: { path: { id: id! } } })),
    enabled: Boolean(id) && enabled,
    retry: false,
  });
}

/** The sheet's placement as a IIIF Georeference Annotation, for saving to a file (not a hook). */
export async function fetchRasterAnnotation(id: string): Promise<unknown> {
  return unwrap(api.GET('/api/v1/georeferenced-maps/{id}/placement/annotation', { params: { path: { id } } }));
}

/** Places a sheet from a Georeference Annotation made here or in another editor. */
export function useImportRasterAnnotation() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: ({ id, annotation }: { id: string; annotation: unknown }) =>
      unwrap(
        api.PUT('/api/v1/georeferenced-maps/{id}/placement/annotation', {
          params: { path: { id } },
          body: annotation as never,
        }),
      ),
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
