// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';

export type Stereonet = components['schemas']['StereonetDto'];
export type StereonetPole = components['schemas']['StereonetPoleDto'];
export type StereonetDensity = components['schemas']['StereonetDensity'];
export type MeasuredStrikeComparison = components['schemas']['MeasuredStrikeComparisonDto'];

/**
 * Keys for the stereonet answers. Under the cave's and the feature's own prefixes, so anything that
 * invalidates a cave or a feature wholesale refreshes its stereonet too; the kind of plane is part
 * of the key because each kind is a different answer.
 */
const stereonetKeys = {
  cave: (id: string, measurementType: string) => ['caves', id, 'stereonet', measurementType] as const,
  area: (id: string, measurementType: string) => ['features', id, 'stereonet', measurementType] as const,
};

/** The poles of the structural readings within reach of a cave, with their density. */
export function useCaveStereonet(caveId: string | undefined, measurementType?: string) {
  return useQuery({
    queryKey: stereonetKeys.cave(caveId ?? '', measurementType ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caves/{id}/stereonet', {
          params: {
            path: { id: caveId! },
            query: measurementType ? { MeasurementType: measurementType } : {},
          },
        }),
      ),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    // A cave this reader may see but not place answers "no such cave"; asking again would not
    // change that.
    retry: false,
  });
}

/** The poles of the structural readings under an area, with their density. */
export function useAreaStereonet(areaId: string | undefined, measurementType?: string, enabled = true) {
  return useQuery({
    queryKey: stereonetKeys.area(areaId ?? '', measurementType ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/stereonet', {
          params: {
            path: { id: areaId! },
            query: measurementType ? { MeasurementType: measurementType } : {},
          },
        }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}
