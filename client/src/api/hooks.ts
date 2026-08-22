// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clusterCellBbox } from '../geo/cluster.ts';
import { api, ApiError, lastReadETag } from './client.ts';
import type { components } from './schema';

export type CaveListItem = components['schemas']['CaveListItemDto'];
export type CaveDetail = components['schemas']['CaveDto'];
export type CaveWrite = components['schemas']['CaveWriteRequest'];
export type Entrance = components['schemas']['EntranceDto'];
export type EntranceWrite = components['schemas']['EntranceWriteRequest'];
export type MapLayerInfo = components['schemas']['MapLayerDto'];
export type Taxonomy = components['schemas']['TaxonomyDto'];
export type FeatureType = components['schemas']['FeatureTypeDto'];
export type DocumentType = components['schemas']['DocumentTypeDto'];
export type DocumentInfo = components['schemas']['DocumentDto'];
export type DocumentComment = components['schemas']['DocumentCommentDto'];
export type TextExtractionState = components['schemas']['TextExtractionState'];
export type CabinetInfo = components['schemas']['CabinetDto'];
export type CabinetWrite = components['schemas']['CabinetWriteRequest'];
export type CabinetDocument = components['schemas']['CabinetDocumentDto'];
export type UploadBatchInfo = components['schemas']['UploadBatchDto'];
export type UploadBatchItemInfo = components['schemas']['UploadBatchItemDto'];
export type UnfiledDocument = components['schemas']['UnfiledDocumentDto'];
export type PhotoInfo = components['schemas']['PhotoDto'];
export type PhotoCredit = components['schemas']['PhotoCreditDto'];
export type AlbumInfo = components['schemas']['AlbumDto'];
export type PublicPhoto = components['schemas']['PublicPhotoDto'];
export type DeletedPhoto = components['schemas']['DeletedPhotoDto'];
export type Visibility = components['schemas']['Visibility'];
export type FileConfig = components['schemas']['FileConfigDto'];
export type EntranceFeatureCollection = components['schemas']['FeatureCollection'];
export type Me = components['schemas']['MeDto'];
export type MeUpdate = components['schemas']['MeUpdateRequest'];
export type MeLocale = components['schemas']['MeLocaleDto'];
export type MeLocaleWrite = components['schemas']['MeLocaleWriteRequest'];
export type ProfileVisibility = components['schemas']['ProfileVisibilityDto'];
export type FieldVisibility = ProfileVisibility['email'];
export type UserAddress = components['schemas']['UserAddressDto'];
export type UserAddressWrite = components['schemas']['UserAddressWriteRequest'];
export type MemberSummary = components['schemas']['MemberDto'];
export type NotificationPreferences = components['schemas']['NotificationPreferencesDto'];
export type NotificationCategory = components['schemas']['NotificationCategoryDto'];

/**
 * The name of one notification category, as the server publishes it. Named separately from the
 * row that carries it because the settings page and the opt-out landing page both look their
 * wording up by this value alone. Non-null by construction: the generated union admits null only
 * because one response omits the category — a daily summary collects every category and names
 * none — and null is not a category anybody can be notified about.
 */
export type NotificationCategoryName = NonNullable<components['schemas']['NotificationCategory']>;

/** What an opt-out link switched off, as the server reports it back to the landing page. */
export type UnsubscribeResult = components['schemas']['UnsubscribeResultDto'];
export type DataExport = components['schemas']['DataExportDto'];
export type MfaStatus = components['schemas']['MfaStatusDto'];
export type MfaMethod = components['schemas']['MfaMethodDto'];
export type TwoFactorMethod = components['schemas']['TwoFactorMethod'];
export type PhoneStatus = components['schemas']['PhoneStatusDto'];
export type AdminSettings = components['schemas']['AdminSettingsDto'];
export type MailSettingsWrite = components['schemas']['MailSettingsWriteRequest'];
export type SmsSettingsWrite = components['schemas']['SmsSettingsWriteRequest'];
export type SecuritySettings = components['schemas']['SecuritySettingsDto'];
export type ProtectionSettings = components['schemas']['ProtectionSettingsDto'];
export type ImportSettings = components['schemas']['ImportSettingsDto'];
export type MessageTemplate = components['schemas']['MessageTemplateDto'];
export type ResLink = components['schemas']['ResLinkDto'];
export type ResLinkMember = components['schemas']['ResLinkMemberDto'];
export type ResLinkTargetDisplay = components['schemas']['ResLinkTargetDisplayDto'];
export type ResLinkTargetHit = components['schemas']['ResLinkTargetHitDto'];
export type ResLinkRelationType = components['schemas']['ResLinkRelationTypeDto'];
export type ResLinkRelationTypeWrite = components['schemas']['ResLinkRelationTypeWriteRequest'];
export type ResLinkCreate = components['schemas']['ResLinkCreateRequest'];
export type ResLinkUpdate = components['schemas']['ResLinkUpdateRequest'];
export type ResLinkMemberAdd = components['schemas']['ResLinkMemberAddRequest'];
export type ResLinkMemberUpdate = components['schemas']['ResLinkMemberUpdateRequest'];
export type ResLinkPointDefault = components['schemas']['ResLinkPointDefaultDto'];
export type AnchorKind = components['schemas']['AnchorKind'];
export type ResLinkAnchorState = components['schemas']['ResLinkAnchorState'];

// Query keys live here so invalidation stays precise.
export const queryKeys = {
  me: ['me'] as const,
  filterVocabulary: ['filters', 'vocabulary'] as const,
  filterQuery: (body: FilterQueryBody) => ['filters', 'query', body] as const,
  filterResolve: (world: string, ids: readonly string[]) =>
    ['filters', 'resolve', world, [...ids].sort()] as const,
  dashboardSummary: ['dashboard', 'summary'] as const,
  mapLayers: ['map-layers'] as const,
  mapConfig: ['map-config'] as const,
  taxonomy: (kind: string) => ['taxonomy', kind] as const,
  caves: (params: CaveListParams) => ['caves', 'list', params] as const,
  cave: (id: string) => ['caves', 'detail', id] as const,
  caveSummary: (id: string) => ['caves', 'summary', id] as const,
  entrances: (caveId: string) => ['entrances', caveId] as const,
  surveyModels: (caveId: string) => ['survey-models', caveId] as const,
  centerlines: (caveId: string) => ['centerlines', caveId] as const,
  search: (q: string, kind?: string) => ['search', q, kind ?? 'all'] as const,
  nominatim: (q: string) => ['nominatim', q] as const,
  features: (params: FeatureListParams) => ['features', 'list', params] as const,
  feature: (id: string) => ['features', 'detail', id] as const,
  featureParents: (id: string) => ['features', id, 'parents'] as const,
  featureChildren: (id: string, params: FeatureChildrenParams) => ['features', id, 'children', params] as const,
  featureLinks: (id: string) => ['features', id, 'links'] as const,
  featureShares: (id: string) => ['features', id, 'shares'] as const,
  geofiles: (params: GeofileListParams) => ['geofiles', 'list', params] as const,
  geofile: (id: string) => ['geofiles', 'detail', id] as const,
  geofileColumns: (id: string) => ['geofile-columns', id] as const,
  termRuleSets: ['term-rule-sets', 'list'] as const,
  termRuleSet: (id: string) => ['term-rule-sets', 'detail', id] as const,
  effectiveTermRuleSet: ['term-rule-sets', 'effective'] as const,
  importSession: (geofileId: string) => ['import-session', geofileId] as const,
  // The options are part of the key: a preview is a pure function of the file and the
  // choices, so changing a rule set or the duplicate radius is a different question rather
  // than a stale answer to the same one.
  importPreview: (geofileId: string, body: unknown) => ['import-preview', geofileId, body] as const,
  photoImportSession: ['photo-import-session'] as const,
  // Same reasoning as the vector preview: the grouping is a pure function of the pictures and
  // the choices, so changing the clustering radius is a different question rather than a stale
  // answer to the same one.
  photoImportPreview: (body: unknown) => ['photo-import-preview', body] as const,
  photoImportTracks: ['photo-import-tracks'] as const,
  importBatches: (params: ImportBatchListParams) => ['import-batches', 'list', params] as const,
  importBatch: (id: string) => ['import-batches', 'detail', id] as const,
  importProvenance: (featureId: string) => ['import-provenance', featureId] as const,
  attachments: (entityType: string, entityId: string) => ['attachments', entityType, entityId] as const,
  file: (id: string) => ['files', 'detail', id] as const,
  fileVersions: (fileId: string) => ['file-versions', fileId] as const,
  fileConfig: ['file-config'] as const,
  document: (id: string) => ['documents', 'detail', id] as const,
  // Deliberately its own root rather than nested under 'documents': saving a title
  // invalidates that whole prefix, and a remark thread has no reason to be refetched
  // because somebody corrected a spelling in the metadata panel above it.
  documentComments: (id: string) => ['document-comments', id] as const,
  cabinets: ['cabinets'] as const,
  unfiledDocuments: (params: UnfiledDocumentParams) => ['documents', 'unfiled', params] as const,
  uploadBatches: (page: number, pageSize: number) => ['upload-batches', 'list', page, pageSize] as const,
  uploadBatch: (id: string) => ['upload-batches', 'detail', id] as const,
  uploadBatchItems: (id: string, params: UploadBatchItemParams) =>
    ['upload-batches', 'items', id, params] as const,
  importRoots: ['upload-batches', 'import-roots'] as const,
  photos: (params: PhotoQueryParams) => ['photos', 'list', params] as const,
  photo: (id: string) => ['photos', 'detail', id] as const,
  photoDuplicates: ['photos', 'duplicates'] as const,
  deletedPhotos: (page: number) => ['photos', 'deleted', page] as const,
  albums: (params: AlbumListParams) => ['albums', 'list', params] as const,
  album: (id: string) => ['albums', 'detail', id] as const,
  publicPhotos: (page: number) => ['public-photos', page] as const,
  sharedAlbum: (token: string) => ['public-albums', token] as const,
  cabinetDocuments: (id: string, params: CabinetDocumentParams) =>
    ['cabinets', id, 'documents', params] as const,
  rasterMaps: (params: RasterMapListParams) => ['raster-maps', 'list', params] as const,
  tripLogs: (params: TripLogListParams) => ['trip-logs', 'list', params] as const,
  tripLog: (id: string) => ['trip-logs', 'detail', id] as const,
  tripInvitations: (id: string) => ['trip-logs', 'invitations', id] as const,
  tripReportTemplates: ['trip-report-templates'] as const,
  taggings: (entityType: string, entityId: string) => ['taggings', entityType, entityId] as const,
  tags: (search: string) => ['tags', search] as const,
  cavingGroups: ['cavingGroups'] as const,
  cavers: ['cavers'] as const,
  cavingGroupMembers: (cavingGroupId: string) => ['teams', cavingGroupId, 'members'] as const,
  tripStatistics: (subject: string, id: string) => ['stats', subject, id] as const,
  objectAccess: (entityType: string, entityId: string) => ['object-access', entityType, entityId] as const,
  history: (entityType: string, entityId: string) => ['history', entityType, entityId] as const,
  mfa: ['mfa'] as const,
  avatarPresets: ['avatar-presets'] as const,
  members: (params: MemberListParams) => ['members', 'list', params] as const,
  member: (id: string) => ['members', 'detail', id] as const,
  notificationPrefs: ['me', 'notifications'] as const,
  uiPreferences: ['me', 'preferences'] as const,
  uiDefaults: ['ui-defaults'] as const,
  dataExport: ['me', 'data-export'] as const,
  phone: ['me', 'phone'] as const,
  adminSettings: ['admin', 'settings'] as const,
  messageTemplates: ['admin', 'message-templates'] as const,
  capabilities: ['me', 'capabilities'] as const,
  myPermissionGroups: ['me', 'permission-groups'] as const,
  effectiveAccess: (entityType: string, entityId: string, explain: boolean) =>
    ['effective-access', entityType, entityId, explain] as const,
  accessCatalog: ['access-catalog'] as const,
  permissionGroups: ['permission-groups'] as const,
  permissionGroupEntries: (id: string) => ['permission-groups', id, 'entries'] as const,
  permissionGroupMembers: (id: string) => ['permission-groups', id, 'members'] as const,
  featureSets: ['feature-sets'] as const,
  featureSetMembers: (id: string) => ['feature-sets', id, 'members'] as const,
  resLinksForTarget: (targetType: string, targetId: string, params: ResLinkPageParams) =>
    ['reslinks', 'for-target', targetType, targetId, params] as const,
  resLink: (idOrCode: string) => ['reslinks', 'detail', idOrCode] as const,
  resLinkTargets: (targetType: string, q: string) => ['reslinks', 'targets', targetType, q] as const,
  resLinkRelationTypes: ['reslinks', 'relation-types'] as const,
  resLinkPointDefault: ['reslinks', 'point-default'] as const,
  expeditions: (params: ExpeditionListParams) => ['expeditions', 'list', params] as const,
  expedition: (id: string) => ['expeditions', 'detail', id] as const,
  expeditionRoster: (id: string) => ['expeditions', 'roster', id] as const,
  expeditionMap: (id: string) => ['expeditions', 'map', id] as const,
  expeditionLeads: (id: string) => ['expeditions', 'leads', id] as const,
};

async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await call;
  if (error !== undefined || data === undefined) {
    const problem = error as { code?: string; detail?: string } | undefined;
    throw new ApiError(response.status, problem?.code, problem?.detail);
  }
  return data;
}

/** unwrap for endpoints with no response body (DELETE / 204). */
async function unwrapVoid(
  call: Promise<{ error?: unknown; response: Response }>,
): Promise<void> {
  const { error, response } = await call;
  if (error !== undefined) {
    const problem = error as { code?: string; detail?: string } | undefined;
    throw new ApiError(response.status, problem?.code, problem?.detail);
  }
}

export function useMe() {
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: () => unwrap(api.GET('/api/v1/me')),
    // The avatar URL carries a short-lived delivery token, so a long-cached response would
    // start 403ing in the header. Same cadence the attachment gallery refreshes on.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

/** Everything the settings pages write invalidates the one profile response they all read. */
function useInvalidateMe() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['me'] });
}

export function useUpdateProfile() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: MeUpdate) => unwrap(api.PUT('/api/v1/me', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateLocale() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: MeLocaleWrite) => unwrap(api.PUT('/api/v1/me/locale', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useAvatarPresets() {
  return useQuery({
    queryKey: queryKeys.avatarPresets,
    queryFn: () => unwrap(api.GET('/api/v1/avatar-presets')),
    staleTime: Infinity, // ships with the build
  });
}

export function useUploadAvatar() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/me/avatar', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useSetAvatarPreset() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (preset: string) => unwrap(api.PUT('/api/v1/me/avatar', { body: { preset } })),
    onSuccess: () => invalidate(),
  });
}

export function useRemoveAvatar() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: () => unwrapVoid(api.DELETE('/api/v1/me/avatar')),
    onSuccess: () => invalidate(),
  });
}

export function useCreateAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: UserAddressWrite) => unwrap(api.POST('/api/v1/me/addresses', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UserAddressWrite }) =>
      unwrap(api.PUT('/api/v1/me/addresses/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/me/addresses/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function useRequestEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (newEmail: string) => unwrapVoid(api.POST('/api/v1/me/email/change', { body: { newEmail } })),
    onSuccess: () => invalidate(),
  });
}

export function useConfirmEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (token: string) => unwrap(api.POST('/api/v1/me/email/confirm', { body: { token } })),
    onSuccess: () => invalidate(),
  });
}

export function useResendEmailChange() {
  return useMutation({ mutationFn: () => unwrapVoid(api.POST('/api/v1/me/email/resend')) });
}

export function useCancelEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: () => unwrapVoid(api.DELETE('/api/v1/me/email/pending')),
    onSuccess: () => invalidate(),
  });
}

export function useVerifyEmail() {
  return useMutation({ mutationFn: () => unwrapVoid(api.POST('/api/v1/me/email/verify')) });
}

export function useChangeUsername() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (username: string) => unwrap(api.PUT('/api/v1/me/username', { body: { username } })),
    onSuccess: () => invalidate(),
  });
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) =>
      unwrapVoid(api.PUT('/api/v1/me/password', { body })),
  });
}

export function useNotificationPreferences() {
  return useQuery({
    queryKey: queryKeys.notificationPrefs,
    queryFn: () => unwrap(api.GET('/api/v1/me/notifications')),
  });
}

export function useUpdateNotificationPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      emailEnabled: boolean;
      digest: NotificationPreferences['digest'];
      categories: { category: NotificationCategory['category']; enabled: boolean }[];
    }) => unwrap(api.PUT('/api/v1/me/notifications', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
}

/**
 * The installation's starting arrangement. A default only: the client fills gaps with it and never
 * lets it overwrite something the person chose.
 */
export function useUiDefaults() {
  return useQuery({
    queryKey: queryKeys.uiDefaults,
    queryFn: () => unwrap(api.GET('/api/v1/ui-defaults')),
    // It changes when an administrator publishes a new one, which is rare; asking again on every
    // mount would be a request per page load for an answer that is the same all day.
    staleTime: 10 * 60_000,
  });
}

export function useUiPreferences() {
  return useQuery({
    queryKey: queryKeys.uiPreferences,
    queryFn: () => unwrap(api.GET('/api/v1/me/preferences')),
  });
}

export function useUpdateUiPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (preferences: Record<string, unknown>) =>
      unwrap(api.PUT('/api/v1/me/preferences', { body: { preferences } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
}

/**
 * The caller's newest account-data export. Polls only while one is being built, the same
 * settle pattern the geodata pages use; a missing export is a normal empty state, not an error.
 */
export function useDataExport() {
  return useQuery({
    queryKey: queryKeys.dataExport,
    queryFn: async (): Promise<DataExport | null> => {
      const { data, error, response } = await api.GET('/api/v1/me/data-export');
      if (response.status === 404) {
        return null;
      }
      if (error !== undefined || data === undefined) {
        const problem = error as { code?: string; detail?: string } | undefined;
        throw new ApiError(response.status, problem?.code, problem?.detail);
      }
      return data;
    },
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'queued' || status === 'running' ? 2000 : false;
    },
  });
}

export function useRequestDataExport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/me/data-export')),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me', 'data-export'] }),
  });
}

export interface MemberListParams {
  search?: string;
  page?: number;
  pageSize?: number;
}

export function useMembers(params: MemberListParams) {
  return useQuery({
    queryKey: queryKeys.members(params),
    queryFn: () => unwrap(api.GET('/api/v1/members', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useMember(id: string) {
  return useQuery({
    queryKey: queryKeys.member(id),
    queryFn: () => unwrap(api.GET('/api/v1/members/{id}', { params: { path: { id } } })),
    enabled: id.length > 0,
  });
}

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

export type MyPermissionGroup = components['schemas']['MyPermissionGroupDto'];

/** The permission groups the caller reaches — where a right of theirs comes from. */
export function useMyPermissionGroups() {
  return useQuery({
    queryKey: queryKeys.myPermissionGroups,
    queryFn: () => unwrap(api.GET('/api/v1/me/permission-groups')),
    staleTime: 5 * 60_000,
  });
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

export function useMapLayers() {
  return useQuery({
    queryKey: queryKeys.mapLayers,
    queryFn: () => unwrap(api.GET('/api/v1/map-layers')),
    staleTime: 5 * 60_000,
  });
}

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

export type LinkKind = components['schemas']['LinkKindDto'];

/** Feature-link taxonomy (drains-to, connects-with, …). */
export function useLinkKinds() {
  return useQuery({
    queryKey: queryKeys.taxonomy('link-kinds'),
    queryFn: () => unwrap(api.GET('/api/v1/link-kinds')),
    staleTime: 5 * 60_000,
  });
}

export interface CaveListParams {
  page?: number;
  pageSize?: number;
  sort?: string;
  caveTypeId?: number;
  region?: string;
  search?: string;
  minLength?: number;
  bbox?: string;
  tag?: string;
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

export type SurveyModelInfo = components['schemas']['SurveyModelDto'];
export type AuditEntry = components['schemas']['AuditEntryDto'];

export function useSurveyModels(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surveyModels(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/survey-models', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
    // The signed model URLs live 10 minutes; refresh before they lapse mid-view.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

function useInvalidateSurveyModels() {
  const queryClient = useQueryClient();
  return (caveId: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModels(caveId) });
}

export function useUploadSurveyModel() {
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: async ({ caveId, file }: { caveId: string; file: File }): Promise<SurveyModelInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/caves/{caveId}/survey-models', {
        params: { path: { caveId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export function useDeleteSurveyModel() {
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: async ({ id }: { id: string; caveId: string }) => {
      const { error, response } = await api.DELETE('/api/v1/survey-models/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type CenterlineInfo = components['schemas']['CenterlineDto'];

export function useCenterlines(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.centerlines(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/centerlines', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
  });
}

function useInvalidateCenterlines() {
  const queryClient = useQueryClient();
  return (caveId: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.centerlines(caveId) });
}

export function useUploadCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: async ({ caveId, file }: { caveId: string; file: File }): Promise<CenterlineInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/caves/{caveId}/centerlines', {
        params: { path: { caveId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type CenterlineUpdate = components['schemas']['CenterlineUpdateRequest'];

/** Centerline metadata update, including promoting one to the cave's default shape. */
export function useUpdateCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; caveId: string; body: CenterlineUpdate }) =>
      unwrap(api.PUT('/api/v1/centerlines/{id}', { params: { path: { id } }, body })),
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export function useDeleteCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: async ({ id }: { id: string; caveId: string }) => {
      const { error, response } = await api.DELETE('/api/v1/centerlines/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type CenterlineFeatureCollection =
  components['schemas']['CenterlineFeatureCollection'];

/** Installation-wide map rendering limits, published by the server. */
export type MapConfig = components['schemas']['MapConfigDto'];

export function useMapConfig() {
  return useQuery({
    queryKey: queryKeys.mapConfig,
    queryFn: () => unwrap(api.GET('/api/v1/map/config')),
    staleTime: 5 * 60_000,
  });
}

/**
 * Imperative fetch used by the map centerline loaders (not a hook). The zoom decides
 * whether the server sends the splay-free skeleton or clipped full detail; `detailZoom` and
 * `maxPaths` carry the viewer's own overrides, which the server bounds.
 *
 * `z` opts into surveyed altitudes. It costs a larger payload and is answered per row — the
 * stored display skeleton is a flat shape by construction, so a row served from it comes back
 * without them and the response counts how many did. The flat map leaves it off.
 */
export async function fetchCenterlineFeatures(
  bbox: string,
  zoom: number,
  detailZoom?: number,
  maxPaths?: number,
  z?: boolean,
): Promise<CenterlineFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/cave-centerlines', {
    params: { query: { bbox, zoom, detailZoom, maxPaths, z } },
  }));
}

/** Imperative fetch used by the OpenLayers photo overlay loader (not a hook). */
export async function fetchPhotoFeatures(bbox: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/photos', { params: { query: { bbox } } }));
}

export type SearchResult = components['schemas']['SearchResultDto'];
export type SearchFeatureItem = components['schemas']['SearchFeatureItemDto'];
export type SearchTripItem = components['schemas']['SearchTripItemDto'];
export type SearchDocumentItem = components['schemas']['SearchDocumentItemDto'];

/**
 * Global search: features of every kind, trip logs, and documents matched by what their text
 * says. Results carry no coordinates — navigate to the entity (or fetch it) instead of
 * centering the map from here.
 *
 * Pass `kind` when only one kind can be picked. The server's hit budget is shared across
 * kinds, so filtering the answer here instead would let commoner kinds crowd the wanted
 * one out of the response entirely.
 *
 * The document section is the one that is paged, and this hook asks only for its first page:
 * a search box shows the best few answers, and the total that comes back with them says
 * honestly how many more there are. The two query parameters that would change that answer —
 * which page, and whether replaced revisions are searched — are deliberately not passed here,
 * because they are not in the cache key and a request that varied them would be served the
 * previous one's rows.
 */
export function useSearch(q: string, kind?: FeatureKind) {
  return useQuery({
    queryKey: queryKeys.search(q, kind),
    queryFn: () => unwrap(api.GET('/api/v1/search', { params: { query: { q, kind } } })),
    enabled: q.trim().length >= 2,
    staleTime: 30_000,
  });
}

export interface NominatimPlace {
  place_id: number;
  display_name: string;
  lon: string;
  lat: string;
}

export function useNominatim(q: string) {
  return useQuery({
    queryKey: queryKeys.nominatim(q),
    queryFn: async (): Promise<NominatimPlace[]> => {
      const url = `https://nominatim.openstreetmap.org/search?format=jsonv2&limit=5&q=${encodeURIComponent(q)}`;
      const response = await fetch(url, { headers: { Accept: 'application/json' } });
      return response.ok ? ((await response.json()) as NominatimPlace[]) : [];
    },
    enabled: q.trim().length >= 3,
    staleTime: 60_000,
    retry: false,
  });
}

/** Imperative fetch used by the OpenLayers entrance-layer loader (not a hook). */
export async function fetchEntranceFeatures(bbox: string, zoom: number, tag?: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/cave-entrances', { params: { query: { bbox, zoom, tag } } }));
}

/**
 * Members of a low-zoom entrance cluster: its cluster cell (+margin) fetched at
 * a zoom above the server's clustering threshold, so points come back.
 */
export function useClusterEntrances(lon: number, lat: number, zoom: number, tag?: string) {
  const bbox = clusterCellBbox(lon, lat, zoom);
  return useQuery({
    queryKey: ['map', 'cluster-entrances', bbox, tag ?? null] as const,
    queryFn: () => fetchEntranceFeatures(bbox, 14, tag),
    staleTime: 30_000,
  });
}

export type FeatureKind = components['schemas']['FeatureKind'];
export type FeatureCategory = components['schemas']['FeatureCategory'];

/** Optional filters the map feature layer applies on top of the viewport bbox. */
export interface MapFeatureFilters {
  /** Comma-separated FeatureKind names, e.g. "generic,cave". */
  kinds?: string;
  featureTypeId?: number;
  category?: FeatureCategory;
  tag?: string;
}

/**
 * Imperative fetch used by the OpenLayers feature-layer loader (not a hook). Protected
 * non-point geometry arrives with a null geometry — the layer must tolerate it.
 */
export async function fetchMapFeatures(
  bbox: string,
  filters?: MapFeatureFilters,
): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/features', { params: { query: { bbox, ...filters } } }));
}

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

export interface FeatureListParams {
  page?: number;
  pageSize?: number;
  kind?: FeatureKind;
  featureTypeId?: number;
  category?: FeatureCategory;
  bbox?: string;
  tag?: string;
  search?: string;
  /** A named set of objects — what a multi-selection asks about. Bounded by the server. */
  ids?: string[];
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
  return useQuery({
    queryKey: queryKeys.feature(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/features/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
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
  const invalidate = useInvalidateFeatures();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/features/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
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

export interface FeatureChildrenParams {
  page?: number;
  pageSize?: number;
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

export type GeofileInfo = components['schemas']['GeofileDto'];
export type GeofileUpdate = components['schemas']['GeofileUpdateRequest'];

export interface GeofileListParams {
  page?: number;
  pageSize?: number;
  search?: string;
}

export function useGeofiles(params: GeofileListParams, pollWhileImporting = false) {
  return useQuery({
    queryKey: queryKeys.geofiles(params),
    queryFn: () => unwrap(api.GET('/api/v1/geofiles', { params: { query: params } })),
    placeholderData: keepPreviousData,
    // Imports run in a background job — keep the table live until they settle.
    refetchInterval: pollWhileImporting
      ? (query) =>
          query.state.data?.items.some(
            (g) => g.importStatus === 'uploaded' || g.importStatus === 'importing',
          )
            ? 2000
            : false
      : false,
  });
}

function useInvalidateGeofiles() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['geofiles'] });
}

export function useUploadGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: async (file: File): Promise<GeofileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      // Multipart: hand the FormData through untouched (the browser sets the boundary).
      return unwrap(api.POST('/api/v1/geofiles', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useUpdateGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: GeofileUpdate }) =>
      unwrap(api.PUT('/api/v1/geofiles/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/geofiles/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

/** Imperative fetch used by the OpenLayers geofile-layer loader (not a hook). */
export async function fetchGeofileFeatureCollection(id: string, bbox: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/geofiles/{id}/features', { params: { path: { id }, query: { bbox } } }));
}

export type FileInfo = components['schemas']['FileDto'];
export type AttachmentInfo = components['schemas']['AttachmentDto'];

/**
 * entityType vocabulary shared by attachments, taggings and ACLs. Any feature — generic,
 * cave, entrance or centerline — is addressed as 'feature' with its feature id. The wire
 * type is a plain string; this union is the documented set of accepted values.
 */
export type EntityType =
  | 'feature'
  | 'tripLog'
  | 'geofile'
  | 'georeferencedMap'
  | 'mapView'
  | 'expedition';
// Stored files additionally carry taggings (never attachments or grants) — the tag
// endpoints accept the extra target; the server rejects it everywhere else.
export type AttachedEntityType = EntityType | 'storedFile';
export type AttachmentRole = AttachmentInfo['role'];

/**
 * @param enabled false keeps the request from being made at all — what a collapsed panel section
 * passes, so selecting an object on the map costs one small request instead of six.
 */
export function useAttachments(
  entityType: AttachedEntityType,
  entityId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.attachments(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/attachments', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: enabled && !!entityId,
    // Delivery URLs embed 10-minute tokens; refresh the list before they lapse.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

function useInvalidateAttachments() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['attachments'] });
    // Attachments are audited children of their target entity, so any attach/detach/metadata
    // change surfaces in that entity's timeline — refresh it here so every caller stays in sync.
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

export type HistoryEvent = components['schemas']['HistoryEventDto'];

/** Change history for an entity (incl. its children's events via audit roots). */
/** @param enabled false keeps the request from being made — see {@link useAttachments}. */
export function useHistory(entityType: string, entityId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.history(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/history', {
        params: { query: { entityType, entityId: entityId!, pageSize: 100 } },
      })),
    enabled: enabled && !!entityId,
  });
}

export function useInvalidateHistory() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['history'] });
}

/**
 * One stored file, with delivery URLs minted for this caller.
 *
 * The document endpoint says what a document *is*; only this one says how to fetch its
 * bytes, and how far this caller may reach for them — the original, or renderings only.
 * Those URLs carry a ten-minute token, so the answer is refreshed well before it lapses,
 * exactly as the attachment lists do; a long read must never end in a dead link.
 */
export function useFile(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.file(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/files/{id}', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

/**
 * Fetches a file again once the words in it have been read.
 *
 * How many pages a document has is discovered by the same pass that reads its text, and it is
 * the file — not the document — that carries the number. Only the document is watched while
 * that work is in flight; the file above is cached for minutes. Without this, someone who has
 * just uploaded a report is shown its first page and no way to reach the rest, until a refetch
 * happens to come round or they reload by hand.
 *
 * The refetch is tied to the moment the reading finishes rather than to the text being read,
 * so opening a document whose words were read long ago still costs one request, not two.
 */
export function useRefreshFileWhenTextRead(
  fileId: string | undefined,
  textExtraction: string | undefined,
) {
  const queryClient = useQueryClient();
  const wasReading = useRef(false);
  useEffect(() => {
    if (textExtraction === 'pending') {
      wasReading.current = true;
      return;
    }
    if (wasReading.current && fileId !== undefined) {
      wasReading.current = false;
      void queryClient.invalidateQueries({ queryKey: queryKeys.file(fileId) });
    }
  }, [fileId, textExtraction, queryClient]);
}

/**
 * How much of a text document is read into the page before the rest is left to a download.
 *
 * A log of a season's surveying can be tens of megabytes of plain text, and putting all of it
 * into a document that has to lay out cannot end well on a phone. The cut is stated to the
 * reader rather than made silently, because a file that appears to stop halfway through is
 * indistinguishable from a file that was truncated when it was written.
 */
export const maxInlineTextBytes = 512 * 1024;

/**
 * The text of a file, for the formats that are text.
 *
 * Fetched from the same short-lived delivery URL everything else uses, so a caller who may not
 * have the stored bytes does not get them here either — which for a text file is every caller
 * who can read the document, since a text file records no position to protect.
 *
 * Only the part that will be shown is asked for. The delivery route answers partial requests,
 * so the cut is made before the bytes cross the network rather than after: pulling a season's
 * survey log down over a phone connection in order to display its first half-megabyte would
 * spend the whole file to show a fragment of it. A server that ignored the request and sent
 * everything is still handled, because the cut is applied here as well.
 */
export function useFileText(file: FileInfo | undefined) {
  return useQuery({
    queryKey: ['file-text', file?.id ?? '', file?.contentUrl ?? ''] as const,
    queryFn: async () => {
      const response = await fetch(file!.contentUrl, {
        headers: { Range: `bytes=0-${maxInlineTextBytes - 1}` },
      });
      if (!response.ok) {
        throw new ApiError(response.status, 'file.not_found');
      }

      const bytes = (await response.arrayBuffer()).slice(0, maxInlineTextBytes);
      // A cut made in bytes can land in the middle of a character — Romanian text is full of
      // two-byte ones — so the tail is decoded as if more were coming, which drops an
      // incomplete character instead of showing it as a replacement mark.
      const text = new TextDecoder('utf-8').decode(bytes, { stream: true });
      return {
        // What was cut is a fact about the file, not about what came back: a partial answer is
        // the same length as a whole one when the file is exactly that long.
        text,
        truncated: file!.sizeBytes > maxInlineTextBytes,
      };
    },
    enabled: !!file && file.mayDownloadOriginal,
    // The bytes of one version never change, so this is refetched only because the URL that
    // reaches them expires; the key carries that URL, so a renewed one is a new request.
    staleTime: 5 * 60_000,
  });
}

/**
 * The words of one page as the server read them — the text a durable selection is measured
 * against.
 *
 * It is asked for one page at a time and only when something needs it, because that is the
 * shape of the question: a reader who selects a sentence on page nine is asking about page
 * nine, and pulling a two-hundred-page report's text to answer it would spend the document to
 * place one phrase. The delivery URL carries the token, so this is the same permission that
 * opens the page picture beside it, exercised the same way.
 *
 * A page nothing has read yet answers 404, which is a real answer and not an error to retry:
 * the reading happens in a background job, and until it has run there is nothing to match a
 * quote against.
 */
export function usePageText(pagesUrl: string | null | undefined, page: number | null) {
  return useQuery({
    queryKey: ['page-text', pagesUrl ?? '', page ?? 0] as const,
    queryFn: async () => {
      const [path, query] = pagesUrl!.split('?');
      const url = `${path.replace(/\/content$/, `/pages/${page}/text`)}?${query ?? ''}`;
      const response = await fetch(url);
      if (!response.ok) {
        throw new ApiError(response.status, 'file.page_text_not_read');
      }
      return (await response.json()) as { page: number; text: string };
    },
    enabled: !!pagesUrl && page !== null && page >= 1,
    retry: false,
    // One version's pages never change; the key carries the delivery URL, so a renewed token
    // is a new request rather than a stale answer.
    staleTime: 5 * 60_000,
  });
}

/**
 * Upload limits this installation applies. Served rather than compiled in, so a client
 * build cannot disagree with its server and let someone watch a large file transfer only
 * to be refused at the end.
 */
export function useFileConfig() {
  return useQuery({
    queryKey: queryKeys.fileConfig,
    queryFn: () => unwrap(api.GET('/api/v1/files/config')),
    staleTime: 60 * 60_000,
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

/** A document: the identity behind a file, with the title and typed metadata that outlive its versions. */
export function useDocument(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.document(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/documents/{id}', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    // Reading a document's text runs in a background job, so "being read" is a state that
    // resolves on its own while the panel is open. Poll only through that one state: every
    // other one is settled, and re-asking would be asking the same question forever.
    refetchInterval: (query) => (query.state.data?.textExtraction === 'pending' ? 2000 : false),
  });
}

/**
 * Updates a document's title, kind, typed metadata, read audience and club binding. A
 * null `metadata` leaves what is stored untouched — that is how a title is corrected on a
 * document whose kind has tightened its schema since the document was written.
 *
 * Visibility and the caving group decide who may read the document when no rule names it,
 * so saving them moves access: the cached lists that were filtered by that answer are
 * dropped along with the document itself.
 *
 * `language` shares the metadata carve-out: null leaves the stored code alone, because it is
 * detected from the document's own text and a title correction is not a statement about it.
 * An empty string is how it is cleared. Changing it re-indexes every page of the document, so
 * cached searches are dropped too — they were answered by the previous stemmer.
 */
export function useUpdateDocument() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: {
      id: string;
      title: string;
      documentTypeId: number | null;
      metadata: Record<string, unknown> | null;
      visibility: Visibility;
      cavingGroupId: string | null;
      language: string | null;
    }) => unwrap(api.PUT('/api/v1/documents/{id}', { params: { path: { id } }, body })),
    onSuccess: (_result, variables) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.document(variables.id) });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
      void queryClient.invalidateQueries({ queryKey: ['search'] });
      invalidateAttachments();
    },
  });
}

/**
 * Comments are audited children of the document they sit on, so writing one changes that
 * document's timeline as well as the thread.
 */
function useInvalidateDocumentComments() {
  const queryClient = useQueryClient();
  return (documentId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.documentComments(documentId) });
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

/**
 * The remarks written on a document, oldest first. Reading them is reading the document:
 * the server answers a caller who may not read the document exactly as it answers one
 * asking about a document that is not there, so this hook's error carries no information
 * the document query did not already give.
 */
export function useDocumentComments(documentId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.documentComments(documentId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/documents/{documentId}/comments', {
        params: { path: { documentId: documentId! }, query: { pageSize: 200 } },
      })),
    enabled: !!documentId && enabled,
  });
}

/**
 * Posts a remark, or a reply to one. `parentId` names the remark being replied to; threads
 * are one level deep and the server refuses a reply to a reply rather than flattening it.
 */
export function useCreateDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, ...body }: { documentId: string; parentId: string | null; body: string }) =>
      unwrap(api.POST('/api/v1/documents/{documentId}/comments', {
        params: { path: { documentId } },
        body: { ...body, anchorKind: 'whole', anchor: null, anchorFileId: null },
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** Rewrites a remark. Only its author may, and the change is recorded in the document's history. */
export function useUpdateDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, id, ...body }: { documentId: string; id: string; body: string }) =>
      unwrap(api.PUT('/api/v1/documents/{documentId}/comments/{id}', {
        params: { path: { documentId, id } },
        body,
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** Removes a remark, and the replies under it: its author, or an administrator. */
export function useDeleteDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, id }: { documentId: string; id: string }) =>
      unwrapVoid(api.DELETE('/api/v1/documents/{documentId}/comments/{id}', {
        params: { path: { documentId, id } },
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** The whole filing tree: small by construction, so one fetch draws the sider and every breadcrumb. */
export function useCabinets(enabled = true) {
  return useQuery({
    queryKey: queryKeys.cabinets,
    queryFn: () => unwrap(api.GET('/api/v1/cabinets')),
    enabled,
    retry: false,
  });
}

/**
 * Cabinet edits move access — rules are scoped to cabinets and filing decides which of
 * them reach a document — so capabilities and effective-access answers refresh with the
 * tree, exactly as they do for feature sets.
 */
function useInvalidateCabinets() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
    // The rules editor lists cabinets as scope anchors from the catalog.
    void queryClient.invalidateQueries({ queryKey: queryKeys.accessCatalog });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
    void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
  };
}

export function useCreateCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: (body: CabinetWrite) => unwrap(api.POST('/api/v1/cabinets', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: ({ id, ...body }: CabinetWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/cabinets/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/cabinets/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export interface CabinetDocumentParams {
  includeSubtree?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * What is on a shelf for this caller. The cabinet's own `documentCount` is decided by the
 * same read rule as this listing and counts the same documents, so a shelf's label and its
 * contents agree. The one case where they are answering different questions is
 * `includeSubtree`: the count is always the documents filed directly on the cabinet, while
 * that switch asks the listing for everything below it as well.
 */
export function useCabinetDocuments(id: string | undefined, params: CabinetDocumentParams = {}) {
  return useQuery({
    queryKey: queryKeys.cabinetDocuments(id ?? '', params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/cabinets/{id}/documents', {
        params: { path: { id: id! }, query: params },
      })),
    enabled: !!id,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Files a document into a cabinet, or takes it out. Both directions move access — a
 * document leaving a cabinet a deny names becomes readable again — so both invalidate the
 * same caches a rule edit would.
 */
export function useFileDocument() {
  const invalidate = useInvalidateCabinets();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ cabinetId, documentId, filed }: {
      cabinetId: string;
      documentId: string;
      filed: boolean;
    }) => {
      const params = { path: { id: cabinetId, documentId } };
      return filed
        ? unwrapVoid(api.PUT('/api/v1/cabinets/{id}/documents/{documentId}', { params }))
        : unwrapVoid(api.DELETE('/api/v1/cabinets/{id}/documents/{documentId}', { params }));
    },
    onSuccess: (_result, variables) => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: queryKeys.document(variables.documentId) });
    },
  });
}

/**
 * Uploads one file.
 *
 * `allowDuplicate` is how a caller answers the one refusal a person can answer: content the
 * store already holds and this caller may read is refused with `file.duplicate` until somebody
 * says to store it anyway. It defaults to no, and the default is the point — a caller that
 * simply never asked would otherwise quietly make second copies of everything.
 */
export function useUploadFile() {
  return useMutation({
    mutationFn: async (
      { file, allowDuplicate = false }: { file: File; allowDuplicate?: boolean },
    ): Promise<FileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/files', {
        params: { query: { allowDuplicate } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
  });
}

/** Updates user-set file metadata (document date). The gallery's file DTO carries it, so refresh attachments. */
export function useUpdateFile() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, documentDate }: { id: string; documentDate: string | null }) =>
      unwrap(api.PUT('/api/v1/files/{id}', { params: { path: { id } }, body: { documentDate } })),
    onSuccess: () => {
      invalidateAttachments();
      void queryClient.invalidateQueries({ queryKey: ['file-versions'] });
    },
  });
}

export function useCreateAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: (body: {
      fileId: string;
      entityType: AttachedEntityType;
      entityId: string;
      role: AttachmentRole;
      caption: string | null;
      sortOrder: number;
    }) => unwrap(api.POST('/api/v1/attachments', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: ({ id, ...body }: {
      id: string;
      role: AttachmentRole;
      caption: string | null;
      sortOrder: number;
    }) => unwrap(api.PUT('/api/v1/attachments/{id}', { params: { path: { id } }, body })),
    // Caption/role changes are audited on the target entity; useInvalidateAttachments refreshes
    // both the gallery and that timeline.
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/attachments/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type FileVersionInfo = components['schemas']['FileVersionDto'];

/** Full version chain of a file (editor-only; the server 403s read-only callers). */
export function useFileVersions(fileId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.fileVersions(fileId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/files/{id}/versions', { params: { path: { id: fileId! } } })),
    enabled: !!fileId && enabled,
    staleTime: 5 * 60_000,
  });
}

export function useUploadFileVersion() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ fileId, file }: { fileId: string; file: File }): Promise<FileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/files/{id}/versions', {
        params: { path: { id: fileId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    // The attachment now points at the new head; both the gallery and any version list refresh.
    onSuccess: () => {
      invalidateAttachments();
      void queryClient.invalidateQueries({ queryKey: ['file-versions'] });
    },
  });
}

export function useDeleteFileVersion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/files/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['file-versions'] }),
  });
}

export type RasterMapInfo = components['schemas']['GeoreferencedMapDto'];
export type RasterMapUpdate = components['schemas']['GeoreferencedMapUpdateRequest'];

export interface RasterMapListParams {
  page?: number;
  pageSize?: number;
  caveFeatureId?: string;
}

export function useRasterMaps(params: RasterMapListParams, pollWhileProcessing = false) {
  return useQuery({
    queryKey: queryKeys.rasterMaps(params),
    queryFn: () => unwrap(api.GET('/api/v1/georeferenced-maps', { params: { query: params } })),
    placeholderData: keepPreviousData,
    // COG normalization runs in a background job; the signed CogUrl also has a
    // 10-minute lifetime, so refresh periodically either way.
    refetchInterval: pollWhileProcessing
      ? (query) =>
          query.state.data?.items.some(
            (m) => m.status === 'uploaded' || m.status === 'processing',
          )
            ? 2000
            : 8 * 60_000
      : 8 * 60_000,
  });
}

function useInvalidateRasterMaps() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['raster-maps'] });
}

export function useUploadRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: async (file: File): Promise<RasterMapInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/georeferenced-maps', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useUpdateRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: RasterMapUpdate }) =>
      unwrap(api.PUT('/api/v1/georeferenced-maps/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/georeferenced-maps/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type TripLogInfo = components['schemas']['TripLogDto'];
export type TripLogWrite = components['schemas']['TripLogWriteRequest'];
export type TripType = components['schemas']['TripTypeDto'];
export type TripParticipant = components['schemas']['TripParticipantDto'];
export type TripParticipantRole = components['schemas']['TripParticipantRoleDto'];
export type TripParticipantWrite = components['schemas']['TripParticipantWrite'];
export type ActivityState = components['schemas']['ActivityState'];
export type TagInfo = components['schemas']['TagDto'];
export type TaggingInfo = components['schemas']['TaggingDto'];

export interface TripLogListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  caveId?: string;
  search?: string;
  /**
   * The camp's own trip list. It is filtered like every other listing, so the same camp lists
   * different trips to different people; a camp the caller may not read answers as though it
   * gathered nothing rather than refusing, so an id cannot be probed for existence here.
   */
  expeditionId?: string;
}

export function useTripLogs(params: TripLogListParams) {
  return useQuery({
    queryKey: queryKeys.tripLogs(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useTripLog(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.tripLog(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

function useInvalidateTripLogs() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
}

/**
 * The same invalidation, but handed back so a caller can wait for the re-read it starts.
 *
 * A write on the trip is checked against the version the caller last *read*, and only a read
 * records a version. So the moment a write succeeds, the version this caller holds is one behind
 * the one their own write produced, and a second write sent before the re-read lands is refused
 * as a conflict — with two saves on the same card a second apart, which is ordinary use, not a
 * race anybody would think to look for. Refusing it is right: the caller really is writing
 * against a version that has moved. What is wrong is answering "saved" while that is still true.
 *
 * So a write on the trip is not finished until the trip has been read back. The cost is that the
 * confirmation waits for the read, which takes as long as it takes; the alternative is a second
 * save that fails for a reason nobody can act on. This would be unnecessary if a write handed
 * back the version it produced, and it is only needed on the trip's own writes — a write on
 * something beside the trip carries no precondition on it.
 */
function useReadTripLogsBack() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
}

export function useCreateTripLog() {
  const invalidate = useInvalidateTripLogs();
  return useMutation({
    mutationFn: (body: TripLogWrite) => unwrap(api.POST('/api/v1/trip-logs', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateTripLog() {
  const readBack = useReadTripLogsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TripLogWrite }) =>
      unwrap(api.PUT('/api/v1/trip-logs/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      invalidateHistory();
      // Handed back rather than started and forgotten: the next write on this trip is checked
      // against the version this one produced, and only the read records it.
      return readBack();
    },
  });
}

export function useDeleteTripLog() {
  const invalidate = useInvalidateTripLogs();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/trip-logs/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export type TripReportTemplate = components['schemas']['TripReportTemplateDto'];

/** The layouts a write-up may be built in. Every account may read them: choosing one is not editing one. */
export function useTripReportTemplates(enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripReportTemplates,
    queryFn: () => unwrap(api.GET('/api/v1/trip-report-templates')),
    enabled,
  });
}

export type TripReportTemplateWrite = components['schemas']['TripReportTemplateRequest'];

function useInvalidateTripReportTemplates() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: queryKeys.tripReportTemplates });
}

/**
 * Stores a layout. A body whose lines cannot be read is refused by the server, which says which
 * line is at fault — the message is passed through rather than replaced, because the person
 * editing the layout is the only one who can act on it.
 */
export function useCreateTripReportTemplate() {
  const invalidate = useInvalidateTripReportTemplates();
  return useMutation({
    mutationFn: (body: TripReportTemplateWrite) =>
      unwrap(api.POST('/api/v1/trip-report-templates', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateTripReportTemplate() {
  const invalidate = useInvalidateTripReportTemplates();
  return useMutation({
    mutationFn: ({ id, ...body }: TripReportTemplateWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/trip-report-templates/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTripReportTemplate() {
  const invalidate = useInvalidateTripReportTemplates();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/trip-report-templates/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/**
 * Writes the trip up and files the document in the trip's report slot.
 *
 * The document is built server-side, so nothing here hands it any of the trip's content — an
 * argument carrying what to write would be a second place the question of who may see what is
 * answered. What is filed is not this caller's own copy: a file attached to a trip is reachable
 * by everybody who may read that trip, so the server builds the filed one from the reading any
 * account has. The fuller copy is the download.
 */
export function useKeepTripReport() {
  const invalidateAttachments = useInvalidateAttachments();
  return useMutation({
    mutationFn: ({ id, templateId }: { id: string; templateId?: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{id}/report', {
          params: { path: { id }, query: templateId ? { templateId } : {} },
        }),
      ),
    onSuccess: () => invalidateAttachments(),
  });
}

/**
 * Moving a trip to another lifecycle state, through the one route that names the state it moves
 * to rather than a verb per move.
 *
 * It is a write on the trip and is checked against the version the user was looking at, so it
 * carries the precondition the detail read captured. Which moves are legal from which state is
 * the server's to decide — the control only offers the ones a reader would expect, and a request
 * the rules refuse comes back as a conflict rather than being prevented here.
 */
export function useMoveTripLog() {
  const readBack = useReadTripLogsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, state }: { id: string; state: ActivityState }) => {
      const etag = lastReadETag(`/api/v1/trip-logs/${id}`);
      return unwrap(
        api.POST('/api/v1/trip-logs/{id}/state', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body: { state },
        }),
      );
    },
    onSuccess: () => {
      invalidateHistory();
      // As on the trip's own update: a move is checked against the version last read, so the
      // move is not finished until the version it produced has been read.
      return readBack();
    },
  });
}

export type TripInvitationInfo = components['schemas']['TripInvitationDto'];
export type TripInvitationList = components['schemas']['TripInvitationListDto'];
export type TripInvitationAnswer = components['schemas']['TripInvitationResponse'];

/**
 * Everybody on a trip's list and what each has said, in the order the server put them in.
 *
 * Three things on this answer are the server's conclusions and not raw rows: the place each
 * person holds in the order people answered in, whether they hold one of the trip's places or
 * are waiting for one, and whether this caller may write an answer for that person. Each has a
 * rule behind it that the client has no way to evaluate — the ordering skips anybody who has not
 * said yes, a hand-picked person keeps their place even past the limit, and answering for
 * somebody else depends on rights over the trip. Rendering them as they arrive is the whole
 * point; re-deriving any of them here would be a second copy of a rule free to drift from the
 * one that is enforced.
 *
 * The list is deliberately unpaged: a place in an order computed over some of the rows would not
 * be a place in the order at all.
 */
export function useTripInvitations(tripLogId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripInvitations(tripLogId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/invitations', {
          params: { path: { tripLogId: tripLogId! } },
        }),
      ),
    enabled: !!tripLogId && enabled,
  });
}

function useInvalidateTripInvitations() {
  const queryClient = useQueryClient();
  const invalidateHistory = useInvalidateHistory();
  return (tripLogId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.tripInvitations(tripLogId) });
    // A row here is an audit child of the trip, so writing one moves the trip's own timeline.
    invalidateHistory();
  };
}

/**
 * Puts somebody on the trip's list. The person is named by their entry in the club's directory
 * and never by a bare name — a list of people to be told about a trip that could hold text
 * nobody can resolve would be a list nobody can act on — so an id the directory does not know
 * is refused rather than created.
 */
export function useInviteToTrip() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({ tripLogId, caverId }: { tripLogId: string; caverId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/invitations', {
          params: { path: { tripLogId } },
          body: { caverId },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/**
 * Records what one person says about coming.
 *
 * The note travels with the answer and is replaced by it: an answer given without words is an
 * answer without words, not an answer still wearing the previous one's. So a cleared note is
 * sent as an explicit absence rather than omitted.
 */
export function useAnswerTripInvitation() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({
      tripLogId,
      caverId,
      response,
      note,
    }: {
      tripLogId: string;
      caverId: string;
      response: TripInvitationAnswer;
      note: string | null;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}/response', {
          params: { path: { tripLogId, caverId } },
          body: { response, note },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/** Picks one person out for the trip, or puts them back in the order. The order itself is unchanged. */
export function useSelectForTrip() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({
      tripLogId,
      caverId,
      selected,
    }: {
      tripLogId: string;
      caverId: string;
      selected: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}/selection', {
          params: { path: { tripLogId, caverId } },
          body: { selected },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/**
 * Takes somebody off the list entirely, answer and all. For a person put on it by mistake —
 * recording a "no" in their name instead would be writing down words they never said.
 */
export function useRemoveTripInvitation() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({ tripLogId, caverId }: { tripLogId: string; caverId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}', {
          params: { path: { tripLogId, caverId } },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

export type TripPromotion = components['schemas']['TripPromotionDto'];

/**
 * Writes everybody holding a place on the trip into the trip's own list of people.
 *
 * A deliberate act and not a consequence of the trip having happened: who turned up is not who
 * said they would, and a roster nobody wrote is one an audit trail cannot account for.
 *
 * It moves the trip itself — its people change and its version with them — so the whole trip
 * prefix is invalidated rather than the list alone, and the re-read is waited for. Leaving the
 * trip as it was read would let a later save carry the roster somebody saw before this ran, pass
 * its precondition, and quietly undo every row written here; not waiting would leave the same
 * window open for as long as the re-read takes, with a confirmation already on screen.
 */
export function usePromoteTripInvitations() {
  const readBack = useReadTripLogsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ tripLogId }: { tripLogId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/invitations/promote', {
          params: { path: { tripLogId } },
        }),
      ),
    onSuccess: () => {
      invalidateHistory();
      // Waited for, as on the trip's own writes: this stamps the trip row, so a save sent
      // between the confirmation and the re-read would be refused against the version it moved.
      return readBack();
    },
  });
}

export function useTags(search: string) {
  return useQuery({
    queryKey: queryKeys.tags(search),
    queryFn: () => unwrap(api.GET('/api/v1/tags', { params: { query: { search: search || undefined } } })),
    staleTime: 60_000,
  });
}

/** @param enabled false keeps the request from being made — see {@link useAttachments}. */
export function useTaggings(
  entityType: AttachedEntityType,
  entityId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.taggings(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/taggings', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: enabled && !!entityId,
  });
}

function useInvalidateTaggings() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['taggings'] });
    void queryClient.invalidateQueries({ queryKey: ['tags'] });
    // Taggings are audited children of the tagged entity — refresh its timeline too.
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

export function useCreateTagging() {
  const invalidate = useInvalidateTaggings();
  return useMutation({
    mutationFn: (body: { tagName: string; entityType: AttachedEntityType; entityId: string }) =>
      unwrap(api.POST('/api/v1/taggings', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteTagging() {
  const invalidate = useInvalidateTaggings();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error, response } = await api.DELETE('/api/v1/taggings/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type CavingGroupInfo = components['schemas']['CavingGroupDto'];
export type CavingGroupMemberInfo = components['schemas']['CavingGroupMemberDto'];
export type CaverInfo = components['schemas']['CaverDto'];
export type ObjectAccessEntry = components['schemas']['ObjectAccessEntryDto'];
export type ObjectAccessEntryWrite = components['schemas']['ObjectAccessEntryWrite'];

export function useCavingGroups() {
  return useQuery({
    queryKey: queryKeys.cavingGroups,
    queryFn: () => unwrap(api.GET('/api/v1/caving-groups')),
    staleTime: 60_000,
  });
}

export function useCavingGroupMembers(cavingGroupId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cavingGroupMembers(cavingGroupId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caving-groups/{id}/members', { params: { path: { id: cavingGroupId! } } })),
    enabled: !!cavingGroupId,
  });
}

function useInvalidateCavingGroups() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['cavingGroups'] });
}

export function useCreateCavingGroup() {
  const invalidate = useInvalidateCavingGroups();
  return useMutation({
    mutationFn: (body: { name: string; type: CavingGroupInfo['type']; description: string | null; website: string | null }) =>
      unwrap(api.POST('/api/v1/caving-groups', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpsertCavingGroupMember(cavingGroupId: string) {
  const invalidate = useInvalidateCavingGroups();
  return useMutation({
    mutationFn: (body: { caverId: string; role: CavingGroupMemberInfo['role'] }) =>
      unwrap(api.POST('/api/v1/caving-groups/{id}/members', { params: { path: { id: cavingGroupId } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useRemoveCavingGroupMember(cavingGroupId: string) {
  const invalidate = useInvalidateCavingGroups();
  return useMutation({
    mutationFn: async (caverId: string) => {
      const { error, response } = await api.DELETE('/api/v1/caving-groups/{id}/members/{caverId}', {
        params: { path: { id: cavingGroupId, caverId } },
      });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

/** The roster of people, optionally filtered by name; `unlinked` narrows to those with no account. */
export function useCavers(search?: string, unlinked?: boolean) {
  return useQuery({
    queryKey: [...queryKeys.cavers, search ?? '', unlinked ?? false],
    queryFn: () => unwrap(api.GET('/api/v1/cavers', { params: { query: { search, unlinked } } })),
    staleTime: 30_000,
  });
}

export function useCaver(id: string | undefined) {
  return useQuery({
    queryKey: [...queryKeys.cavers, id],
    queryFn: () => unwrap(api.GET('/api/v1/cavers/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

function useInvalidateCavers() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.cavers });
    // A roster edit can change how a person is named on trips and group pages too.
    void queryClient.invalidateQueries({ queryKey: ['cavingGroups'] });
  };
}

export function useCreateCaver() {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (body: { fullName: string; email: string | null; phone: string | null; notes: string | null }) =>
      unwrap(api.POST('/api/v1/cavers', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCaver(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (body: { fullName: string; email: string | null; phone: string | null; notes: string | null }) =>
      unwrap(api.PUT('/api/v1/cavers/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteCaver() {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/cavers/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export function useMergeCavers(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (sourceCaverId: string) =>
      unwrap(api.POST('/api/v1/cavers/{id}/merge', { params: { path: { id } }, body: { sourceCaverId } })),
    onSuccess: () => invalidate(),
  });
}

export function useLinkCaverAccount(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (userId: string) =>
      unwrap(api.POST('/api/v1/cavers/{id}/account-link', { params: { path: { id } }, body: { userId } })),
    onSuccess: () => invalidate(),
  });
}

export function useUnlinkCaverAccount(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: async () => {
      const { error, response } = await api.DELETE('/api/v1/cavers/{id}/account-link', {
        params: { path: { id } },
      });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

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

export type DashboardSummary = components['schemas']['DashboardSummaryDto'];
export type DashboardActivityItem = components['schemas']['DashboardActivityItemDto'];
export type DashboardActivityKind = components['schemas']['DashboardActivityKind'];

export function useDashboardSummary() {
  return useQuery({
    queryKey: queryKeys.dashboardSummary,
    queryFn: () => unwrap(api.GET('/api/v1/dashboard/summary')),
    // Counts and the activity feed move as other people edit; a short window keeps the
    // page from refetching on every visit without going stale over a working session.
    staleTime: 60_000,
  });
}

export type MapViewInfo = components['schemas']['MapViewDto'];
export type MapViewWrite = components['schemas']['MapViewWriteRequest'];

export function useMapViews() {
  return useQuery({
    queryKey: ['map-views'] as const,
    queryFn: () => unwrap(api.GET('/api/v1/map-views')),
  });
}

function useInvalidateMapViews() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['map-views'] });
}

export function useCreateMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: (body: MapViewWrite) => unwrap(api.POST('/api/v1/map-views', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/map-views/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export function useShareMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/map-views/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** Imperative fetch for the anonymous shared-view page (no auth attached needed). */
export async function fetchSharedView(token: string) {
  return unwrap(api.GET('/api/v1/shared/views/{token}', { params: { path: { token } } }));
}

export function useMfaStatus() {
  return useQuery({
    queryKey: queryKeys.mfa,
    queryFn: () => unwrap(api.GET('/api/v1/me/mfa')),
  });
}

export function usePhoneStatus() {
  return useQuery({
    queryKey: queryKeys.phone,
    queryFn: () => unwrap(api.GET('/api/v1/me/phone')),
  });
}

/** Admin-only; the query is left disabled for anyone else so no 403 is provoked. */
export function useAdminSettings(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.adminSettings,
    queryFn: () => unwrap(api.GET('/api/v1/admin/settings')),
    enabled,
  });
}

export function useMessageTemplates(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.messageTemplates,
    queryFn: () => unwrap(api.GET('/api/v1/admin/message-templates')),
    enabled,
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
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/caves/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
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

// ---------------------------------------------------------------------------
// Resource links
// ---------------------------------------------------------------------------

export interface ResLinkPageParams {
  page?: number;
  pageSize?: number;
  /**
   * A relation code, narrowing the panel to links of that one relation — how a page asks
   * for a single role rather than reading every link and sorting them itself. Omitted or
   * blank means every relation, untyped links included; a code no relation type carries is
   * refused by the server rather than answered with an empty page.
   */
  relation?: string;
}

/**
 * Links incident to one entity — the per-entity panel query. `totalItems` is the badge
 * count and is legitimately 0 with an empty page: a link reachable only through a member
 * whose location is protected from this caller is withheld whole, so two callers can
 * honestly see different counts for the same entity. Never reconcile it against another
 * source.
 */
export function useResLinksForTarget(
  targetType: string,
  targetId: string,
  params: ResLinkPageParams = {},
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.resLinksForTarget(targetType, targetId, params),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/reslinks/for-target', {
          params: { query: { type: targetType, id: targetId, ...params } },
        }),
      ),
    enabled: enabled && Boolean(targetId),
    // Deliberately no placeholder from the previous key: this query is keyed by the entity,
    // and a section that showed the last entity's links under this one's heading would not
    // just be stale — the rows elide the entity whose page they are on, so the previous
    // entity's own chip would appear as a sibling of one it is not linked to.
  });
}

/** One link by id or by its 8-character short code — the endpoint tells them apart by shape. */
export function useResLink(idOrCode: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLink(idOrCode),
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/{idOrCode}', { params: { path: { idOrCode } } })),
    enabled: enabled && Boolean(idOrCode),
    retry: false,
  });
}

/** The picker feed. A blank query is answered with an empty list without touching the database. */
export function useResLinkTargets(targetType: string, q: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkTargets(targetType, q),
    queryFn: () =>
      unwrap(api.GET('/api/v1/reslinks/targets/search', { params: { query: { type: targetType, q } } })),
    enabled: enabled && q.trim().length > 0,
  });
}

/**
 * The audience a new GPS point would take for this caller if the form names none. The rule
 * reads the caller's caving-group roster, which nothing else publishes, so the audience is
 * asked for rather than guessed: a guess about who can see a cave position is worse than
 * no notice at all.
 */
export function useResLinkPointDefault(enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkPointDefault,
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/point-default')),
    enabled,
    staleTime: 5 * 60_000,
  });
}

export function useResLinkRelationTypes(enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkRelationTypes,
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/relation-types')),
    enabled,
    staleTime: 5 * 60_000,
  });
}

/**
 * Every link read hangs off one prefix, and a write to one link can change what any
 * entity's panel shows, so writes invalidate the lot. Write responses are deliberately
 * not seeded into the cache: the server answers an author with the membership they just
 * asserted, uncut, while the next ordinary read may withhold part of it — priming from
 * the echo would show the author a link that does not exist for them on refresh.
 */
export function useInvalidateResLinks() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['reslinks'] });
}

export function useCreateResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (body: ResLinkCreate) => unwrap(api.POST('/api/v1/reslinks', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ResLinkUpdate }) =>
      unwrap(api.PATCH('/api/v1/reslinks/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/reslinks/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

export function useAddResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ResLinkMemberAdd }) =>
      unwrap(api.POST('/api/v1/reslinks/{id}/members', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, memberId, body }: { id: string; memberId: string; body: ResLinkMemberUpdate }) =>
      unwrap(
        api.PATCH('/api/v1/reslinks/{id}/members/{memberId}', {
          params: { path: { id, memberId } },
          body,
        }),
      ),
    onSuccess: invalidate,
  });
}

export function useDeleteResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, memberId }: { id: string; memberId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/reslinks/{id}/members/{memberId}', { params: { path: { id, memberId } } }),
      ),
    onSuccess: invalidate,
  });
}

/**
 * Adds a relation to the installation's vocabulary. The whole link prefix is invalidated
 * rather than only the vocabulary list, because every link row carries its relation inline
 * and would otherwise keep rendering the wording that was current when it was fetched.
 */
export function useCreateResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (body: ResLinkRelationTypeWrite) =>
      unwrap(api.POST('/api/v1/reslinks/relation-types', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: ResLinkRelationTypeWrite }) =>
      unwrap(api.PATCH('/api/v1/reslinks/relation-types/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/reslinks/relation-types/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

// ---------------------------------------------------------------------------
// Staged vector import: the club's term rules, the review of a loaded file, and
// the confirmations it produced.
// ---------------------------------------------------------------------------

export type TermRuleSetInfo = components['schemas']['TermRuleSetDto'];
export type TermRuleSetDetail = components['schemas']['TermRuleSetDetailDto'];
export type TermRule = components['schemas']['TermRule'];
export type TermRuleDocument = components['schemas']['TermRuleDocument'];
export type TermRuleSetCreate = components['schemas']['TermRuleSetCreateRequest'];
export type TermRuleSetWrite = components['schemas']['TermRuleSetWriteRequest'];
export type TermRuleSetScopeWrite = components['schemas']['TermRuleSetScopeRequest'];
export type TermRuleSetImport = components['schemas']['TermRuleSetImportRequest'];
export type TermRuleScope = components['schemas']['TermRuleScope'];
export type TermMatchMode = components['schemas']['TermMatchMode'];
export type TermStripMode = components['schemas']['TermStripMode'];
export type ImportTargetKind = components['schemas']['ImportTargetKind'];
export type ImportOptions = components['schemas']['ImportOptions'];
export type ImportDecision = components['schemas']['ImportDecision'];
export type ImportCandidate = components['schemas']['ImportCandidateDto'];
export type ImportPreview = components['schemas']['ImportPreviewDto'];
export type ImportPreviewRequest = components['schemas']['ImportPreviewRequest'];
export type ImportSession = components['schemas']['ImportSessionDto'];
export type ImportCommitResult = components['schemas']['ImportCommitResultDto'];
export type ImportBatch = components['schemas']['ImportBatchDto'];
export type ImportBatchDetail = components['schemas']['ImportBatchDetailDto'];
export type ImportProvenance = components['schemas']['ImportProvenanceDto'];
export type GeofileSourceOptions = components['schemas']['GeofileSourceOptions'];

export function useTermRuleSets() {
  return useQuery({
    queryKey: queryKeys.termRuleSets,
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets')),
  });
}

/** The set an import starts from when the reviewer names none. */
export function useEffectiveTermRuleSet() {
  return useQuery({
    queryKey: queryKeys.effectiveTermRuleSet,
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets/effective')),
  });
}

export function useTermRuleSet(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.termRuleSet(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

/**
 * The whole prefix, not one row: which set applies to somebody is derived from all of them,
 * so promoting one changes what another account's next import starts from.
 */
function useInvalidateTermRuleSets() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['term-rule-sets'] });
}

export function useCreateTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (body: TermRuleSetCreate) => unwrap(api.POST('/api/v1/term-rule-sets', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TermRuleSetWrite }) =>
      unwrap(api.PUT('/api/v1/term-rule-sets/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/term-rule-sets/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

export function useSetTermRuleSetScope() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TermRuleSetScopeWrite }) =>
      unwrap(api.POST('/api/v1/term-rule-sets/{id}/scope', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useImportTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (body: TermRuleSetImport) =>
      unwrap(api.POST('/api/v1/term-rule-sets/import', { body })),
    onSuccess: invalidate,
  });
}

export function useImportSession(geofileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.importSession(geofileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/geofiles/{geofileId}/import/session', {
          params: { path: { geofileId: geofileId! } },
        }),
      ),
    enabled: Boolean(geofileId),
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session
 * query: the browser already holds what it just sent, and refetching would make every tick
 * of the table fight the answer coming back.
 */
export function useSaveImportSession() {
  return useMutation({
    mutationFn: ({
      geofileId,
      body,
    }: {
      geofileId: string;
      body: { options: ImportOptions; decisions: Record<string, ImportDecision> };
    }) =>
      unwrap(
        api.PUT('/api/v1/geofiles/{geofileId}/import/session', {
          params: { path: { geofileId } },
          body,
        }),
      ),
  });
}

/** The dry run. A POST because the options are a body, but it creates nothing. */
export function useImportPreview(
  geofileId: string | undefined,
  body: ImportPreviewRequest,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.importPreview(geofileId ?? '', body),
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/geofiles/{geofileId}/import/preview', {
          params: { path: { geofileId: geofileId! } },
          body,
        }),
      ),
    enabled: Boolean(geofileId) && enabled,
    placeholderData: keepPreviousData,
  });
}

export function useCommitImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      geofileId,
      body,
    }: {
      geofileId: string;
      body: {
        options: ImportOptions;
        selection: number[];
        decisions: Record<string, ImportDecision>;
        withoutReview: boolean;
      };
    }) =>
      unwrap(
        api.POST('/api/v1/geofiles/{geofileId}/import/commit', {
          params: { path: { geofileId } },
          body,
        }),
      ),
    onSuccess: () => {
      // A confirmation puts caves, entrances and features into the registry and spends the
      // review that produced them, so four surfaces go stale at once.
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['import-session'] });
    },
  });
}

// ---------------------------------------------------------------------------
// Photographs into the registry
// ---------------------------------------------------------------------------

export type PhotoImportOptions = components['schemas']['PhotoImportOptions'];
export type PhotoDecision = components['schemas']['PhotoDecision'];
export type PhotoCandidate = components['schemas']['PhotoCandidateDto'];
export type PhotoCandidateMember = components['schemas']['PhotoCandidateMemberDto'];
export type PhotoNearby = components['schemas']['PhotoNearbyDto'];
export type PhotoPreview = components['schemas']['PhotoPreviewDto'];
export type PhotoPreviewRequest = components['schemas']['PhotoPreviewRequest'];
export type PhotoSession = components['schemas']['PhotoSessionDto'];
export type PhotoTrackOption = components['schemas']['PhotoTrackOptionDto'];
export type PhotoPosition = components['schemas']['PhotoPositionDto'];
export type PhotoPositionSource = components['schemas']['PhotoPositionSource'];
export type PhotoExif = components['schemas']['PhotoExif'];
export type PositionConfidenceBand = components['schemas']['PositionConfidenceBand'];

export function usePhotoImportSession() {
  return useQuery({
    queryKey: queryKeys.photoImportSession,
    queryFn: () => unwrap(api.GET('/api/v1/photo-import/session')),
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session query,
 * for the same reason the vector one does not: the browser already holds what it just sent.
 */
export function useSavePhotoImportSession() {
  return useMutation({
    mutationFn: (body: {
      fileIds: string[];
      options: PhotoImportOptions;
      decisions: Record<string, PhotoDecision>;
    }) => unwrap(api.PUT('/api/v1/photo-import/session', { body })),
  });
}

/** Groups the drop into places. A POST because the options are a body, but it creates nothing. */
export function usePhotoImportPreview(body: PhotoPreviewRequest, enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoImportPreview(body),
    queryFn: () => unwrap(api.POST('/api/v1/photo-import/preview', { body })),
    enabled: enabled && body.fileIds.length > 0,
    placeholderData: keepPreviousData,
  });
}

export function useCommitPhotoImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      options: PhotoImportOptions;
      fileIds: string[];
      selection: string[];
      decisions: Record<string, PhotoDecision>;
    }) => unwrap(api.POST('/api/v1/photo-import/commit', { body })),
    onSuccess: () => {
      // A confirmation creates objects, hangs pictures on them and spends the review that
      // produced them, so every surface built from any of those goes stale at once.
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['attachments'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['photo-import-session'] });
    },
  });
}

/** Uploaded tracks a picture with no fix of its own can be placed against. */
export function usePhotoImportTracks(enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoImportTracks,
    queryFn: () => unwrap(api.GET('/api/v1/photo-import/tracks')),
    enabled,
  });
}

/**
 * Gives a picture a position by hand, or forgets one. `replaceRecordedFix` has to be true when
 * the camera recorded a fix — the server refuses otherwise, so that a drag cannot quietly
 * replace a measurement.
 */
export function useSetPhotoPosition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      position,
      replaceRecordedFix,
    }: {
      id: string;
      position: number[] | null;
      replaceRecordedFix: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/files/{id}/position', {
          params: { path: { id } },
          body: { position, replaceRecordedFix },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['attachments'] });
      void queryClient.invalidateQueries({ queryKey: ['photo-import-preview'] });
    },
  });
}

/**
 * Moves an object to where one of its pictures was taken. An ordinary geometry write: it needs
 * write access to the object and lands in that object's own history.
 */
export function useFeaturePositionFromPhoto() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ featureId, fileId }: { featureId: string; fileId: string }) =>
      unwrap(
        api.POST('/api/v1/features/{id}/position-from-photo', {
          params: { path: { id: featureId } },
          body: { fileId },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['history'] });
    },
  });
}

export interface ImportBatchListParams {
  geofileId?: string;
  page?: number;
  pageSize?: number;
}

export function useImportBatches(params: ImportBatchListParams) {
  return useQuery({
    queryKey: queryKeys.importBatches(params),
    queryFn: () => unwrap(api.GET('/api/v1/import-batches', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useImportBatch(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.importBatch(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/import-batches/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

export function useRevertImportBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/import-batches/{id}/revert', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
    },
  });
}

/**
 * Where an object came from. Absent for anything nobody imported, which is most of them —
 * hence no retry: a 404 here is the normal answer, not a failure worth asking again about.
 */
export function useImportProvenance(featureId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.importProvenance(featureId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{featureId}/import-provenance', {
          params: { path: { featureId: featureId! } },
        }),
      ),
    enabled: Boolean(featureId) && enabled,
    retry: false,
  });
}

export function useGeofile(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.geofile(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/geofiles/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

/**
 * The header of a delimited upload, so a wrong coordinate-column guess can be corrected.
 * Only a delimited upload has one — callers must not ask about anything else, because the
 * refusal is a browser console error on every review of a GPX.
 */
export function useGeofileColumns(geofileId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.geofileColumns(geofileId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/geofiles/{id}/columns', { params: { path: { id: geofileId! } } })),
    enabled: Boolean(geofileId) && enabled,
    retry: false,
  });
}

export function useReimportGeofile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, sourceOptions }: { id: string; sourceOptions?: GeofileSourceOptions }) =>
      unwrap(
        api.POST('/api/v1/geofiles/{id}/reimport', {
          params: { path: { id } },
          body: { sourceOptions: sourceOptions ?? null },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['geofiles'] });
      void queryClient.invalidateQueries({ queryKey: ['import-preview'] });
      void queryClient.invalidateQueries({ queryKey: ['geofile-columns'] });
    },
  });
}

export interface UnfiledDocumentParams {
  uploadBatchId?: string;
  page?: number;
  pageSize?: number;
}

/**
 * The inbox: documents filed nowhere.
 *
 * "Unfiled" is a query rather than a shelf — a document with no cabinet row — which is what
 * keeps filing a pure addition and leaving the inbox automatic. Nothing here is a way to see
 * anything: it is filtered by the same read rule as every other document listing, and an
 * unfiled document starts private because it has no cabinet for a rule to reach it through.
 */
export function useUnfiledDocuments(params: UnfiledDocumentParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.unfiledDocuments(params),
    queryFn: () => unwrap(api.GET('/api/v1/documents/unfiled', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Files and unfiles many documents at once.
 *
 * The answer is a partial result rather than all-or-nothing, and the caller has to read it: a
 * selection of two hundred documents will, on a real archive, contain one somebody else owns.
 */
export function useBulkFiling() {
  const invalidate = useInvalidateCabinets();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: {
      documentIds: string[];
      fileIntoCabinetIds?: string[];
      unfileFromCabinetIds?: string[];
    }) =>
      unwrap(api.POST('/api/v1/cabinets/filing', {
        // Both lists are always sent: the contract distinguishes "no shelves named" from
        // "this field was not mentioned", and only the first of those is a thing to ask for.
        body: {
          documentIds: request.documentIds,
          fileIntoCabinetIds: request.fileIntoCabinetIds ?? [],
          unfileFromCabinetIds: request.unfileFromCabinetIds ?? [],
        },
      })),
    onSuccess: () => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });
}

/** The caller's own drops, newest first. */
export function useUploadBatches(page = 1, pageSize = 20, enabled = true) {
  return useQuery({
    queryKey: queryKeys.uploadBatches(page, pageSize),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches', { params: { query: { page, pageSize } } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * One drop and its counts.
 *
 * Polled while the work is still running, because an archive expansion and a directory import
 * happen in the background and the page has nothing else to learn from.
 */
export function useUploadBatch(id: string | undefined, poll = false) {
  return useQuery({
    queryKey: queryKeys.uploadBatch(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
    refetchInterval: poll ? 2000 : false,
    retry: false,
  });
}

export interface UploadBatchItemParams {
  outcome?: 'pending' | 'stored' | 'skipped' | 'failed';
  page?: number;
  pageSize?: number;
}

/** The per-file report of one drop. */
export function useUploadBatchItems(
  id: string | undefined,
  params: UploadBatchItemParams = {},
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.uploadBatchItems(id ?? '', params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches/{id}/items', {
        params: { path: { id: id! }, query: params },
      })),
    enabled: Boolean(id) && enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** Opens a drop, so everything uploaded into it can be found together afterwards. */
export function useOpenUploadBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: { label?: string | null; cabinetId?: string | null; tagName?: string | null }) =>
      unwrap(api.POST('/api/v1/upload-batches', {
        body: {
          label: request.label ?? null,
          cabinetId: request.cabinetId ?? null,
          tagName: request.tagName ?? null,
        },
      })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['upload-batches'] }),
  });
}

/** Closes a drop; nothing more can be counted into it. */
export function useCloseUploadBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/upload-batches/{id}/close', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['upload-batches'] }),
  });
}

/**
 * The directories this installation may import from.
 *
 * Empty means the operator has not switched the feature on — which the page states rather than
 * offering a field that refuses everything.
 */
export function useImportRoots(enabled = true) {
  return useQuery({
    queryKey: queryKeys.importRoots,
    queryFn: () => unwrap(api.GET('/api/v1/upload-batches/import-roots')),
    enabled,
    retry: false,
    staleTime: 60 * 60_000,
  });
}

/** Starts a background import from a directory the server itself can reach. */
export function useImportDirectory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: {
      path: string;
      label?: string | null;
      cabinetId?: string | null;
      tagName?: string | null;
    }) =>
      unwrap(api.POST('/api/v1/upload-batches/import-directory', {
        body: {
          path: request.path,
          label: request.label ?? null,
          cabinetId: request.cabinetId ?? null,
          tagName: request.tagName ?? null,
        },
      })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['upload-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
    },
  });
}

/** What the gallery can be narrowed by. Every field is optional and they combine. */
export interface PhotoQueryParams {
  caveId?: string;
  featureId?: string;
  tripLogId?: string;
  caverId?: string;
  tagId?: number;
  albumId?: string;
  uploadBatchId?: string;
  camera?: string;
  /** The map extent, as west,south,east,north. */
  bbox?: string;
  unplaced?: boolean;
  from?: string;
  to?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

/**
 * The gallery.
 *
 * Page-sized queries rather than one growing list: a club archive runs to tens of thousands of
 * photographs, and the grid is what decides how many are on screen.
 */
export function usePhotos(params: PhotoQueryParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.photos(params),
    queryFn: () => unwrap(api.GET('/api/v1/photos', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function usePhoto(documentId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.photo(documentId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/photos/{documentId}', { params: { path: { documentId: documentId! } } })),
    enabled: Boolean(documentId),
    retry: false,
  });
}

/** Everything a photograph, an album or an attachment listing might now show differently. */
function useInvalidatePhotos() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['photos'] });
    void queryClient.invalidateQueries({ queryKey: ['albums'] });
    void queryClient.invalidateQueries({ queryKey: ['attachments'] });
    void queryClient.invalidateQueries({ queryKey: ['documents'] });
  };
}

export function useUpdatePhotoCredit() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ documentId, ...body }: {
      documentId: string;
      photographerCaverId?: string | null;
      photographerName?: string | null;
      caption?: string | null;
      licenceCode?: string | null;
      placeName?: string | null;
    }) =>
      unwrap(api.PUT('/api/v1/photos/{documentId}/credit', {
        params: { path: { documentId } },
        body: {
          photographerCaverId: body.photographerCaverId ?? null,
          photographerName: body.photographerName ?? null,
          caption: body.caption ?? null,
          licenceCode: body.licenceCode ?? null,
          placeName: body.placeName ?? null,
        },
      })),
    onSuccess: () => invalidate(),
  });
}

/**
 * One operation over a selection.
 *
 * The answer is a partial result and callers have to read it: a selection of two hundred
 * photographs will, on a real archive, contain one somebody else owns.
 */
export function usePhotoBulk() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: (body: {
      documentIds: string[];
      addTagIds?: number[];
      removeTagIds?: number[];
      visibility?: string;
      rotateQuarterTurns?: number;
      attachEntityType?: string;
      attachEntityId?: string;
      addToAlbumId?: string;
      delete?: boolean;
    }) => unwrap(api.POST('/api/v1/photos/bulk', { body: body as never })),
    onSuccess: () => invalidate(),
  });
}

export function usePhotoDuplicates(enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoDuplicates,
    queryFn: () => unwrap(api.GET('/api/v1/photos/duplicates')),
    enabled,
    retry: false,
  });
}

export function useDeletedPhotos(page = 1, enabled = true) {
  return useQuery({
    queryKey: queryKeys.deletedPhotos(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/photos/deleted', { params: { query: { page, pageSize: 50 } } })),
    enabled,
    retry: false,
  });
}

export function useRestorePhoto() {
  const invalidate = useInvalidatePhotos();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (documentId: string) =>
      unwrapVoid(api.POST('/api/v1/photos/{documentId}/restore', {
        params: { path: { documentId } },
      })),
    onSuccess: () => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: ['photos', 'deleted'] });
    },
  });
}

/** Publishing to the internet — a full administrator's decision, and not the read audience. */
export function useSetPhotoPublic() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ documentId, published }: { documentId: string; published: boolean }) =>
      unwrapVoid(api.PUT('/api/v1/photos/{documentId}/public', {
        params: { path: { documentId }, query: { published } },
      })),
    onSuccess: () => invalidate(),
  });
}

export interface AlbumListParams {
  subjectEntityId?: string;
  page?: number;
  pageSize?: number;
}

export function useAlbums(params: AlbumListParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.albums(params),
    queryFn: () => unwrap(api.GET('/api/v1/albums', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function useAlbum(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.album(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/albums/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
    retry: false,
  });
}

export interface AlbumWrite {
  title: string;
  description?: string | null;
  visibility: string;
  cavingGroupId?: string | null;
  subjectEntityType?: string | null;
  subjectEntityId?: string | null;
}

function albumBody(body: AlbumWrite) {
  return {
    title: body.title,
    description: body.description ?? null,
    visibility: body.visibility,
    cavingGroupId: body.cavingGroupId ?? null,
    subjectEntityType: body.subjectEntityType ?? null,
    subjectEntityId: body.subjectEntityId ?? null,
  } as never;
}

function useInvalidateAlbums() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['albums'] });
}

export function useCreateAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (body: AlbumWrite) => unwrap(api.POST('/api/v1/albums', { body: albumBody(body) })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: ({ id, ...body }: AlbumWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/albums/{id}', { params: { path: { id } }, body: albumBody(body) })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/albums/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function useAddAlbumItems() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentIds }: { id: string; documentIds: string[] }) =>
      unwrapVoid(api.POST('/api/v1/albums/{id}/items', {
        params: { path: { id } },
        body: documentIds as never,
      })),
    onSuccess: () => invalidate(),
  });
}

export function useRemoveAlbumItem() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentId }: { id: string; documentId: string }) =>
      unwrapVoid(api.DELETE('/api/v1/albums/{id}/items/{documentId}', {
        params: { path: { id, documentId } },
      })),
    onSuccess: () => invalidate(),
  });
}

/** Moves a picture to sit after another, or to the start when nothing is named. */
export function useReorderAlbum() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentId, afterDocumentId }: {
      id: string;
      documentId: string;
      afterDocumentId?: string | null;
    }) =>
      unwrapVoid(api.POST('/api/v1/albums/{id}/reorder', {
        params: { path: { id } },
        body: { documentId, afterDocumentId: afterDocumentId ?? null },
      })),
    onSuccess: () => invalidate(),
  });
}

export function useSetAlbumCover() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: ({ id, documentId }: { id: string; documentId: string | null }) =>
      unwrapVoid(api.PUT('/api/v1/albums/{id}/cover', {
        params: { path: { id }, query: documentId ? { documentId } : {} },
      })),
    onSuccess: () => invalidate(),
  });
}

/** Mints a share link. The token comes back once and is never recoverable afterwards. */
export function useShareAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/albums/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function useRevokeAlbumShare() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/albums/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** The object's headline picture — the one a list, a card or a popup shows. */
export function useSetPrimaryAttachment() {
  const invalidate = useInvalidatePhotos();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, primary }: { id: string; primary: boolean }) =>
      unwrapVoid(api.PUT('/api/v1/attachments/{id}/primary', {
        params: { path: { id }, query: { primary } },
      })),
    onSuccess: () => {
      invalidate();
      // The cave summary carries the headline picture, so the card and the panel beside the map
      // both read a stale one until this is dropped.
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
    },
  });
}

/** The installation's curated public gallery. Reachable without signing in. */
export function usePublicPhotos(page = 1) {
  return useQuery({
    queryKey: queryKeys.publicPhotos(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/photos', { params: { query: { page, pageSize: 60 } } })),
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** One album, opened by its share link. Reachable without signing in. */
export function useSharedAlbum(token: string | undefined) {
  return useQuery({
    queryKey: queryKeys.sharedAlbum(token ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/albums/{token}', { params: { path: { token: token! } } })),
    enabled: Boolean(token),
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// The one filter route, and the two that describe it
// ---------------------------------------------------------------------------

export type FilterQueryBody = components['schemas']['FilterQueryRequest'];
export type FilterQueryResult = components['schemas']['FilterQueryResponse'];
export type FilterVocabularyResult = components['schemas']['FilterVocabularyResponse'];
export type FilterHitDto = components['schemas']['FilterHitDto'];

/**
 * What each kind of object can be asked, for this caller.
 *
 * Cached hard. It changes when an administrator edits the taxonomy, not while somebody is typing,
 * and every control that draws a filter reads it — so refetching per mount would put a request
 * behind every dropdown on the page.
 */
export function useFilterVocabulary() {
  return useQuery({
    queryKey: queryKeys.filterVocabulary,
    queryFn: () => unwrap(api.GET('/api/v1/filters/vocabulary')),
    staleTime: 10 * 60_000,
  });
}

/**
 * Runs a filter.
 *
 * `keepPreviousData` is what makes a type-ahead readable: without it the list empties on every
 * keystroke and the rows jump, which reads as the control losing what it had found.
 */
export function useFilterQuery(body: FilterQueryBody, enabled = true) {
  return useQuery({
    queryKey: queryKeys.filterQuery(body),
    queryFn: () => unwrap(api.POST('/api/v1/filters/query', { body })),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/**
 * Describes rows somebody already chose.
 *
 * An id the caller may not see comes back missing rather than refused, so the caller of this hook
 * must treat an absent row as "cannot show this" — never as an error, and never by falling back to
 * printing the identifier, which would put a raw uuid on screen where a name belongs.
 */
export function useFilterResolve(world: string, ids: readonly string[]) {
  return useQuery({
    queryKey: queryKeys.filterResolve(world, ids),
    queryFn: () => unwrap(api.POST('/api/v1/filters/resolve', {
      body: { world, ids: [...ids] },
    })),
    enabled: ids.length > 0,
    // Names change rarely and this is asked every time a form with a stored value opens.
    staleTime: 5 * 60_000,
  });
}

// ---------------------------------------------------------------------------
// Trip statistics
// ---------------------------------------------------------------------------

/**
 * What a person, a cave, a club or a camp adds up to across trips.
 *
 * Every figure is counted over the trips the caller may read, so two people legitimately see
 * different totals for the same subject. That is a fact about the answer, not about the request —
 * whatever shows these has to say so on the screen beside them, or the difference is reported as a
 * bug and repaired by removing the filter.
 */
export type TripStatistics = components['schemas']['TripStatisticsDto'];

/** Which thing is being added up. */
export type StatisticsSubject = 'caver' | 'cave' | 'cavingGroup' | 'expedition';

export function useTripStatistics(
  subject: StatisticsSubject,
  id: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripStatistics(subject, id ?? ''),
    queryFn: () => {
      const path = { id: id! };
      // A named branch per subject rather than a trailing fallthrough: a subject added to the
      // union without a branch of its own would otherwise be asked about as a club, and answered.
      switch (subject) {
        case 'caver':
          return unwrap(api.GET('/api/v1/stats/cavers/{id}', { params: { path } }));
        case 'cave':
          return unwrap(api.GET('/api/v1/stats/caves/{id}', { params: { path } }));
        case 'cavingGroup':
          return unwrap(api.GET('/api/v1/stats/caving-groups/{id}', { params: { path } }));
        case 'expedition':
          return unwrap(api.GET('/api/v1/stats/expeditions/{id}', { params: { path } }));
      }
    },
    enabled: enabled && !!id,
    // Derived on every request from trips that change slowly; a page revisited within the minute
    // does not need to ask again.
    staleTime: 30_000,
    // A caller who may not read the subject is refused, and the surface simply does not appear.
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// Expeditions
// ---------------------------------------------------------------------------

export type ExpeditionInfo = components['schemas']['ExpeditionDto'];

/**
 * How the camp list is narrowed. The window asks what a camp overlapped rather than what it
 * started inside, so a fortnight camp running across the end of a month is in both months; the
 * word is looked for in the name; the state is one of the camp lifecycle's own, and a word the
 * server does not have is refused rather than ignored.
 */
export interface ExpeditionListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  search?: string;
  state?: string;
}

/** Camps, most recent first, narrowed by the filters the list offers. */
export function useExpeditions(params: ExpeditionListParams = {}) {
  return useQuery({
    queryKey: queryKeys.expeditions(params),
    queryFn: () => unwrap(api.GET('/api/v1/expeditions', { params: { query: params } })),
    // Paging or retyping a filter keeps the rows on screen while the next answer arrives, rather
    // than emptying the table under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

/**
 * One camp. A camp the caller may not read answers exactly as one that does not exist does —
 * the server spells both `expedition.not_found` — so the page has no way to tell them apart and
 * must not try: an address that answered differently for the two would be an address anybody
 * could probe for the existence of a camp they cannot see.
 */
export function useExpedition(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expedition(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/expeditions/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    // A refusal here is a settled answer about the caller, not a transient failure: retrying it
    // three times only delays the page saying so.
    retry: false,
  });
}

export type ExpeditionRoster = components['schemas']['ExpeditionRosterDto'];
export type ExpeditionRosterEntry = components['schemas']['ExpeditionRosterEntryDto'];
export type ExpeditionRosterRole = components['schemas']['ExpeditionRosterRoleDto'];

/** What somebody may be recorded as having been at a camp as. Seeded rows plus an installation's own. */
export function useExpeditionRosterRoles() {
  return useQuery({
    queryKey: queryKeys.taxonomy('expedition-roster-roles'),
    queryFn: () => unwrap(api.GET('/api/v1/expedition-roster-roles')),
    staleTime: 5 * 60_000,
  });
}

/**
 * Who was at a camp, and for which days.
 *
 * Two rights, not one: the right to read the camp *and* the right to read people. A caller
 * holding the first and not the second is refused outright, with a code of its own, rather than
 * being handed rows with the names struck out — a struck-out list still says how many people were
 * there and when. That refusal is a designed answer and the surface showing it renders it as a
 * state of the page, which is why it must not be retried: the identical request cannot produce
 * anything else, and three attempts only hold the screen in its loading state meanwhile.
 */
export function useExpeditionRoster(expeditionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expeditionRoster(expeditionId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/expeditions/{expeditionId}/roster', {
          params: { path: { expeditionId: expeditionId! } },
        }),
      ),
    enabled: !!expeditionId,
    retry: false,
  });
}

/**
 * Everything one camp draws on a map, as one answer.
 *
 * Three kinds of shape come back together — the camp's working area, the sketches of the member
 * trips this caller may read, and the entrances of the caves those trips name whose positions
 * this caller may see — because they are governed by three different rules and only the server
 * can apply them. Nothing here filters what arrives: whatever reached the client was cleared to.
 *
 * Fetched only once the map tab is the one on screen, so opening a camp does not pay for a map
 * nobody looked at.
 */
export function useExpeditionMap(expeditionId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.expeditionMap(expeditionId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/expeditions/{id}/map', { params: { path: { id: expeditionId! } } })),
    enabled: !!expeditionId && enabled,
    retry: false,
  });
}

export type ExpeditionLeads = components['schemas']['ExpeditionLeadsDto'];
export type ExpeditionLeadGroup = components['schemas']['ExpeditionLeadGroupDto'];
export type ExpeditionLead = components['schemas']['ExpeditionLeadDto'];

/**
 * What a camp's trips left open, grouped by whether each way on is still going.
 *
 * Nothing here is derived on the client and nothing here is filtered on it: which leads are on
 * the board is decided per lead on the server, against the same rule any single place is judged
 * by, and a lead this caller may not place exactly never arrives. So the board is drawn from what
 * came back and the count beside it is the count of what came back — two readers of the same camp
 * see different boards, and the page says so rather than leaving the numbers to imply otherwise.
 */
export function useExpeditionLeads(expeditionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expeditionLeads(expeditionId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/expeditions/{id}/leads', { params: { path: { id: expeditionId! } } })),
    enabled: !!expeditionId,
    retry: false,
  });
}
