// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clusterCellBbox } from '../../geo/cluster.ts';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import type { EntranceFeatureCollection, FeatureCategory, FeatureKind } from './types.ts';
import { queryKeys } from './queryKeys.ts';

export function useMapLayers() {
  return useQuery({
    queryKey: queryKeys.mapLayers,
    queryFn: () => unwrap(api.GET('/api/v1/map-layers')),
    staleTime: 5 * 60_000,
  });
}

export type MapBackground = components['schemas']['MapBackgroundDto'];
export type MapBackgroundChoice = components['schemas']['MapLayerDocumentChoice'];

/**
 * The map backgrounds this installation publishes, each with whether a document may copy it,
 * what the shipped catalogue says and what an administrator decided. An administrator's reading:
 * the server refuses it to anybody who may not read the installation's settings.
 */
export function useMapBackgrounds(enabled = true) {
  return useQuery({
    queryKey: queryKeys.mapBackgrounds,
    queryFn: () => unwrap(api.GET('/api/v1/admin/map-backgrounds')),
    enabled,
  });
}

/**
 * Decides whether documents may copy one background: on, off, or back to what the catalogue
 * says. The answer is the row as it now stands, so the list takes it in place; and the published
 * catalogue is read again, because that is what a write-up's page chooses its background from —
 * a page already open must stop copying a source the moment an administrator says so.
 */
export function useChooseMapBackground() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, choice }: { id: number; choice: MapBackgroundChoice }) =>
      unwrap(
        api.PUT('/api/v1/admin/map-backgrounds/{id}/in-documents', {
          params: { path: { id } },
          body: { choice },
        }),
      ),
    onSuccess: (updated) => {
      queryClient.setQueryData<MapBackground[]>(queryKeys.mapBackgrounds, (rows) =>
        rows?.map((row) => (row.id === updated.id ? updated : row)),
      );
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapLayers });
    },
  });
}

/** Installation-wide map rendering limits, published by the server. */
export type MapConfig = components['schemas']['MapConfigDto'];

export function useMapConfig() {
  return useQuery({
    queryKey: queryKeys.mapConfig,
    queryFn: () => unwrap(api.GET('/api/v1/map/config')),
    staleTime: 5 * 60_000,
  });
}

export type SearchResult = components['schemas']['SearchResultDto'];
export type SearchFeatureItem = components['schemas']['SearchFeatureItemDto'];

/**
 * Global search: features of every kind, trip logs, and documents matched by what their text
 * says. Results carry no coordinates — navigate to the entity (or fetch it) instead of
 * centering the map from here.
 *
 * Pass `kind` when only one kind can be picked. The server's hit budget is shared across
 * kinds, so filtering the answer here instead would let commoner kinds crowd the wanted
 * one out of the response entirely.
 *
 * The document section is the one that is paged, and this hook asks only for its first page:
 * a search box shows the best few answers, and the total that comes back with them says
 * honestly how many more there are. The two query parameters that would change that answer —
 * which page, and whether replaced revisions are searched — are deliberately not passed here,
 * because they are not in the cache key and a request that varied them would be served the
 * previous one's rows.
 */
export function useSearch(q: string, kind?: FeatureKind) {
  return useQuery({
    queryKey: queryKeys.search(q, kind),
    queryFn: () => unwrap(api.GET('/api/v1/search', { params: { query: { q, kind } } })),
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
export async function fetchEntranceFeatures(bbox: string, zoom: number, tag?: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/cave-entrances', { params: { query: { bbox, zoom, tag } } }));
}

/**
 * Members of a low-zoom entrance cluster: its cluster cell (+margin) fetched at
 * a zoom above the server's clustering threshold, so points come back.
 */
export function useClusterEntrances(lon: number, lat: number, zoom: number, tag?: string) {
  const bbox = clusterCellBbox(lon, lat, zoom);
  return useQuery({
    queryKey: ['map', 'cluster-entrances', bbox, tag ?? null] as const,
    queryFn: () => fetchEntranceFeatures(bbox, 14, tag),
    staleTime: 30_000,
  });
}

/** Optional filters the map feature layer applies on top of the viewport bbox. */
export interface MapFeatureFilters {
  /** Comma-separated FeatureKind names, e.g. "generic,cave". */
  kinds?: string;
  featureTypeId?: number;
  category?: FeatureCategory;
  tag?: string;
}

/**
 * Imperative fetch used by the OpenLayers feature-layer loader (not a hook). Protected
 * non-point geometry arrives with a null geometry — the layer must tolerate it.
 */
export async function fetchMapFeatures(
  bbox: string,
  filters?: MapFeatureFilters,
): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/features', { params: { query: { bbox, ...filters } } }));
}

// ---------------------------------------------------------------------------
// Work areas
// ---------------------------------------------------------------------------

export type WorkArea = components['schemas']['WorkAreaDto'];
export type WorkAreaCollection = components['schemas']['WorkAreaCollectionDto'];

/**
 * Every work area this caller may read, as one answer.
 *
 * There are tens of these and not thousands — a club works the ground it can reach — so the whole
 * tree is fetched once and levelled in the browser. That is what lets the dashboard list, the
 * overview map and the zoom-to-area link share a cache entry instead of asking three times and
 * risking three different answers.
 */
export function useWorkAreas(enabled = true) {
  return useQuery({
    queryKey: queryKeys.workAreas,
    queryFn: () => unwrap(api.GET('/api/v1/work-areas', {})),
    enabled,
    retry: false,
  });
}
