// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery } from '@tanstack/react-query';
import { api, ApiError } from '../client.ts';
import { allLinksForTarget, RASTER_LINKS_PAGE_SIZE } from '../../rastermap/paging.ts';
import { MAP_PLACE_POINT_CODE } from '../../rastermap/vocabulary.ts';
import { useInvalidateResLinks } from './resLinks.ts';
import { unwrap } from './shared.ts';
import type { components } from '../schema';
import type { ResLink } from './types.ts';

export type PlacePointCreate = components['schemas']['PlacePointCreateRequest'];
export type PlacePointMove = components['schemas']['PlacePointMoveRequest'];

/**
 * Under the `reslinks` prefix on purpose: every link write invalidates that prefix, so a pin moved
 * or removed anywhere — this screen, the links panel — redraws here too.
 */
export const placePointLinksKey = (documentId: string | undefined) =>
  ['reslinks', 'place-points', documentId] as const;

/**
 * Every place pinned on one picture, across however many pages it takes. A phone's map of a cave
 * can carry far more pins than a page of links, and a fold fed one page would draw a map that looks
 * finished and is not. Pins the reader is not shown are not in the answer at all — not counted
 * either — so what arrives is what may be drawn.
 */
export function usePlacePointLinks(documentId: string | undefined, enabled = true) {
  return useQuery<ResLink[]>({
    queryKey: placePointLinksKey(documentId),
    queryFn: () =>
      allLinksForTarget(async (page) => {
        const { data, error, response } = await api.GET('/api/v1/reslinks/for-target', {
          params: {
            query: {
              type: 'document',
              id: documentId!,
              relation: MAP_PLACE_POINT_CODE,
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
    enabled: enabled && documentId !== undefined,
  });
}

/** Pins a place on a phone's map, at a point of its current picture. */
export function useCreatePlacePoint() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (body: PlacePointCreate) => unwrap(api.POST('/api/v1/reslinks/place-points', { body })),
    onSuccess: invalidate,
  });
}

/** Moves a pinned place to a point of the map's current picture; the phones get it under the same id. */
export function useMovePlacePoint() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: PlacePointMove }) =>
      unwrap(api.PUT('/api/v1/reslinks/{id}/place-point', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}
