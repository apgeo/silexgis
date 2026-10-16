// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { invalidateAfterDelete, unwrap, unwrapVoid, useInvalidateHistory } from './shared.ts';
import type { FeatureChildrenParams, FeatureListParams } from './types.ts';
import { queryKeys } from './queryKeys.ts';

export type FeatureListItem = components['schemas']['FeatureListItemDto'];
export type FeatureDetail = components['schemas']['FeatureDto'];
export type FeatureEnvelope = components['schemas']['FeatureEnvelopeDto'];
export type FeatureCreate = components['schemas']['FeatureCreateRequest'];
export type FeatureUpdate = components['schemas']['FeatureUpdateRequest'];

// Imperative feature calls used by the map edit controller (outside React).
export async function fetchFeature(id: string): Promise<FeatureEnvelope> {
  return unwrap(api.GET('/api/v1/features/{id}', { params: { path: { id } } }));
}

export async function createFeature(body: FeatureCreate): Promise<FeatureDetail> {
  return unwrap(api.POST('/api/v1/features', { body }));
}

export async function updateFeature(id: string, body: FeatureUpdate): Promise<FeatureDetail> {
  return unwrap(api.PUT('/api/v1/features/{id}', { params: { path: { id } }, body }));
}

export function useFeatures(params: FeatureListParams, enabled = true) {
  return useQuery({
    queryKey: queryKeys.features(params),
    queryFn: () => unwrap(api.GET('/api/v1/features', { params: { query: params } })),
    placeholderData: keepPreviousData,
    // Pickers (parent/link target selects) pass enabled=false until they open.
    enabled,
  });
}

/** Resolves any feature id — generic, cave, entrance or centerline — to its typed envelope. */
export function useFeature(id: string | undefined) {
  return useQuery({ ...featureQuery(id ?? ''), enabled: !!id });
}

/**
 * One feature's read as a query object rather than as a hook.
 *
 * A view being told to show something is told outside React's render — it is a message arriving
 * on the workspace bus — so it cannot call a hook and must not reach for the transport either.
 * Handing out the query lets it go through the same cache under the same key, so a view already
 * showing that feature pays nothing to be told about it again.
 */
export function featureQuery(id: string) {
  return {
    queryKey: queryKeys.feature(id),
    queryFn: () => unwrap(api.GET('/api/v1/features/{id}', { params: { path: { id } } })),
  };
}

function useInvalidateFeatures() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['features'] });
}

export function useCreateFeature() {
  const invalidate = useInvalidateFeatures();
  return useMutation({
    mutationFn: (body: FeatureCreate) => createFeature(body),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateFeature() {
  const invalidate = useInvalidateFeatures();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: FeatureUpdate }) => updateFeature(id, body),
    onSuccess: () => {
      invalidate();
      invalidateHistory();
    },
  });
}

/** Soft-deletes the feature and its containment subtree. */
export function useDeleteFeature() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/features/{id}', { params: { path: { id } } })),
    onSuccess: (_, id) => void invalidateAfterDelete(queryClient, ['features'], id),
  });
}

export type DeletedFeature = components['schemas']['DeletedFeatureDto'];

/**
 * The deletions of caves, entrances and surface features this caller may undo, most recent
 * first: one row for each deletion, with a count of what went with it. Which those are is decided
 * on the server row by row — the right that deletes is the right that restores — so an account
 * that may delete nothing is answered with an empty list rather than refused.
 */
export function useDeletedFeatures(page = 1, enabled = true) {
  return useQuery({
    queryKey: queryKeys.deletedFeatures(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/deleted', { params: { query: { page, pageSize: 50 } } })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/**
 * Puts a deleted cave, entrance or surface feature back, with everything deleted along with it.
 *
 * The answer is the feature as its own address now answers it, so it is taken as that reading.
 * Everything else that can show a feature is read again: the lists it returns to and the deleted
 * list it has just left, a cave's entrances, the trips that name it — a trip stops naming a
 * deleted cave and names it again once it is back — and an import's own page, which says which
 * of the objects it created are deleted.
 *
 * A refusal is news about the list the button was on — somebody else put it back, or what
 * contains it was deleted meanwhile — so the deleted list is read again then as well, rather
 * than left offering a row that can only be refused a second time.
 */
export function useRestoreFeature() {
  const queryClient = useQueryClient();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/features/{id}/restore', { params: { path: { id } } })),
    onSuccess: (envelope, id) => {
      queryClient.setQueryData(queryKeys.feature(id), envelope);
      void queryClient.invalidateQueries({
        queryKey: ['features'],
        // The feature itself was just taken from the answer.
        predicate: ({ queryKey }) => !(queryKey[1] === 'detail' && queryKey[2] === id),
      });
      for (const scope of ['caves', 'entrances', 'trip-logs', 'import-batches']) {
        void queryClient.invalidateQueries({ queryKey: [scope] });
      }
      invalidateHistory();
    },
    onError: () => void queryClient.invalidateQueries({ queryKey: ['features', 'deleted'] }),
  });
}

export type FeatureParent = components['schemas']['FeatureParentDto'];
export type ParentEdgeWrite = components['schemas']['ParentEdgeRequest'];
export type FeatureChild = components['schemas']['FeatureChildDto'];

export function useFeatureParents(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.featureParents(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/parents', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

/** Replaces the feature's parent edges (the DAG requires exactly one primary edge). */
export function useSetFeatureParents() {
  const invalidate = useInvalidateFeatures();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, parents }: { id: string; parents: ParentEdgeWrite[] }) =>
      unwrap(api.PUT('/api/v1/features/{id}/parents', { params: { path: { id } }, body: { parents } })),
    onSuccess: () => {
      invalidate();
      invalidateHistory();
    },
  });
}

export function useFeatureChildren(id: string | undefined, params: FeatureChildrenParams = {}) {
  return useQuery({
    queryKey: queryKeys.featureChildren(id ?? '', params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/children', {
        params: { path: { id: id! }, query: params },
      })),
    enabled: !!id,
    placeholderData: keepPreviousData,
  });
}

export type FeatureLink = components['schemas']['FeatureLinkDto'];
export type FeatureLinkWrite = components['schemas']['FeatureLinkWriteRequest'];

/** Links in both directions; protected far endpoints arrive redacted. */
export function useFeatureLinks(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.featureLinks(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/links', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

/** Replaces the feature's outgoing links; incoming ones belong to the other feature. */
export function useSetFeatureLinks() {
  const invalidate = useInvalidateFeatures();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, links }: { id: string; links: FeatureLinkWrite[] }) =>
      unwrap(api.PUT('/api/v1/features/{id}/links', { params: { path: { id } }, body: { links } })),
    onSuccess: () => {
      invalidate();
      invalidateHistory();
    },
  });
}

export type FeatureShare = components['schemas']['FeatureShareDto'];
export type FeatureShareCreate = components['schemas']['FeatureShareCreateRequest'];
export type FeatureShareCreated = components['schemas']['FeatureShareCreatedDto'];
export type SharedFeatureEnvelope = components['schemas']['SharedFeatureEnvelopeDto'];

/** Share-link metadata only — tokens are shown once at mint time and never again. */
export function useFeatureShares(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.featureShares(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/shares', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    // Requires Share permission; a 403 is a settled answer, not worth retrying.
    retry: false,
  });
}

function useInvalidateFeatureShares() {
  const queryClient = useQueryClient();
  return (id: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.featureShares(id) });
}

/** Mints a share link; the response carries the one-time token. */
export function useCreateFeatureShare() {
  const invalidate = useInvalidateFeatureShares();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: FeatureShareCreate }) =>
      unwrap(api.POST('/api/v1/features/{id}/shares', { params: { path: { id } }, body })),
    onSuccess: (_, { id }) => invalidate(id),
  });
}

export function useRevokeFeatureShare() {
  const invalidate = useInvalidateFeatureShares();
  return useMutation({
    mutationFn: ({ id, shareId }: { id: string; shareId: string }) =>
      unwrapVoid(api.DELETE('/api/v1/features/{id}/shares/{shareId}', {
        params: { path: { id, shareId } },
      })),
    onSuccess: (_, { id }) => invalidate(id),
  });
}

/** Imperative fetch for the anonymous shared-feature page (no auth required for public shares). */
export async function fetchSharedFeature(token: string): Promise<SharedFeatureEnvelope> {
  return unwrap(api.GET('/api/v1/shared/features/{token}', { params: { path: { token } } }));
}

export type CaveQrPublication = components['schemas']['CaveQrPublicationDto'];
export type PublicQr = components['schemas']['PublicQrDto'];

/**
 * Whether a cave's printed codes resolve for a visitor with no account, and the record of the
 * decision that last governed it. Not published is an answer rather than an absence, so this
 * always resolves to a document for a caller allowed to ask at all.
 */
export function useCaveQrPublication(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.caveQrPublication(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/qr-publication', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    // Requires the right to share the cave; a 403 is a settled answer, not worth retrying.
    retry: false,
  });
}

function useInvalidateCaveQrPublication() {
  const queryClient = useQueryClient();
  return (id: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.caveQrPublication(id) });
}

/** Lets the cave's printed codes resolve for anyone. Publishing an already-published cave is the state asked for. */
export function usePublishCaveQr() {
  const invalidate = useInvalidateCaveQrPublication();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/caves/{id}/qr-publication', { params: { path: { id } } })),
    onSuccess: (_, id) => invalidate(id),
  });
}

/** Stops them resolving. Withdrawing a cave nobody published is the state asked for, not an error. */
export function useRevokeCaveQr() {
  const invalidate = useInvalidateCaveQrPublication();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/caves/{id}/qr-publication', { params: { path: { id } } })),
    onSuccess: (_, id) => invalidate(id),
  });
}

/**
 * Imperative fetch for the anonymous printed-code landing page. No account is involved: a person
 * holding a phone at a cave entrance has none, which is the entire reason the route exists.
 */
export async function fetchPublicQr(code: string): Promise<PublicQr> {
  return unwrap(api.GET('/api/v1/public/qr/{code}', { params: { path: { code } } }));
}
