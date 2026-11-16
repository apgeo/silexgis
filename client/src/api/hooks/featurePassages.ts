// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

/**
 * The caves with passage beneath, or near, a doline, a sinkhole or any outline — with the rock
 * overhead at the nearest passage and the plan offset. The same answer read the other way round is
 * "which passages run under this polygon".
 */
export type FeaturePassages = components['schemas']['FeaturePassagesDto'];

/** One cave in that answer. */
export type FeaturePassage = components['schemas']['FeaturePassageDto'];

/**
 * Asks which caves lie beneath a feature.
 *
 * A reader who may not place the feature, or who holds no right to read elevation, gets a refusal;
 * the card then does not appear rather than showing an empty list, which would read as "nothing
 * runs under this".
 */
export function useFeaturePassages(id: string | undefined, distanceM: number, enabled = true) {
  return useQuery({
    queryKey: queryKeys.featurePassages(id ?? '', distanceM),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/passages-beneath', {
          params: { path: { id: id! }, query: { DistanceM: distanceM } },
        }),
      ),
    enabled: enabled && !!id,
    staleTime: 60_000,
    retry: false,
  });
}
