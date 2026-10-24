// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { unwrap } from './shared.ts';

export type BasemapStatus = components['schemas']['BasemapStatusDto'];
export type BasemapDownload = components['schemas']['BasemapDownloadDto'];
export type BasemapBuild = components['schemas']['BasemapBuildDto'];
export type BasemapEstimate = components['schemas']['BasemapEstimateDto'];
export type BasemapRegion = components['schemas']['BasemapRegionDto'];

/** What a download or an estimate asks for: a box, its last level, and a build — none for the latest. */
export interface BasemapRegionRequest {
  region: BasemapRegion;
  maxZoom: number;
  build: string | null;
}

/** Whether a download is still going, so the page keeps asking how far it has got. */
export function isUnderway(download: BasemapDownload | null | undefined): boolean {
  return download?.state === 'queued' || download?.state === 'running';
}

/**
 * The installation's own basemap as an administrator manages it: whether it is served, the extract
 * served now and the latest download. Asked again every two seconds while a download is underway.
 */
export function useBasemapStatus(enabled = true) {
  return useQuery({
    queryKey: queryKeys.basemap,
    queryFn: () => unwrap(api.GET('/api/v1/admin/basemap')),
    enabled,
    refetchInterval: (query) => (isUnderway(query.state.data?.download) ? 2000 : false),
  });
}

/** The planet builds the configured source publishes, newest first. Read only when asked for. */
export function useBasemapBuilds(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.basemapBuilds,
    queryFn: () => unwrap(api.GET('/api/v1/admin/basemap/builds')),
    enabled,
    staleTime: 10 * 60_000,
    retry: false,
  });
}

/** What an extract of a region would weigh, and whether it would be accepted. Downloads nothing. */
export function useEstimateBasemap() {
  return useMutation({
    mutationFn: (body: BasemapRegionRequest) => unwrap(api.POST('/api/v1/admin/basemap/estimate', { body })),
  });
}

/** Downloads a region as the installation's basemap, in the background. */
export function useStartBasemapDownload() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: BasemapRegionRequest) => unwrap(api.POST('/api/v1/admin/basemap/downloads', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.basemap }),
  });
}

/**
 * Calls a download off. When the status shows it ended, the published catalogue is read again
 * too: a finished download is what puts the self-hosted entry on the map.
 */
export function useCancelBasemapDownload() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      unwrap(api.POST('/api/v1/admin/basemap/downloads/{id}/cancel', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.basemap }),
  });
}
