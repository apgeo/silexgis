// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { unwrap } from './shared.ts';

/** The vertical figures of the caves filed under one area, taken together. */
export type AreaSystemFigures = components['schemas']['AreaSystemFiguresDto'];

/**
 * Highest and lowest entrance, system depth and potential depth across the caves under one area.
 * A refusal and an area nobody may read answer identically, so a failed read is not retried.
 */
export function useAreaSystemFigures(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaSystemFigures(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/system-figures', { params: { path: { id: areaId! } } }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}
