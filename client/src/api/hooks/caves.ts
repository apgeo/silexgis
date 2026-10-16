// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  keepPreviousData,
  useMutation,
  useQueries,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components, paths } from '../schema';
import { invalidateAfterDelete, unwrap, unwrapVoid, useInvalidateHistory } from './shared.ts';
import type { CaveListParams, CaveWrite, EntranceWrite } from './types.ts';
import { queryKeys } from './queryKeys.ts';

/** What an interchange export was asked for, minus the treatments it has not been given yet. */
export type KarstLinkExportRequest =
  paths['/api/v1/export/caves/karstlink']['post']['requestBody']['content']['application/json'];

/** How many caves an interchange export would hold, and how many need a decision. */
export type KarstLinkExportPreview = components['schemas']['KarstLinkExportPreview'];

/**
 * How large the protected-position decision is, before it is made.
 *
 * Asked of the server rather than counted on the client: whether a cave's position is protected
 * is a fact about the cave that this client is never shown, and it is the same question whoever
 * is asking — an owner and an administrator have to decide too, because the file outlives their
 * right to look. `enabled` is what stops it being asked before somebody opens the chooser.
 */
export function useKarstLinkExportPreview(request: KarstLinkExportRequest, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.karstLinkExportPreview(request),
    queryFn: () =>
      unwrap(api.POST('/api/v1/export/caves/karstlink/preview', { body: request })),
    enabled,
  });
}

/** One identifier another register knows a cave by. */
export type CaveExternalId = components['schemas']['CaveExternalIdDto'];

/** What grottocenter.org offered for a cave's name, and whether it was asked at all. */
export type GrottocenterLookup = components['schemas']['GrottocenterLookupDto'];

/** The identifiers other registers know this cave by. */
export function useCaveExternalIds(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveExternalIds(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/external-ids', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
  });
}

/**
 * The places this cave has named: the depths it has declared the meaning of — each one a station,
 * and a name for the place — and then the stations it names without a depth, as they were declared.
 *
 * <b>Why a cave declares this at all.</b> A depth alone has to be turned into a station by looking
 * for the nearest one, and "nearest" cannot tell which of several stations within a metre of each
 * other is the place people actually mean by that number. A club knows. Declaring it once means
 * every report of that depth — typed, corrected or imported — lands where the club says, and gives
 * the place a name somebody can report by instead of a number. A place named without a depth
 * (`depthM` null) is a name for a station and nothing else: no depth is ever placed by it.
 */
export function useCaveDepthPlaces(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveDepthPlaces(caveId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caves/{caveId}/depth-places', { params: { path: { caveId: caveId! } } }),
      ),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
  });
}

/**
 * Declares one named place of this cave: what a depth means, replacing the declaration for that
 * depth — or, with no depth, a name for a station, replacing that station's depthless name.
 *
 * The key is the whole point: two answers to "what is at 96 m" is no answer, and the resolution
 * that consults these has to be able to take one; a station named twice without a depth is one
 * place with two names.
 */
export function useWriteCaveDepthPlace(caveId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      depthM,
      stationName,
      placeLabel,
      note,
      landmark,
    }: {
      depthM: number | null;
      stationName: string;
      placeLabel: string | null;
      note: string | null;
      landmark: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/caves/{caveId}/depth-places', {
          params: { path: { caveId } },
          body: { depthM, stationName, placeLabel, note, landmark },
        }),
      ),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: queryKeys.caveDepthPlaces(caveId) }),
  });
}

/** Withdraws one declaration. Reports already recorded under it keep the station they were given. */
export function useDeleteCaveDepthPlace(caveId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id }: { id: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/caves/{caveId}/depth-places/{id}', {
          params: { path: { caveId, id } },
        }),
      ),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: queryKeys.caveDepthPlaces(caveId) }),
  });
}

/** Records one register's identifier for a cave, or clears it when the value is empty. */
export function useSetCaveExternalId(caveId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ system, value }: { system: string; value: string | null }) =>
      unwrap(
        api.PUT('/api/v1/caves/{caveId}/external-ids/{system}', {
          params: { path: { caveId, system } },
          body: { value },
        }),
      ),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: queryKeys.caveExternalIds(caveId) }),
  });
}

/**
 * Asks grottocenter.org which of its caves match this one's name.
 *
 * A mutation although it reads: it is an errand that leaves the installation, and it happens
 * because somebody pressed a button rather than because a screen was opened. Cached as a query
 * it would be repeated on a remount, sending a cave's name outward again for nobody.
 */
export function useGrottocenterLookup(caveId: string) {
  return useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/v1/caves/{caveId}/external-ids/grottocenter/lookup', {
          params: { path: { caveId } },
        }),
      ),
  });
}

export function useCaves(params: CaveListParams) {
  return useQuery({
    queryKey: queryKeys.caves(params),
    queryFn: () => unwrap(api.GET('/api/v1/caves', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useCave(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cave(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

/**
 * Names for caves known only by id — the ones a stored selection points at that a paged, searched
 * list did not happen to return.
 *
 * Worth its own hook because the alternative is worse than untidy: falling back to "a cave you can
 * no longer read" for anything simply absent from the current page tells a caver their access was
 * revoked when nothing of the kind happened. Asking for each id by name distinguishes the two —
 * what comes back is named, and what genuinely cannot be read stays unnamed. A failure is not
 * retried, because the expected failure here is a definite "you cannot read this".
 */
export function useCaveNames(ids: readonly string[]) {
  return useQueries({
    queries: ids.map((id) => ({
      queryKey: queryKeys.cave(id),
      queryFn: () => unwrap(api.GET('/api/v1/caves/{id}', { params: { path: { id } } })),
      staleTime: 300_000,
      retry: false,
    })),
    combine: (results) =>
      new Map(
        results.flatMap((result) =>
          result.data ? ([[result.data.id, result.data.name]] as [string, string][]) : [],
        ),
      ),
  });
}

export type CaveSummary = components['schemas']['CaveSummaryDto'];

/** Cave header block: related-record counts, main entrance and the caller's capabilities. */
export function useCaveSummary(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveSummary(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/summary', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

export function useEntrances(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.entrances(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/entrances', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
  });
}

function useInvalidateCaves() {
  const queryClient = useQueryClient();
  return (caveId?: string) => {
    void queryClient.invalidateQueries({ queryKey: ['caves'] });
    if (caveId) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.entrances(caveId) });
    }
  };
}

export function useCreateCave() {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: (body: CaveWrite) => unwrap(api.POST('/api/v1/caves', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCave(id: string) {
  const invalidate = useInvalidateCaves();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: (body: CaveWrite) =>
      unwrap(api.PUT('/api/v1/caves/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      invalidate(id);
      invalidateHistory();
    },
  });
}

export function useDeleteCave() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/caves/{id}', { params: { path: { id } } })),
    onSuccess: (_, id) => void invalidateAfterDelete(queryClient, ['caves'], id),
  });
}

/** Non-hook variant for flows where the cave id is only known at submit time. */
export async function createEntranceFor(caveId: string, body: EntranceWrite) {
  return unwrap(api.POST('/api/v1/caves/{caveId}/entrances', { params: { path: { caveId } }, body }));
}

export function useCreateEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: (body: EntranceWrite) =>
      unwrap(api.POST('/api/v1/caves/{caveId}/entrances', { params: { path: { caveId } }, body })),
    onSuccess: () => {
      invalidate(caveId);
      invalidateHistory();
    },
  });
}

export function useUpdateEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: EntranceWrite }) =>
      unwrap(api.PUT('/api/v1/cave-entrances/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      invalidate(caveId);
      invalidateHistory();
    },
  });
}

export function useDeleteEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/cave-entrances/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => {
      invalidate(caveId);
      invalidateHistory();
    },
  });
}
export type CaveDepthPlace = components['schemas']['CaveDepthPlaceDto'];
