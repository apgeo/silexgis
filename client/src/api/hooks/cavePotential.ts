// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

export type CavePotentialResultInfo = components['schemas']['CavePotentialResultDto'];
export type CavePotentialRunRequest = components['schemas']['CavePotentialRunRequest'];
export type CavePotentialModelInfo = components['schemas']['CavePotentialModelDto'];
export type CavePotentialModelWrite = components['schemas']['CavePotentialModelWriteRequest'];
export type CavePotentialWeightingRequest = components['schemas']['CavePotentialWeightingRequest'];
export type CavePotentialWeighting = components['schemas']['CavePotentialWeightingDto'];
export type EvidenceLayerInfo = components['schemas']['EvidenceLayerDto'];
export type CavePotentialMethod = components['schemas']['CavePotentialMethod'];
export type CavePotentialDefinition = components['schemas']['CavePotentialModelDefinition'];
export type EvidenceTransform = components['schemas']['EvidenceTransform'];
export type EvidenceTransformKind = components['schemas']['EvidenceTransformKind'];
export type CavePotentialTrainingInfo = components['schemas']['CavePotentialTrainingDto'];
export type CavePotentialWeightTable = components['schemas']['CavePotentialWeightTableDto'];
export type CavePotentialClassWeights = components['schemas']['CavePotentialClassWeightsDto'];
export type CavePotentialRateCurve = components['schemas']['CavePotentialRateCurveDto'];

/** How long a result's map address stays usable, less a margin to re-read it in. */
const CAVE_POTENTIAL_REFRESH_MS = 8 * 60_000;

/** A run that is still being computed, or waiting its turn to be. */
export function cavePotentialUnsettled(status: CavePotentialResultInfo['status']): boolean {
  return status === 'queued' || status === 'computing';
}

/**
 * How often the list of results is re-read: closely while any run is still being computed, and
 * slowly the rest of the time. Never off, because the address of each map carries a signature that
 * expires after ten minutes, and a page left open on the map would otherwise hold a layer whose
 * next range request is refused.
 */
export function cavePotentialPollInterval(
  items: { status: CavePotentialResultInfo['status'] }[] | undefined,
): number {
  return (items ?? []).some((result) => cavePotentialUnsettled(result.status))
    ? 2000
    : CAVE_POTENTIAL_REFRESH_MS;
}

/**
 * The cave-potential results this reader may read, newest first. The server lists a map weighed
 * from recorded positions only for a reader who may place every one of them, so nothing here needs
 * hiding on the client.
 */
export function useCavePotentialResults(enabled = true) {
  return useQuery({
    queryKey: queryKeys.cavePotentialResults,
    queryFn: () => unwrap(api.GET('/api/v1/cave-potential/results')),
    enabled,
    refetchInterval: (query) => cavePotentialPollInterval(query.state.data),
  });
}

/** Asks for a model to be run over an area at a cell size. */
export function useRunCavePotentialModel() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CavePotentialRunRequest) =>
      unwrap(api.POST('/api/v1/cave-potential/results', { body: request })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavePotentialResults });
    },
  });
}

/** Removes a result and the map it left on disk. */
export function useDeleteCavePotentialResult() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/cave-potential/results/{id}', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavePotentialResults });
    },
  });
}

/**
 * The kinds of evidence a model can weigh and how each may be scored. A closed list the server
 * owns, so it is read once and kept for the session.
 */
export function useEvidenceLayers() {
  return useQuery({
    queryKey: queryKeys.cavePotentialEvidenceLayers,
    queryFn: () => unwrap(api.GET('/api/v1/cave-potential/evidence-layers')),
    staleTime: Infinity,
  });
}

/** The cave-potential models this reader may read: their own and those shared with them. */
export function useCavePotentialModels() {
  return useQuery({
    queryKey: queryKeys.cavePotentialModels,
    queryFn: () => unwrap(api.GET('/api/v1/cave-potential/models')),
  });
}

/**
 * The weights a definition comes to, with the consistency of its comparisons, before it is saved.
 *
 * Asked of the server rather than worked out here, so the figure beside the form is the figure a
 * run will use. `request` is null while the form is not yet something that could be weighed: the
 * route refuses an unfinished definition, and a refusal on every keystroke is noise in the
 * reader's console and nothing they can act on.
 */
export function useCavePotentialWeighting(request: CavePotentialWeightingRequest | null) {
  const key = request === null ? '' : JSON.stringify(request);
  return useQuery({
    queryKey: queryKeys.cavePotentialWeighting(key),
    queryFn: () => unwrap(api.POST('/api/v1/cave-potential/models/weighting', { body: request! })),
    enabled: request !== null,
    staleTime: Infinity,
    retry: false,
  });
}

/** Saves a new model; whoever saves it owns it. */
export function useCreateCavePotentialModel() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (write: CavePotentialModelWrite) =>
      unwrap(api.POST('/api/v1/cave-potential/models', { body: write })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavePotentialModels });
    },
  });
}

/** Rewrites, renames or re-shares a model. */
export function useUpdateCavePotentialModel() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, write }: { id: string; write: CavePotentialModelWrite }) =>
      unwrap(api.PUT('/api/v1/cave-potential/models/{id}', { params: { path: { id } }, body: write })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavePotentialModels });
    },
  });
}

/** Takes a private copy of a model the reader may read, under a name of their choosing. */
export function useCloneCavePotentialModel() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) =>
      unwrap(api.POST('/api/v1/cave-potential/models/{id}/clone', { params: { path: { id } }, body: { name } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavePotentialModels });
    },
  });
}

/** Deletes a model. The maps already computed from it keep the definition they were run with. */
export function useDeleteCavePotentialModel() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/cave-potential/models/{id}', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavePotentialModels });
      // A result names the model it came from, and that link is cleared when the model goes.
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavePotentialResults });
    },
  });
}
