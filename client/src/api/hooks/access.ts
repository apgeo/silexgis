// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import type { EntityType } from './types.ts';
import { queryKeys } from './queryKeys.ts';

// ---- capabilities: what the caller may do, per resource domain ----

export type Capabilities = components['schemas']['CapabilitiesDto'];
export type AccessDomainName = components['schemas']['AccessDomain'];
export type AccessActionSet = components['schemas']['AccessAction'];
/** The individual action flags the wire's comma-joined action string is made of. */
export type AccessActionFlag =
  | 'read' | 'write' | 'delete' | 'share' | 'managePermissions'
  | 'viewExactLocation' | 'create' | 'execute';

/** Parses a comma-joined action set ("read, write") into its individual flags. */
export function parseAccessActions(actions: string | null | undefined): Set<AccessActionFlag> {
  return new Set(
    (actions ?? '')
      .split(',')
      .map((x) => x.trim())
      .filter((x): x is AccessActionFlag => x.length > 0 && x !== 'none'),
  );
}

/** True when a comma-joined action set carries the flag. */
export function hasAccessAction(
  actions: string | null | undefined,
  flag: AccessActionFlag,
): boolean {
  return parseAccessActions(actions).has(flag);
}

/**
 * The caller's domain-level rights, for hiding controls the server would refuse anyway.
 * Purely a hint: every actual decision is re-made server-side on the row in question,
 * and per-object answers come from `useEffectiveAccess` instead.
 */
export function useCapabilities() {
  return useQuery({
    queryKey: queryKeys.capabilities,
    queryFn: () => unwrap(api.GET('/api/v1/me/capabilities')),
    staleTime: 5 * 60_000,
  });
}

/** Convenience gate over `useCapabilities`: false while loading, so controls appear, never flash away. */
export function useCan(domain: AccessDomainName, action: AccessActionFlag): boolean {
  const { data } = useCapabilities();
  return hasAccessAction(data?.domains[domain], action);
}

/** One of the caller's own caving groups a new row may be bound to. */
export type CreatableCavingGroup = components['schemas']['CreatableCavingGroupDto'];

/** Where the caller may create in one domain — what a create door and its form need to know. */
export interface CreateDoor {
  /**
   * Whether a create door is worth drawing at all: the right is held over the domain as such,
   * or it reaches through at least one of the caller's caving groups.
   */
  canCreate: boolean;
  /**
   * The right is held with no row in view, so a row that belongs to no caving group is accepted.
   * False for somebody whose right comes only through their groups: what they create has to be
   * bound to one of `cavingGroups`, or the server refuses it.
   */
  unbound: boolean;
  /** The caller's own caving groups a new row may be bound to, ordered by name. */
  cavingGroups: readonly CreatableCavingGroup[];
}

// One array for every "none", so a component that depends on the list does not see a new one
// on each render while the answer is still on its way.
const NO_CAVING_GROUPS: readonly CreatableCavingGroup[] = [];

/**
 * Whether the caller may create in a domain somewhere, and where.
 *
 * `useCan(domain, 'create')` answers a narrower question — is the right held over the domain as
 * such, with no row in view — and a right held only at a caving group's scope is never that: a
 * member whose club lets them record the club's trips is told `create` on none. Gating a create
 * door on it hides a door the server would open. The capabilities answer therefore says, beside
 * the map, in which of the caller's own groups a create is accepted, decided by the rule the
 * write itself is decided by; this reads both and keeps them apart, because the form behind the
 * door needs to know which it is — an unbound create needs no group, a create by club only must
 * name one.
 *
 * Creating only. Whether somebody may write or delete under a right held at a group's scope is a
 * question about a row and stays with the per-object answers. All false and empty while the
 * answer is loading, so a door appears and never flashes away.
 */
export function useCreateDoor(domain: AccessDomainName): CreateDoor {
  const { data } = useCapabilities();
  const unbound = hasAccessAction(data?.domains[domain], 'create');
  const cavingGroups = data?.createInCavingGroups[domain] ?? NO_CAVING_GROUPS;
  return { canCreate: unbound || cavingGroups.length > 0, unbound, cavingGroups };
}

export type EffectiveAccess = components['schemas']['EffectiveAccessDto'];
export type AccessExplanation = components['schemas']['AccessExplanationDto'];

/**
 * What the caller may do to one object — the per-object refinement of `useCapabilities`.
 * With `explain` the server also names the rule that decided each action, redacting
 * anchors the caller may not read.
 */
export function useEffectiveAccess(
  entityType: EntityType,
  entityId: string | undefined,
  options: { explain?: boolean; enabled?: boolean } = {},
) {
  const explain = options.explain ?? false;
  return useQuery({
    queryKey: queryKeys.effectiveAccess(entityType, entityId ?? '', explain),
    queryFn: () =>
      unwrap(api.GET('/api/v1/objects/{entityType}/{id}/effective-access', {
        params: { path: { entityType, id: entityId! }, query: { explain } },
      })),
    enabled: (options.enabled ?? true) && !!entityId,
    // A 403/404 is a settled answer here, not worth retrying.
    retry: false,
  });
}
export type ObjectAccessEntry = components['schemas']['ObjectAccessEntryDto'];
export type ObjectAccessEntryWrite = components['schemas']['ObjectAccessEntryWrite'];

/** The rules written directly onto one object (server-side: ManagePermissions). */
export function useObjectAccess(entityType: EntityType, entityId: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.objectAccess(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/objects/{entityType}/{id}/access', {
        params: { path: { entityType, id: entityId! } },
      })),
    enabled: enabled && !!entityId,
    retry: false,
  });
}

export function useReplaceObjectAccess(entityType: EntityType, entityId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (entries: ObjectAccessEntryWrite[]) =>
      unwrap(api.PUT('/api/v1/objects/{entityType}/{id}/access', {
        params: { path: { entityType, id: entityId } },
        body: { entries },
      })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['object-access'] });
      // The rules just written decide what everyone may do here, this caller included.
      void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
    },
  });
}

// ---- the permission model's own surface: rulesets, trustees, feature sets ----

export type PermissionGroup = components['schemas']['PermissionGroupDto'];
export type PermissionGroupMember = components['schemas']['PermissionGroupMemberDto'];
export type AccessEntry = components['schemas']['AccessEntryDto'];
export type AccessEntryWrite = components['schemas']['AccessEntryWrite'];
export type AccessCatalog = components['schemas']['AccessCatalogDto'];
export type AccessCatalogDomain = components['schemas']['AccessCatalogDomainDto'];
export type AccessCatalogScope = components['schemas']['AccessCatalogScopeDto'];
export type AccessScopeKind = components['schemas']['AccessScopeKind'];
export type AccessEffect = components['schemas']['AccessEffect'];
export type AccessSubjectKind = components['schemas']['AccessSubjectKind'];
export type AccessPreview = components['schemas']['AccessPreviewDto'];
export type AccessPreviewExplanation = components['schemas']['AccessPreviewExplanationDto'];
export type FeatureSetInfo = components['schemas']['FeatureSetDto'];
export type FeatureSetMember = components['schemas']['FeatureSetMemberDto'];

/**
 * The rule editor's vocabulary: which scopes each domain accepts and the actions valid in
 * each, generated server-side from the validator so the editor can never drift from it.
 */
export function useAccessCatalog(enabled = true) {
  return useQuery({
    queryKey: queryKeys.accessCatalog,
    queryFn: () => unwrap(api.GET('/api/v1/permission-groups/catalog')),
    enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function usePermissionGroups(enabled = true) {
  return useQuery({
    queryKey: queryKeys.permissionGroups,
    queryFn: () => unwrap(api.GET('/api/v1/permission-groups')),
    enabled,
    retry: false,
  });
}

/**
 * Everything the model's admin surface writes moves authorization, so every mutation
 * refreshes the caller's own capabilities along with the edited resource.
 */
function useInvalidatePermissionModel() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['permission-groups'] });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
    void queryClient.invalidateQueries({ queryKey: queryKeys.myPermissionGroups });
    void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
  };
}

export function useCreatePermissionGroup() {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.POST('/api/v1/permission-groups', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdatePermissionGroup(id: string) {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.PUT('/api/v1/permission-groups/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeletePermissionGroup() {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/permission-groups/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function usePermissionGroupEntries(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.permissionGroupEntries(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/permission-groups/{id}/entries', { params: { path: { id: id! } } })),
    enabled: !!id,
    retry: false,
  });
}

export function useReplacePermissionGroupEntries(id: string) {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (entries: AccessEntryWrite[]) =>
      unwrap(api.PUT('/api/v1/permission-groups/{id}/entries', {
        params: { path: { id } },
        body: { entries },
      })),
    onSuccess: () => invalidate(),
  });
}

export function usePermissionGroupMembers(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.permissionGroupMembers(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/permission-groups/{id}/members', { params: { path: { id: id! } } })),
    enabled: !!id,
    retry: false,
  });
}

export function useAddPermissionGroupMember(id: string) {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (body: { memberKind: AccessSubjectKind; memberId: string }) =>
      unwrapVoid(api.POST('/api/v1/permission-groups/{id}/members', {
        params: { path: { id } },
        body,
      })),
    onSuccess: () => invalidate(),
  });
}

export function useRemovePermissionGroupMember(id: string) {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: ({ memberKind, memberId }: { memberKind: AccessSubjectKind; memberId: string }) =>
      unwrapVoid(api.DELETE('/api/v1/permission-groups/{id}/members/{memberKind}/{memberId}', {
        params: { path: { id, memberKind, memberId } },
      })),
    onSuccess: () => invalidate(),
  });
}

/**
 * What the model would answer for somebody else — the look an editor takes before saving
 * a deny. A mutation rather than a query: it is asked deliberately, per subject.
 */
export function useAccessPreview() {
  return useMutation({
    mutationFn: (body: { subjectKind: AccessSubjectKind; subjectId: string }) =>
      unwrap(api.POST('/api/v1/permission-groups/preview', { body })),
  });
}

export function useFeatureSets(enabled = true) {
  return useQuery({
    queryKey: queryKeys.featureSets,
    queryFn: () => unwrap(api.GET('/api/v1/feature-sets')),
    enabled,
    retry: false,
  });
}

/** Feature-set edits move access (rules hang on sets), so capabilities refresh too. */
function useInvalidateFeatureSets() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['feature-sets'] });
    // The rules editor lists sets as scope anchors from the catalog.
    void queryClient.invalidateQueries({ queryKey: queryKeys.accessCatalog });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
    void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
  };
}

export function useCreateFeatureSet() {
  const invalidate = useInvalidateFeatureSets();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.POST('/api/v1/feature-sets', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateFeatureSet(id: string) {
  const invalidate = useInvalidateFeatureSets();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.PUT('/api/v1/feature-sets/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteFeatureSet() {
  const invalidate = useInvalidateFeatureSets();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/feature-sets/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** The set's features the caller may read — the count on the set itself can be larger. */
export function useFeatureSetMembers(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.featureSetMembers(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/feature-sets/{id}/members', { params: { path: { id: id! } } })),
    enabled: !!id,
    retry: false,
  });
}

export function useReplaceFeatureSetMembers(id: string) {
  const invalidate = useInvalidateFeatureSets();
  return useMutation({
    mutationFn: (featureIds: string[]) =>
      unwrapVoid(api.PUT('/api/v1/feature-sets/{id}/members', {
        params: { path: { id } },
        body: { featureIds },
      })),
    onSuccess: () => invalidate(),
  });
}

export type UserSummary = components['schemas']['UserSummaryDto'];

export function useUserSearch(q: string) {
  return useQuery({
    queryKey: ['user-search', q] as const,
    queryFn: () => unwrap(api.GET('/api/v1/users/search', { params: { query: { q } } })),
    enabled: q.trim().length >= 2,
    staleTime: 30_000,
  });
}
export type AdminUserPermissionGroup = components['schemas']['AdminUserPermissionGroupDto'];
export type SyncCapabilities = components['schemas']['SyncCapabilitiesDto'];

/**
 * What this installation's sync protocol can do. Read by the settings page for the same reason a
 * phone reads it first: the parts of the protocol a server actually serves are announced by name,
 * so a page can say what a device will and will not be able to do here instead of guessing.
 */
export function useSyncCapabilities() {
  return useQuery({
    queryKey: queryKeys.syncCapabilities,
    queryFn: () => unwrap(api.GET('/api/v1/sync/capabilities')),
    staleTime: 300_000,
  });
}
