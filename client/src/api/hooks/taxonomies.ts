// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

function taxonomyQuery(path: '/api/v1/cave-types' | '/api/v1/entrance-types' | '/api/v1/rock-types', kind: string) {
  return {
    queryKey: queryKeys.taxonomy(kind),
    queryFn: () => unwrap(api.GET(path)),
    staleTime: 5 * 60_000,
  };
}

export function useCaveTypes() {
  return useQuery(taxonomyQuery('/api/v1/cave-types', 'cave-types'));
}

export function useEntranceTypes() {
  return useQuery(taxonomyQuery('/api/v1/entrance-types', 'entrance-types'));
}

export function useRockTypes() {
  return useQuery(taxonomyQuery('/api/v1/rock-types', 'rock-types'));
}

export function useFeatureTypes() {
  return useQuery({
    queryKey: queryKeys.taxonomy('feature-types'),
    queryFn: () => unwrap(api.GET('/api/v1/feature-types')),
    staleTime: 5 * 60_000,
  });
}

type FeatureTypeRow = components['schemas']['FeatureTypeDto'];

/**
 * Whether a generic feature may be given this type. Every type is, except the one bound to the
 * cave kind: every cave carries it and nothing else may, and the server refuses it for anything
 * else. Lists that only describe or filter by type keep it; pickers that give a type to a new or
 * edited generic feature, an import's surface features or a permission rule leave it out.
 */
export function isGenericFeatureType(type: Pick<FeatureTypeRow, 'boundKind'>): boolean {
  return type.boundKind === 'generic';
}

/** The types a generic feature may be given — the same cached list, without the cave type. */
export function useGenericFeatureTypes() {
  return useQuery({
    queryKey: queryKeys.taxonomy('feature-types'),
    queryFn: () => unwrap(api.GET('/api/v1/feature-types')),
    staleTime: 5 * 60_000,
    select: (types: FeatureTypeRow[]) => types.filter(isGenericFeatureType),
  });
}

export type FeatureTypeWrite = components['schemas']['FeatureTypeRequest'];

function useInvalidateFeatureTypes() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('feature-types') });
}

export function useCreateFeatureType() {
  const invalidate = useInvalidateFeatureTypes();
  return useMutation({
    mutationFn: (body: FeatureTypeWrite) => unwrap(api.POST('/api/v1/feature-types', { body })),
    onSuccess: invalidate,
  });
}

/**
 * Saves a feature kind. Rewriting its properties schema moves the schema version server-side, so
 * every feature already stored keeps the version it was checked against and is re-checked the next
 * time someone edits it.
 */
export function useUpdateFeatureType() {
  const invalidate = useInvalidateFeatureTypes();
  return useMutation({
    mutationFn: ({ id, ...body }: FeatureTypeWrite & { id: number }) =>
      unwrap(api.PUT('/api/v1/feature-types/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteFeatureType() {
  const invalidate = useInvalidateFeatureTypes();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/feature-types/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

export type LinkKind = components['schemas']['LinkKindDto'];

/** Feature-link taxonomy (drains-to, connects-with, …). */
export function useLinkKinds() {
  return useQuery({
    queryKey: queryKeys.taxonomy('link-kinds'),
    queryFn: () => unwrap(api.GET('/api/v1/link-kinds')),
    staleTime: 5 * 60_000,
  });
}

/** The document kinds, each with the metadata schema its documents are described by. */
export function useDocumentTypes() {
  return useQuery({
    queryKey: queryKeys.taxonomy('document-types'),
    queryFn: () => unwrap(api.GET('/api/v1/document-types')),
    staleTime: 5 * 60_000,
  });
}

/**
 * The jobs a person may be recorded as having done on a trip: the rows that ship, which this
 * client has its own wording for, plus whatever an installation added, shown as it was written.
 */
export function useTripParticipantRoles() {
  return useQuery({
    queryKey: queryKeys.taxonomy('trip-participant-roles'),
    queryFn: () => unwrap(api.GET('/api/v1/trip-participant-roles')),
    staleTime: 5 * 60_000,
  });
}

/** A participant role as an administrator authors it. */
export interface TripParticipantRoleWrite {
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
}

function useInvalidateTripParticipantRoles() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('trip-participant-roles') });
    // Every roster row renders its job from this list, so a renamed or removed row leaves the
    // trips already in cache showing wording that no longer exists.
    void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
  };
}

export function useCreateTripParticipantRole() {
  const invalidate = useInvalidateTripParticipantRoles();
  return useMutation({
    mutationFn: (body: TripParticipantRoleWrite) =>
      unwrap(api.POST('/api/v1/trip-participant-roles', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateTripParticipantRole() {
  const invalidate = useInvalidateTripParticipantRoles();
  return useMutation({
    mutationFn: ({ id, ...body }: TripParticipantRoleWrite & { id: number }) =>
      unwrap(api.PUT('/api/v1/trip-participant-roles/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTripParticipantRole() {
  const invalidate = useInvalidateTripParticipantRoles();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/trip-participant-roles/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/**
 * The purposes a trip may be recorded under: the rows that ship, which this client has its own
 * wording for, plus whatever an installation added, which is shown as it was written.
 */
export function useTripTypes() {
  return useQuery({
    queryKey: queryKeys.taxonomy('trip-types'),
    queryFn: () => unwrap(api.GET('/api/v1/trip-types')),
    staleTime: 5 * 60_000,
  });
}

/**
 * A trip purpose as an administrator authors it: three schemas, each arriving as the raw text
 * that was typed rather than as a parsed object, because raw text is what is edited and what
 * the server measures reports against.
 */
export interface TripTypeWrite {
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
  fieldDataSchema: string | null;
  logisticsSchema: string | null;
  safetySchema: string | null;
  /** The list trips of this purpose work through, by identity. Null names none. */
  defaultChecklistId: string | null;
}

function useInvalidateTripTypes() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('trip-types') });
    // A purpose's schemas decide what every trip recorded under it may say, so cached trip
    // reads are stale in a way the vocabulary list alone does not express.
    void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
  };
}

export function useCreateTripType() {
  const invalidate = useInvalidateTripTypes();
  return useMutation({
    mutationFn: (body: TripTypeWrite) => unwrap(api.POST('/api/v1/trip-types', { body })),
    onSuccess: invalidate,
  });
}

/**
 * Saves a trip purpose. Rewriting one of its schemas publishes a new version of that schema
 * server-side, so every trip already recorded keeps the version it was checked against and is
 * only re-checked when someone next edits the section it belongs to.
 */
export function useUpdateTripType() {
  const invalidate = useInvalidateTripTypes();
  return useMutation({
    mutationFn: ({ id, ...body }: TripTypeWrite & { id: number }) =>
      unwrap(api.PUT('/api/v1/trip-types/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTripType() {
  const invalidate = useInvalidateTripTypes();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/trip-types/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/** A document kind as it is authored: the schema arrives as raw text, not as a parsed object. */
export interface DocumentTypeWrite {
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
  metadataSchema: string | null;
}

export function useCreateDocumentType() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: DocumentTypeWrite) => unwrap(api.POST('/api/v1/document-types', { body })),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('document-types') }),
  });
}

/**
 * Saves a document kind. Rewriting the schema publishes a new schema version server-side,
 * so every document already stored keeps the version it was checked against and is only
 * re-checked when someone next edits it.
 */
export function useUpdateDocumentType() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: DocumentTypeWrite & { id: number }) =>
      unwrap(api.PUT('/api/v1/document-types/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('document-types') });
      // A schema change alters what every document of the kind may say, so cached
      // document reads are stale in a way the kind list alone does not express.
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });
}
