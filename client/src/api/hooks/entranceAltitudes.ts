// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { unwrap } from './shared.ts';

/** Entrance altitudes set beside the ground height under them, ranked by the widest gap. */
export type AltitudeDisagreement = components['schemas']['AltitudeDisagreementDto'];

/** One entrance's recorded altitude beside the modelled one. Carries no position. */
export type EntranceAltitudeRow = components['schemas']['EntranceAltitudeRowDto'];

export type EntranceAltitudeVerdict = components['schemas']['EntranceAltitudeVerdict'];

/** A cave's depth and height taken across its entrances and its survey. */
export type CaveSystemFigures = components['schemas']['CaveSystemFiguresDto'];

/**
 * One cave's entrance altitudes against the elevation model.
 *
 * A caller who may read the cave but not place it exactly is refused with the same "no such cave" a
 * never-created cave gets, and a caller without the right to read elevation is refused outright.
 * Neither is cured by asking again, so nothing retries and the panel renders nothing on an error.
 */
export function useCaveAltitudeDisagreement(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveAltitudeDisagreement(caveId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caves/{id}/altitude-disagreement', { params: { path: { id: caveId! } } }),
      ),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** The entrances under one area, widest gap first, counting only those the caller may place. */
export function useAreaAltitudeDisagreement(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaAltitudeDisagreement(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/altitude-disagreement', { params: { path: { id: areaId! } } }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** Highest and lowest entrance, total depth, height and potential depth of one cave. */
export function useCaveSystemFigures(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveSystemFigures(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/system-figures', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/**
 * Fills an empty entrance altitude from the elevation model. The server refuses to replace an
 * altitude somebody recorded, so this is safe to offer on any row the report marks fillable.
 */
export function useFillEntranceAltitude() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (entranceId: string) =>
      unwrap(
        api.POST('/api/v1/cave-entrances/{id}/fill-altitude', { params: { path: { id: entranceId } } }),
      ),
    onSuccess: () => {
      // The altitude is part of the cave's record, of its entrance list and of every figure taken
      // from entrance altitudes, so all of those are asked again.
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['entrances'] });
    },
  });
}
