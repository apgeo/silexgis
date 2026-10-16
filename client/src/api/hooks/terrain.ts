// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import type { TerrainBuildPageParams } from './types.ts';
import { queryKeys } from './queryKeys.ts';

export type TerrainBuild = components['schemas']['TerrainBuildDto'];
export type TerrainBuildDetail = components['schemas']['TerrainBuildDetailDto'];
export type TerrainBuildSource = components['schemas']['TerrainBuildSourceDto'];
export type TerrainBuildSourceRequest = components['schemas']['TerrainBuildSourceRequest'];
export type TerrainBuildSubmitRequest = components['schemas']['TerrainBuildSubmitRequest'];
export type TerrainRasterUpload = components['schemas']['TerrainRasterUploadDto'];
export type TerrainBuildStatus = components['schemas']['TerrainBuildStatus'];
export type TerrainBuildPhase = components['schemas']['TerrainBuildPhase'];
export type TerrainBuildSourceKind = components['schemas']['TerrainBuildSourceKind'];
export type TerrainHeightDatum = components['schemas']['TerrainHeightDatum'];

/** Whether a build is still going, which is the only thing worth asking the server again about. */
export function terrainBuildUnsettled(status: TerrainBuildStatus): boolean {
  return status === 'queued' || status === 'running';
}

/**
 * How long to wait before asking again, or `false` for "stop asking".
 *
 * Exported and pure so the rule that a settled build is left alone can be asserted directly,
 * rather than inferred from a live query that would have to be watched for two seconds to prove
 * it did nothing.
 */
export function terrainBuildPollInterval(
  status: TerrainBuildStatus | undefined,
): number | false {
  return status !== undefined && terrainBuildUnsettled(status) ? 2000 : false;
}

/**
 * The same rule for a page of builds: ask again only while something on it is unfinished.
 *
 * A rule of its own rather than the one above applied to a row, and exported for the same reason:
 * a list left open on nothing but finished builds must stop asking, and that is only provable by
 * naming the rule the list actually uses.
 */
export function terrainListPollInterval(
  items: { status: TerrainBuildStatus }[] | undefined,
): number | false {
  return (items ?? []).some((build) => terrainBuildUnsettled(build.status)) ? 2000 : false;
}

/**
 * Every build this installation has made, newest first.
 *
 * A bake takes eleven seconds over a pilot rectangle and hours over a country, so the list keeps
 * asking while anything on it is unfinished and stops the moment nothing is — the interval
 * disables itself rather than being cleared by whoever navigated away.
 */
export function useTerrainBuilds(params: TerrainBuildPageParams, enabled = true) {
  return useQuery({
    queryKey: queryKeys.terrainBuildList(params),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/terrain/builds', {
          params: { query: { page: params.page, pageSize: params.pageSize } },
        }),
      ),
    enabled,
    placeholderData: keepPreviousData,
    refetchInterval: (query) => terrainListPollInterval(query.state.data?.items),
    // Newest first is the server's own ordering; there is nothing to sort by here.
  });
}

/**
 * One build with its sources and the tail of what the tool itself said.
 *
 * The log tail and the source list live only on this response, so a running build is watched
 * through here rather than through the list — and only while it is running.
 */
export function useTerrainBuild(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.terrainBuild(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/terrain/builds/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    refetchInterval: (query) => terrainBuildPollInterval(query.state.data?.build.status),
  });
}

/**
 * Starts a build over a rectangle.
 *
 * The answer is the build row, not the queue row, so the list is asked again rather than seeded:
 * the server decides where a new build sorts and what phase it starts in.
 */
export function useSubmitTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: TerrainBuildSubmitRequest) =>
      unwrap(api.POST('/api/v1/terrain/builds', { body: request })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
    },
  });
}

/**
 * Sends one raster up, and answers the reference a build declares it by.
 *
 * Nothing is invalidated: an uploaded file is not a build and does not appear anywhere until a
 * submit names it.
 */
export function useUploadTerrainRaster() {
  return useMutation({
    mutationFn: async (file: File): Promise<TerrainRasterUpload> => {
      const form = new FormData();
      form.append('file', file, file.name);
      // Multipart: hand the FormData through untouched (the browser sets the boundary).
      return unwrap(
        api.POST('/api/v1/terrain/rasters', {
          body: form as never,
          bodySerializer: (b: unknown) => b as FormData,
        }),
      );
    },
  });
}

/**
 * The directories on the server this installation may read rasters from.
 *
 * Refused outright to anyone who is not a full administrator, because the answer is the operator's
 * own directory layout — a fact about the machine rather than about anything in it. Callers gate
 * on that before enabling this, so the request is never made and never refused; `retry` is off
 * regardless, since a refusal will not become an acceptance by being asked twice.
 */
export function useTerrainSourceDirectories(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.terrainSourceDirectories,
    queryFn: () => unwrap(api.GET('/api/v1/terrain/source-directories')),
    enabled,
    retry: false,
    staleTime: 5 * 60_000,
  });
}

/**
 * Everything that changes which elevation model the 3D scene draws.
 *
 * The invalidation is the load-bearing part, not decoration. What the scene draws arrives with the
 * map configuration, which is treated as fresh for five minutes — so without this, choosing terrain
 * answers immediately, changes nothing on screen, and the wait looks like a bad bake rather than a
 * cached answer. The scene picks the new ground up on its own once the configuration is re-read:
 * each build is served from an address of its own, and a terrain source whose address has changed
 * is what makes the engine load one at all.
 */
export function useChooseTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/terrain/builds/{id}/active', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapConfig });
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
    },
  });
}

/** Stops drawing a build, which leaves the scene on bare ground until something else is chosen. */
export function useStopDrawingTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE('/api/v1/terrain/builds/{id}/active', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapConfig });
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
    },
  });
}

export type TerrainDerivativeLayerInfo = components['schemas']['TerrainDerivativeLayerDto'];
export type TerrainDerivativeRasterInfo = components['schemas']['TerrainDerivativeRasterDto'];
export type TerrainDerivativeCreate = components['schemas']['TerrainDerivativeCreateRequest'];

/** How long a computed picture's addresses stay usable, less a margin to re-read them in. */
const TERRAIN_DERIVATIVE_REFRESH_MS = 8 * 60_000;

/**
 * The computed pictures of the ground, each carrying the addresses of its rasters and whether the
 * elevation beneath it has since been replaced.
 *
 * Re-read while a picture is still being computed, and re-read slowly the rest of the time: the
 * addresses in each raster carry a signature that expires after ten minutes, so a page left open
 * on the map would otherwise be holding layers whose next range request is refused.
 */
export function useTerrainDerivatives(enabled = true) {
  return useQuery({
    queryKey: queryKeys.terrainDerivatives,
    queryFn: () => unwrap(api.GET('/api/v1/terrain/derivatives')),
    enabled,
    refetchInterval: (query) => terrainDerivativePollInterval(query.state.data),
  });
}

/** A picture that is still being drawn, or waiting its turn to be. */
export function terrainDerivativeUnsettled(status: TerrainDerivativeLayerInfo['status']): boolean {
  return status === 'queued' || status === 'computing';
}

/**
 * How often the register of pictures is re-read: closely while any is still being computed, and
 * slowly the rest of the time.
 *
 * Never off, unlike the builds list, because the addresses in each raster expire; and a rule of
 * its own, exported, for the same reason that list's is — a register left open on finished
 * pictures must drop to the slow rate, and that is only provable by naming the rule it uses.
 */
export function terrainDerivativePollInterval(
  items: { status: TerrainDerivativeLayerInfo['status'] }[] | undefined,
): number {
  return (items ?? []).some((layer) => terrainDerivativeUnsettled(layer.status))
    ? 2000
    : TERRAIN_DERIVATIVE_REFRESH_MS;
}

/** Asks for a picture of the ground to be computed from one elevation build. */
export function useRequestTerrainDerivative() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: TerrainDerivativeCreate) =>
      unwrap(api.POST('/api/v1/terrain/derivatives', { body: request })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainDerivatives });
    },
  });
}

/** Removes a computed picture and the rasters it left on disk. */
export function useDeleteTerrainDerivative() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/terrain/derivatives/{id}', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainDerivatives });
    },
  });
}

/**
 * Calls a build off.
 *
 * Answers with the build as it now stands: a queued one is already stopped, a running one is still
 * running with the time the request was made, until it reaches a point where it can stop. The list
 * is re-read either way, and keeps polling while anything on it is unsettled, so the second state
 * turns into the first on screen without anything further being pressed.
 */
export function useCancelTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/terrain/builds/{id}/cancel', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
    },
  });
}

/** Puts a stopped build back on the queue, to carry on from where it stopped. */
export function useRetryTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/terrain/builds/{id}/retry', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
    },
  });
}

/**
 * Removes a build and the disk it was keeping.
 *
 * The map configuration goes with it even though the build being drawn cannot be deleted: another
 * account may have chosen different terrain since this list was read, and a delete is the moment
 * this browser is talking to the server anyway.
 */
export function useDeleteTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/terrain/builds/{id}', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapConfig });
    },
  });
}
