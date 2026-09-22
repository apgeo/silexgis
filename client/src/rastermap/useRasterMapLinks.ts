// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQuery } from '@tanstack/react-query';
import { api, ApiError } from '../api/client.ts';
import type { ResLink } from '../api/hooks.ts';
import { allLinksForTarget, RASTER_LINKS_PAGE_SIZE } from './paging.ts';

/**
 * Under the `reslinks` prefix on purpose: every reslink mutation invalidates that prefix
 * and nothing else, so a key outside it would go on serving yesterday's links after a map
 * or pin is created or deleted — the tab strip and the markers would sit stale while the
 * generic links panel, whose reads live under the prefix, refreshed in front of them.
 */
export const rasterMapLinksKey = (surveyModelId: string | undefined) =>
  ['reslinks', 'rastermap', surveyModelId] as const;

/**
 * Every link incident to one survey model, for the map folds to read.
 *
 * One answer feeds the whole tab set: the map-of declarations and every pin of the model
 * arrive in the same incident-link set, because both link shapes carry a surveyModel
 * member. The folds sort them apart; asking twice would be the same read with a second
 * chance to disagree with itself.
 *
 * `enabled` is load-bearing exactly as it is for the station pictures next door: the
 * viewer modal is mounted closed, and a model list page must not pay for links nobody has
 * opened. Anonymous surfaces cannot use this hook — the route takes an account — and the
 * public page's maps will come from its envelope, never from here.
 */
export function useRasterMapLinks(surveyModelId: string | undefined, enabled: boolean) {
  return useQuery<ResLink[]>({
    queryKey: rasterMapLinksKey(surveyModelId),
    queryFn: () =>
      allLinksForTarget(async (page) => {
        const { data, error, response } = await api.GET('/api/v1/reslinks/for-target', {
          params: {
            query: {
              type: 'surveyModel',
              id: surveyModelId!,
              page,
              pageSize: RASTER_LINKS_PAGE_SIZE,
            },
          },
        });
        if (error !== undefined || data === undefined) {
          const problem = error as { code?: string; detail?: string } | undefined;
          throw new ApiError(response.status, problem?.code, problem?.detail);
        }
        return data;
      }),
    enabled: enabled && surveyModelId !== undefined,
  });
}
