// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { unwrap } from './shared.ts';

/** A cave's depth below its highest surveyed point, along its passages. Carries no position. */
export type CaveDepthProfile = components['schemas']['CaveDepthProfileDto'];

/** One reading of the depth profile. */
export type CaveDepthProfileSample = components['schemas']['CaveDepthProfileSampleDto'];

/** An area's entrances and sinkholes against the ground, class by class. */
export type AreaTerrainStatistics = components['schemas']['AreaTerrainStatisticsDto'];

/** The ground along a drawn line: elevation against distance, with ascent and descent. */
export type TerrainProfile = components['schemas']['TerrainProfileDto'];

/** One reading along the line. A reading the elevation data does not reach carries no elevation. */
export type TerrainProfileSample = components['schemas']['TerrainProfileSampleDto'];

/** One measure (slope, aspect or position) of the area's terrain statistics. */
export type TerrainMeasure = components['schemas']['TerrainMeasureDto'];

/**
 * A cave's descent profile.
 *
 * A caller who may read the cave but not place it exactly is refused with the same "no such cave" a
 * never-created cave gets; asking again does not cure that, so nothing retries and the panel
 * renders nothing on an error.
 */
export function useCaveDepthProfile(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveDepthProfile(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/depth-profile', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/**
 * Where an area's entrances and sinkholes sit on the ground, against the ground itself. Needs the
 * right to read elevation; a refusal is not cured by asking again.
 */
export function useAreaTerrainStatistics(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaTerrainStatistics(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/terrain-statistics', { params: { path: { id: areaId! } } }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/**
 * The ground profile along a line drawn on the map, in longitude and latitude. A mutation rather
 * than a query: every drawn line is a new question and none of them is worth caching.
 */
export function useTerrainProfile() {
  return useMutation({
    mutationFn: (points: { longitude: number; latitude: number }[]) =>
      unwrap(api.POST('/api/v1/terrain/profile', { body: { points } })),
  });
}
